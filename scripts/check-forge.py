#!/usr/bin/env python3
"""Fresh bridge/fixture checks using public policy annotations and synthetic decks."""
from pathlib import Path
import json
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    active = ROOT / 'data/engine-bundles/active.json'
    pointer = active.read_bytes()
    bundle = ROOT / 'data/engine-bundles' / json.loads(pointer)['id']
    jar = bundle / json.loads((bundle / 'bundle.json').read_text())['jarName']
    java_home = ROOT / 'vendor/jdk'
    if (java_home / 'Contents/Home').is_dir():
        java_home /= 'Contents/Home'
    suffix = '.exe' if os.name == 'nt' else ''
    java = java_home / 'bin' / ('java' + suffix)
    javac = java.with_name('javac' + suffix)
    with tempfile.TemporaryDirectory(prefix='crucible-forge-checks-') as temporary:
        folder = Path(temporary)
        classes = folder / 'classes'; classes.mkdir()
        subprocess.run([str(javac), '-cp', str(jar), '-d', str(classes), str(ROOT / 'engine/ForgeBridge.java'),
                        *map(str, sorted((ROOT / 'tests').glob('*.java')))], check=True)
        # Select the highest-priority reviewed public annotation for each profile.
        profiles = {}
        for card, attributes in json.loads((ROOT / 'docs/subtype-pilots.seed.json').read_text()).items():
            for profile, priority in attributes.get('subtypePilot', {}).get('developOnPass', {}).items():
                profiles.setdefault(profile, []).append((priority, card))
        registry = folder / 'registry.json'
        registry.write_text(json.dumps([{'profile': profile, 'cards': [max(cards)[1]]}
                                        for profile, cards in sorted(profiles.items())]))
        inventory = folder / 'inventory.json'
        inventory.write_text(json.dumps([{'id': 'synthetic-basic-lands', 'manifest': {'cards': [
            {'name': 'Plains', 'zone': 'MainDeck', 'quantity': 60}]}}]))
        def run(name, *args):
            home = folder / ('home-' + name); home.mkdir()
            subprocess.run([str(java), '-Xmx1400m', '-Djava.awt.headless=true', '-Duser.home=' + str(home),
                            '-cp', os.pathsep.join([str(classes), str(jar)]), *args], cwd=bundle, check=True, timeout=600)
        for name in ['ReanimatorPilotChecks', 'ControlPilotChecks', 'OptimizerTuningChecks', 'AggroIntegrationChecks',
                     'ControlIntegrationChecks', 'ReanimatorCopyManaChecks', 'EngagementProtocolChecks', 'EngagementProtocolsAllPilotsChecks']:
            run(name, name)
        run('subtypes', 'SubtypePilotChecks', str(registry), str(inventory))
        run('lute', 'SubtypePilotChecks', str(registry), str(inventory), '--lute')
        run('classification', 'PilotAttributeChecks', '--classification-checks')
        positions = folder / 'positions.json'
        run('current-diagnostics', 'PilotAttributeChecks', str(registry), str(positions), '10,11')
        cases = json.loads(positions.read_text())['cases']
        counts = {status: sum(case['status'] == status for case in cases) for status in ['passed', 'failed', 'error', 'observed']}
        print('Current pilot diagnostics: ' + json.dumps(counts), flush=True)
        if counts['failed'] or counts['error']:
            raise SystemExit('Current pilot diagnostics failed.')
    if active.read_bytes() != pointer:
        raise SystemExit('Checks changed the active engine pointer.')

if __name__ == '__main__':
    main()
