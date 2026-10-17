#!/usr/bin/env python3
"""Scriptable client for Crucible's local pilot optimization service."""
import argparse
import json
import os
from pathlib import Path
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

def client(base, method, path, value=None, key=None):
    origin = urllib.parse.urlparse(base)
    if origin.scheme != "http" or origin.hostname not in ("127.0.0.1", "localhost", "::1") or origin.username or origin.password or origin.query or origin.fragment:
        raise ValueError("Use a local HTTP Crucible service origin")
    headers = {"Content-Type": "application/json"}
    if method != "GET":
        with urllib.request.urlopen(base.rstrip("/") + "/api/v1/session", timeout=10) as response:
            headers["X-Instance-Token"] = json.load(response)["instanceToken"]
        if key:
            headers["Idempotency-Key"] = key
    payload = json.dumps(value).encode() if value is not None else None
    request = urllib.request.Request(base.rstrip("/") + path, data=payload, headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=600) as response:
        return json.load(response)

def main():
    if len(sys.argv) == 3 and sys.argv[1] == "--internal":
        from pilot_optimizer import Backend
        import signal
        backend = Backend(os.environ["MTG_PROJECT_ROOT"])
        signal.signal(signal.SIGTERM, lambda *_: setattr(backend, "stop", True))
        try:
            request = json.load(sys.stdin)
            pyre = request.get("providerId") == "pyre"
            if request.get("id"):
                pyre = backend.exp(request["id"]).get("providerId") == "pyre"
            if pyre:
                import pilot_optimizer as base_module
                from pyre_optimizer import make_backend
                backend = make_backend(base_module)(os.environ["MTG_PROJECT_ROOT"])
            # An experiment executes its verified backend snapshot across service upgrades.
            if request.get("id") and sys.argv[2] not in ("recover", "create", "inspect", "list"):
                import importlib.util
                from pilot_optimizer import digest, no_links
                experiment=backend.exp(request["id"])
                if sys.argv[2]=="promote" and not pyre:
                    from pilot_optimizer import catalog_publication_supported
                    candidate=backend.candidate(request["id"],request["candidateId"])
                    catalog_path=backend.root/"data/engine-bundles"/backend.frozen(request["id"])["bundleId"]/"protocol-catalog.json"
                    baseline_catalog=json.loads(catalog_path.read_text()) if catalog_path.exists() else None
                    if not catalog_publication_supported(experiment,candidate,baseline_catalog):
                        raise ValueError("This frozen experiment predates catalog-aware publication. Its publisher would retain a stale catalog. Preserve its evidence and create a catalog-aware experiment before promotion.")
                snapshot=no_links(backend.folder(request["id"])/"backend.py")
                if digest(snapshot)!=experiment["evidenceHashes"]["backend.py"]:
                    raise ValueError("Frozen backend checksum mismatch")
                module_spec=importlib.util.spec_from_file_location("frozen_pilot_optimizer",snapshot)
                frozen_module=importlib.util.module_from_spec(module_spec)
                module_spec.loader.exec_module(frozen_module)
                if pyre:
                    adapter=no_links(backend.folder(request["id"])/"pyre-backend.py")
                    if digest(adapter)!=experiment["evidenceHashes"]["pyre-backend.py"]:
                        raise ValueError("Frozen Pyre adapter checksum mismatch")
                    spec=importlib.util.spec_from_file_location("frozen_pyre_adapter",adapter)
                    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
                    backend=module.make_backend(frozen_module)(os.environ["MTG_PROJECT_ROOT"])
                else:
                    backend=frozen_module.Backend(os.environ["MTG_PROJECT_ROOT"])
                signal.signal(signal.SIGTERM, lambda *_: setattr(backend,"stop",True))
            result = backend.dispatch(sys.argv[2], request)
            print(json.dumps(dict(ok=True, result=result), ensure_ascii=False))
        except Exception as error:
            print(json.dumps(dict(ok=False, error=str(error)), ensure_ascii=False))
            return 1
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--service", default="http://127.0.0.1:5079", help="Loopback simulation service origin")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("inspect")
    sub.add_parser("list")
    create = sub.add_parser("create")
    create.add_argument("--profile", required=True)
    create.add_argument("--source-run", dest="sourceRunId")
    create.add_argument("--deck", dest="deckIds", action="append")
    create.add_argument("--opponent", dest="opponentIds", action="append")
    create.add_argument("--budget-seconds", dest="budgetSeconds", type=int, default=7200)
    create.add_argument("--root-seed", dest="rootSeed", type=int, default=42)
    create.add_argument("--screen-pairs", dest="screenPairs", type=int, default=10)
    create.add_argument("--holdout-pairs", dest="holdoutPairs", type=int, default=40)
    create.add_argument("--key", default=None, help="Stable idempotency key; generated and printed when omitted")
    stage = sub.add_parser("stage")
    stage.add_argument("id")
    stage.add_argument("--source", type=Path, help="Bounded ForgeBridge.java candidate; omission uses frozen template")
    stage.add_argument("--tuning", type=Path, help="JSON object of registered card|priority.path integer values")
    stage.add_argument("--fixtures", dest="fixtureSource", type=Path, help="Focused OptimizerCandidateChecks.java source; required for code revisions")
    stage.add_argument("--label", default="candidate")
    for name in ["start", "resume"]:
        p = sub.add_parser(name)
        p.add_argument("id")
        p.add_argument("--no-search", action="store_true", help="Run only staged candidates")
        p.add_argument("--screen-only", action="store_true", help="Stop for review before untouched holdout")
    for name in ["status", "pause", "report"]:
        p = sub.add_parser(name)
        p.add_argument("id")
    evidence = sub.add_parser("evidence")
    evidence.add_argument("id")
    evidence.add_argument("--limit", type=int, default=8)
    detail = sub.add_parser("detail")
    detail.add_argument("id")
    detail.add_argument("--replay", required=True)
    detail.add_argument("--sequence", type=int, required=True)
    source = sub.add_parser("source")
    source.add_argument("id")
    source.add_argument("--term", required=True)
    source.add_argument("--candidate", dest="candidateId")
    promote = sub.add_parser("promote")
    promote.add_argument("id")
    promote.add_argument("--candidate", dest="candidateId", required=True)
    profile = sub.add_parser("profile")
    profile.add_argument("id")
    profile.add_argument("--candidate", dest="candidateId", required=True)
    args = vars(parser.parse_args())
    command, base = args.pop("command"), args.pop("service")
    prefix = "/api/v1/pilot-optimizations"
    if command in ["inspect", "list"]:
        result = client(base, "GET", prefix + ("/inspect" if command == "inspect" else ""))
    elif command == "create":
        key = args.pop("key") or "optimizer-" + uuid.uuid4().hex
        print("Idempotency-Key: " + key, file=sys.stderr)
        result = client(base, "POST", prefix, {k: v for k, v in args.items() if v is not None}, key)
    else:
        eid = args.pop("id")
        if command == "stage":
            args["source"] = args["source"].read_text() if args["source"] else None
            args["tuning"] = json.loads(args["tuning"].read_text()) if args["tuning"] else {}
            args["fixtureSource"] = args["fixtureSource"].read_text() if args["fixtureSource"] else None
        if command in ["start", "resume"]:
            args = dict(search=not args["no_search"], screenOnly=args["screen_only"])
        method = "GET" if command in ["status", "report", "evidence", "detail", "source"] else "POST"
        path = prefix + "/" + urllib.parse.quote(eid, safe="")
        if command != "status":
            path += "/" + command
        if method == "GET" and args:
            path += "?" + urllib.parse.urlencode({k:v for k,v in args.items() if v is not None})
        result = client(base, method, path, args if method == "POST" else None)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(error.read().decode(), file=sys.stderr)
        sys.exit(1)
    except (ValueError, OSError, urllib.error.URLError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
