#!/usr/bin/env python3
"""Run all self-contained .NET assertion suites serially, without a live service."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    projects = sorted((ROOT / 'tests').glob('*/*.csproj'))
    projects = [p for p in projects if p.stem not in {'FakeForge', 'PyreImportChecks'}]
    if not projects:
        raise RuntimeError('No .NET check projects found in this checkout.')
    for project in projects:
        args = ['resources/starter'] if project.stem == 'WindowsBetaChecks' else []
        print(f'Checking {project.stem}', flush=True)
        subprocess.run(['dotnet', 'run', '--project', str(project), '--', *args], cwd=ROOT, check=True)
    print(f'{len(projects)} self-contained .NET suites passed. PyreImportChecks requires external native records; the private Pyre workflow runs it.')

if __name__ == '__main__':
    main()
