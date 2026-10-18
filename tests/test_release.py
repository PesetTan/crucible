"""Check release provenance, version consistency, corruption, and draft-only publication."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('release', ROOT / 'scripts/release.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.windows = self.root / 'windows'
        (self.windows / 'evidence').mkdir(parents=True)
        self.output = self.root / 'release'
        self.installer = self.windows / 'Crucible Desktop_0.1.0_x64-setup.exe'
        self.installer.write_bytes(b'fixture executable')
        self.digest = release.sha(self.installer)
        self.summary = {'passed': True, 'checks': ['complete installed-app fixture'], 'humanWindows11Validation': False}
        self.provenance = {'installer': self.installer.name, 'sha256': self.digest,
                           'installerRunId': '123', 'testCommit': 'a' * 40}
        self.save_evidence()
        self.addCleanup(patch.stopall)
        patch.dict(os.environ, {'GITHUB_RUN_ID': '123', 'GITHUB_REPOSITORY': 'owner/repo'}).start()
        patch.object(release, 'identity', return_value='a' * 40).start()
        patch.object(release, 'version', return_value='0.1.0').start()
        patch.object(release, 'source_archive', side_effect=lambda path, *_: path.write_bytes(b'fixture source')).start()

    def save_evidence(self):
        release.write_json(self.windows / 'evidence/summary.json', self.summary)
        release.write_json(self.windows / 'evidence/installer-provenance.json', self.provenance)
        (self.windows / 'SHA256SUMS.txt').write_text(f'{self.digest}  {self.installer.name}\n')

    def assemble(self):
        release.bundle('v0.1.0', self.windows, self.output)

    def test_bundle_has_exact_verified_assets(self):
        self.assemble()
        manifest = release.verify_bundle(self.output, 'v0.1.0', 'a' * 40)
        self.assertEqual(manifest['channel'], 'beta')
        self.assertEqual(len(list(self.output.iterdir())), 6)
        self.assertFalse(release.read_json(self.output / 'validation.json')['summary']['humanWindows11Validation'])

    def test_wrong_installer_commit_run_or_hash_fails(self):
        for key, value in [('sha256', 'bad'), ('testCommit', 'b' * 40), ('installerRunId', '456'), ('installer', 'another.exe')]:
            with self.subTest(key=key):
                original = self.provenance[key]
                self.provenance[key] = value
                self.save_evidence()
                with self.assertRaisesRegex(ValueError, 'do not match'):
                    self.assemble()
                self.assertFalse(self.output.exists())
                self.provenance[key] = original

    def test_failure_or_missing_validation_never_packages(self):
        self.summary['passed'] = False
        self.save_evidence()
        with self.assertRaisesRegex(ValueError, 'did not pass'):
            self.assemble()
        (self.windows / 'evidence/summary.json').unlink()
        with self.assertRaises(FileNotFoundError):
            self.assemble()

    def test_checksum_or_installer_version_mismatch_fails(self):
        (self.windows / 'SHA256SUMS.txt').write_text('corrupt')
        with self.assertRaisesRegex(ValueError, 'Build checksum'):
            self.assemble()
        self.installer.rename(self.windows / 'Crucible Desktop_0.2.0_x64-setup.exe')
        with self.assertRaisesRegex(ValueError, 'filename'):
            self.assemble()

    def test_tampered_missing_or_extra_release_assets_fail(self):
        self.assemble()
        source = self.output / 'crucible-0.1.0-source.tar.gz'
        source.write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'checksums'):
            release.verify_bundle(self.output, 'v0.1.0', 'a' * 40)
        source.unlink()
        with self.assertRaisesRegex(ValueError, 'missing'):
            release.verify_bundle(self.output, 'v0.1.0', 'a' * 40)

    def test_published_release_is_never_modified(self):
        self.assemble()
        with patch.object(release, 'run', return_value=json.dumps([[{'tag_name': 'v0.1.0', 'draft': False}]])) as gh:
            with self.assertRaisesRegex(ValueError, 'already published'):
                release.publish('v0.1.0', self.output)
            self.assertEqual(gh.call_count, 1)

    def test_new_release_is_drafted_as_prerelease(self):
        self.assemble()
        with patch.object(release, 'run', side_effect=['[[]]', '', '', '', 'https://example.test/release']) as gh:
            release.publish('v0.1.0', self.output)
            create = gh.call_args_list[1].args
            self.assertIn('--draft', create)
            self.assertIn('--prerelease', create)
            self.assertIn('--verify-tag', create)


class VersionChecks(unittest.TestCase):
    def test_current_versions_agree(self):
        version = release.read_json(ROOT / 'src/Crucible.Ui/package.json')['version']
        self.assertEqual(release.version('v' + version), version)

    def test_invalid_or_mismatching_tags_fail(self):
        for tag in ['v1.2.3/other', 'v01.2.3', 'v1.2.3-01', '1.2.3', 'v999.0.0', 'v1.2.3\n']:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.version(tag)

    def test_stale_lockfile_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ui = root / 'src/Crucible.Ui'
            (ui / 'src-tauri').mkdir(parents=True)
            for file in ['package.json', 'package-lock.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'src-tauri/Cargo.lock']:
                shutil.copy2(ROOT / 'src/Crucible.Ui' / file, ui / file)
            lock = release.read_json(ui / 'package-lock.json')
            current = lock['version']
            lock['packages']['']['version'] = '999.0.0'
            release.write_json(ui / 'package-lock.json', lock)
            with self.assertRaisesRegex(ValueError, 'six committed'):
                release.version('v' + current, root)


if __name__ == '__main__':
    unittest.main()
