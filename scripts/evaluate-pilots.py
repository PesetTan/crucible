#!/usr/bin/env python3
"""Run real Forge diagnostic positions and atomically publish an attributable scorecard."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]
DIMENSIONS = {
    "aggression": ("Aggression", "Attack tendency in an equal-trade position; not a quality score."),
    "combat": ("Combat judgment", "Unblocked attack, immediate lethal, and a losing attack. Attack decisions only; blocking is unmeasured."),
    "resources": ("Resource management", "Payment, copy-value, or resource-preservation checks applicable to this profile."),
    "interaction": ("Interaction timing", "Reviewed burn, sweeper, exile, or own-main permanent decisions. General counterspell and protection timing is unmeasured."),
    "combo": ("Combo choices", "Cast and uncast copier choices. Full combo execution is unmeasured."),
    "adaptation": ("Adaptation", "Public opponent classifier guards. Strategic adaptation in complete games is unmeasured."),
}
SOURCES = ["tests/PilotAttributeChecks.java", "tests/AggroIntegrationChecks.java", "tests/ControlIntegrationChecks.java",
           "tests/SubtypePilotChecks.java", "tests/ReanimatorCopyManaChecks.java"]
EVIDENCE_SOURCES = SOURCES + ["scripts/evaluate-pilots.py"]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def summarize(cases, profiles):
    cards = []
    for profile in profiles:
        dimensions = []
        for key, (label, scope) in DIMENSIONS.items():
            rows = [c for c in cases if c["profile"] == profile and c["dimension"] == key]
            errors = sum(c["status"] == "error" for c in rows)
            observations = [c for c in rows if c["status"] == "observed"]
            checks = [c for c in rows if c["status"] in ("passed", "failed")]
            total = len(observations) if key == "aggression" else len(checks)
            passed = sum(c["actual"] == "attack" for c in observations) if key == "aggression" else sum(c["status"] == "passed" for c in checks)
            dimensions.append(dict(key=key, label=label, scope=scope, measured=total > 0,
                                   kind="Observed tendency" if key == "aggression" else "Diagnostic checks",
                                   passed=passed, total=total, errors=errors,
                                   percent=100.0 * passed / total if total else None,
                                   origins=sorted({c["origin"] for c in rows}), cases=rows))
        cards.append(dict(profile=profile, dimensions=dimensions))
    return cards

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", help="An existing immutable bundle ID; defaults to active")
    args = parser.parse_args()
    bundle_id = args.bundle or json.loads((ROOT / "data/engine-bundles/active.json").read_text())["id"]
    if len(bundle_id) != 129 or bundle_id[64] != "-" or any(c not in "0123456789abcdef" for c in bundle_id.replace("-", "")):
        raise ValueError("Invalid immutable bundle ID")
    bundle = ROOT / "data/engine-bundles" / bundle_id
    manifest = json.loads((bundle / "bundle.json").read_text())
    if manifest["engineHash"] + "-" + manifest["adapterHash"] != bundle_id:
        raise ValueError("Bundle identity does not match manifest")
    classes = sorted((bundle / "bridge").glob("*.class"))
    if hashlib.sha256("\n".join(digest(p) for p in classes).encode()).hexdigest() != manifest["adapterHash"]:
        raise ValueError("Bundle bridge checksum failed")
    jar = bundle / manifest["jarName"]
    resources = [jar]
    for folder in ["cardsfolder", "tokenscripts", "editions", "ai", "formats", "effects", "defaults"]:
        resources.extend(p for p in (bundle / "res" / folder).rglob("*") if p.is_file())
    engine_hash = hashlib.sha256("\n".join(str(p.relative_to(bundle)) + ":" + digest(p) for p in sorted(resources)).encode()).hexdigest()
    if engine_hash != manifest["engineHash"]:
        raise ValueError("Bundle engine/resource checksum failed")
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
    out = ROOT / "data/pilot-evaluations" / stamp
    (out / "classes").mkdir(parents=True)
    registry = ROOT / "data/reports/subtype-pilots-v001/registry.json"
    # Freeze the reviewed fixture inventory; these annotations are fixtures, not live catalog data.
    (out / "registry.json").write_bytes(registry.read_bytes())
    source_hashes = {name: digest(ROOT / name) for name in EVIDENCE_SOURCES}
    for name in EVIDENCE_SOURCES:
        snapshot = out / "sources" / name
        snapshot.parent.mkdir(parents=True, exist_ok=True)
        snapshot.write_bytes((ROOT / name).read_bytes())
        if digest(snapshot) != source_hashes[name]:
            raise ValueError("Evaluation source changed while freezing")
    # Only expose implementation source when compiling it reproduces the tested bridge byte-for-byte.
    implementation = ROOT / "engine/ForgeBridge.java"
    implementation_snapshot = out / "sources/engine/ForgeBridge.java"
    implementation_snapshot.parent.mkdir(parents=True, exist_ok=True)
    implementation_snapshot.write_bytes(implementation.read_bytes())
    implementation_classes = out / "implementation-classes"
    implementation_classes.mkdir()
    verification = subprocess.run([str(ROOT / "vendor/jdk/Contents/Home/bin/javac"), "-cp", str(jar),
                                   "-d", str(implementation_classes), str(implementation_snapshot)], capture_output=True, timeout=120)
    expected_classes = {p.name: digest(p) for p in classes}
    reproduced_classes = {p.name: digest(p) for p in implementation_classes.glob("*.class")}
    implementation_verified = verification.returncode == 0 and reproduced_classes == expected_classes
    if implementation_verified:
        source_hashes["engine/ForgeBridge.java"] = digest(implementation_snapshot)
    else:
        implementation_snapshot.unlink()
    java = ROOT / "vendor/jdk/Contents/Home/bin/java"
    javac = java.with_name("javac")
    cp = os.pathsep.join(map(str, [out / "classes", bundle / "bridge", jar]))
    subprocess.run([str(javac), "-cp", cp, "-d", str(out / "classes"), *[str(ROOT / name) for name in SOURCES]], check=True, timeout=120)
    with (out / "classification-checks.log").open("wb") as classification_log:
        subprocess.run([str(java), "-cp", cp, "PilotAttributeChecks", "--classification-checks"],
                       stdout=classification_log, stderr=subprocess.STDOUT, check=True, timeout=30)
    bridge_bytes = (bundle / "bridge/ForgeBridge.class").read_bytes()
    revisions = [str(v) for v in (9, 10) if all(("reanimator-" + variant + "-v" + str(v)).encode() in bridge_bytes for variant in ("terror", "sultai", "cryomancer", "shared"))]
    if b"reanimator-v011" in bridge_bytes: revisions.append("11")
    if not revisions:
        raise ValueError("No supported Reanimator revision detected in the pinned bridge")
    started = dt.datetime.now(dt.timezone.utc).isoformat()
    with (out / "execution.log").open("wb") as log:
        subprocess.run([str(java), "-Xmx1400m", "-Djava.awt.headless=true", "-Duser.home=" + str(ROOT / "data/engine-home"),
                        "-cp", cp, "PilotAttributeChecks", str(out / "registry.json"), str(out / "positions.json"), ",".join(revisions)],
                       cwd=bundle, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=600)
    if source_hashes != {name: digest(ROOT / name) for name in source_hashes}:
        raise ValueError("Evaluation sources changed while running; report not published")
    raw = json.loads((out / "positions.json").read_text())
    cases = raw["cases"]
    identities = [(c["profile"], c["dimension"], c["id"]) for c in cases]
    if len(set(identities)) != len(identities) or any(c["status"] not in ("passed", "failed", "observed", "error") for c in cases):
        raise ValueError("Invalid or duplicate fixture results")
    profiles = sorted({c["profile"] for c in cases})
    report = dict(version=1, id=stamp, startedAt=started, completedAt=dt.datetime.now(dt.timezone.utc).isoformat(),
                  engineHash=manifest["engineHash"], adapterHash=manifest["adapterHash"], bundleId=bundle_id,
                  fixtureSeed=20261013, baseProfile="Default", settings=raw["settings"], sourceHashes=source_hashes,
                  fixtureInventoryHash=digest(out / "registry.json"), rawEvidenceHash=digest(out / "positions.json"),
                  logHash=digest(out / "execution.log"), jarHash=digest(jar),
                  fixtureClassHashes={p.name: digest(p) for p in sorted((out / "classes").glob("*.class"))},
                  implementationSourceVerified=implementation_verified,
                  scope="Synthetic diagnostic positions with fixed fixture annotations; not complete games, live catalog ratings, or Arena forecasts.",
                  profiles=summarize(cases, profiles))
    report_path = out / "scorecard.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n")
    latest = dict(directory=stamp, sha256=digest(report_path))
    temporary = out.parent / (".latest-" + uuid.uuid4().hex + ".json")
    temporary.write_text(json.dumps(latest) + "\n")
    temporary.replace(out.parent / "latest.json")
    print(json.dumps(dict(report=str(report_path), profiles=len(profiles), positions=len(cases),
                          failed=sum(c["status"] == "failed" for c in cases), errors=sum(c["status"] == "error" for c in cases))))

if __name__ == "__main__":
    main()
