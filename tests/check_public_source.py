#!/usr/bin/env python3
"""Check release boundary failures with synthetic leaked data and unsafe paths."""
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('public_export', ROOT / 'scripts/export-public-source.py')
export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(export)

class BoundaryChecks(unittest.TestCase):
    def test_current_tree(self):
        export.verify(ROOT, export.source_files(ROOT))

    def test_source_reference(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'leak.md').write_bytes(b'https://mtga.' + b'untapped' + b'.gg/private-fixture')
            with self.assertRaisesRegex(ValueError, 'Private source reference'):
                export.verify(root, [Path('leak.md')])

    def test_symlink(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'private-input').symlink_to(ROOT / 'README.md')
            with self.assertRaisesRegex(ValueError, 'linked path'):
                export.verify(root, [Path('private-input')])

    def test_private_paths_and_screenshots(self):
        for rel in ['private/decks.json', 'docs/source.png', 'data/simulator.sqlite', '.git/HEAD']:
            with self.subTest(path=rel), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                (root / rel).parent.mkdir(parents=True)
                (root / rel).write_bytes(b'fixture')
                with self.assertRaises(ValueError):
                    export.verify(root, [Path(rel)])

    def test_compressed_data_even_with_updated_checksum(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            shutil.copytree(ROOT / 'resources', root / 'resources')
            (root / 'scripts').mkdir()
            shutil.copy2(ROOT / 'scripts/export-starter-catalog.py', root / 'scripts')
            (root / 'src/DeckTester/notes').mkdir(parents=True)
            (root / 'src/DeckTester/notes/editorial.json').write_text('[]')
            database = root / 'fixture.sqlite'
            starter = root / 'resources/starter'
            database.write_bytes(gzip.decompress((starter / 'catalog.sqlite.gz').read_bytes()))
            with sqlite3.connect(database) as db:
                db.execute('CREATE TABLE source_decks(name TEXT)')
                db.execute("INSERT INTO source_decks VALUES('private fixture')")
            raw = database.read_bytes()
            database.unlink()
            (starter / 'catalog.sqlite.gz').write_bytes(gzip.compress(raw))
            manifest = json.loads((starter / 'manifest.json').read_text())
            manifest['sha256'] = hashlib.sha256(raw).hexdigest()
            (starter / 'manifest.json').write_text(json.dumps(manifest))
            with self.assertRaises(AssertionError):
                export.verify(root, export.source_files(root))

if __name__ == '__main__':
    unittest.main()
