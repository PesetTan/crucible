#!/usr/bin/env python3
"""Export the current source files without Git history, local state, or private inputs."""
import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
sys.dont_write_bytecode = True
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
# Split patterns so the guard's own source does not contain a forbidden reference.
FORBIDDEN = [b'untapped' + b'.gg', b'untapped' + b'_', b'standard-bo1-' + b'bronze-platinum-2026',
             b'standard-bo1-' + b'tier-representatives-2026']

def source_files(root):
    if (root / '.git').exists():
        raw = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z', '--cached', '--others', '--exclude-standard'])
        return sorted({Path(p) for p in raw.decode().split('\0') if p and (root / p).exists()})
    return sorted(p.relative_to(root) for p in root.rglob('*') if p.is_file() or p.is_symlink())

def verify(root, paths):
    for rel in paths:
        path = root / rel
        if path.is_symlink() or any(p in {'.git', 'private', 'data', 'vendor', 'node_modules', 'bin', 'obj', 'dist', 'target', '__pycache__'} for p in rel.parts):
            raise ValueError(f'Non-source or linked path: {rel}')
        if 'docs' in rel.parts and path.suffix.lower() in {'.png', '.jpg', '.jpeg', '.webp', '.gif'}:
            raise ValueError(f'Unreviewed documentation screenshot: {rel}')
        if '.sqlite' in path.name or path.suffix == '.db':
            if rel.as_posix() != 'resources/starter/catalog.sqlite.gz':
                raise ValueError(f'Unexpected database: {rel}')
        if rel.as_posix() == 'resources/starter/catalog.sqlite.gz':
            continue  # Checked by schema and row allowlists below, after decompression.
        raw = path.read_bytes().lower()
        if any(token in raw for token in FORBIDDEN):
            raise ValueError(f'Private source reference: {rel}')
    spec = importlib.util.spec_from_file_location('starter', root / 'scripts/export-starter-catalog.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    import gzip, hashlib, tempfile
    starter = root / 'resources/starter'
    manifest = json.loads((starter / 'manifest.json').read_text())
    raw = gzip.decompress((starter / 'catalog.sqlite.gz').read_bytes())
    if hashlib.sha256(raw).hexdigest() != manifest['sha256']:
        raise ValueError('Starter checksum mismatch')
    with tempfile.TemporaryDirectory() as tmp:
        database = Path(tmp) / 'catalog.sqlite'
        database.write_bytes(raw)
        if module.verify(database) != manifest['rows']:
            raise ValueError('Starter row counts differ')
    if json.loads((root / 'src/DeckTester/notes/editorial.json').read_text()) != []:
        raise ValueError('Source-specific editorial seeds must remain private')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    paths = source_files(ROOT)
    verify(ROOT, paths)
    if args.output is None:
        print(f'Public source boundary passed for {len(paths)} files; card-only starter verified. Git history was not scanned or sanitized.')
        return
    output = args.output.resolve()
    if output.exists():
        parser.error('Output already exists; choose a fresh directory.')
    if output == ROOT or ROOT.is_relative_to(output):
        parser.error('Output cannot be the source tree or its parent.')
    output.mkdir(parents=True)
    try:
        for rel in paths:
            destination = output / rel
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / rel, destination)
        verify(output, source_files(output))
    except Exception:
        shutil.rmtree(output)
        raise
    print(f'Exported and verified {len(paths)} files without Git history: {output}')

if __name__ == '__main__':
    main()
