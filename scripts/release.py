#!/usr/bin/env python3
"""Validate, assemble, and draft a Windows beta release from the same tested commit."""
import argparse
import gzip
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import tomllib

ROOT = Path(__file__).resolve().parents[1]
TAG = re.compile(r'v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?')


def run(*args, cwd=ROOT):
    return subprocess.check_output(args, cwd=cwd, text=True).strip()


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n', encoding='utf-8')


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def version(tag, root=ROOT):
    if not TAG.fullmatch(tag):
        raise ValueError('Use a vMAJOR.MINOR.PATCH tag, optionally with a SemVer prerelease suffix.')
    expected = tag[1:]
    ui = root / 'src/Crucible.Ui'
    package = read_json(ui / 'package.json')
    lock = read_json(ui / 'package-lock.json')
    cargo = tomllib.loads((ui / 'src-tauri/Cargo.toml').read_text())
    cargo_lock = tomllib.loads((ui / 'src-tauri/Cargo.lock').read_text())
    versions = [package['version'], lock['version'], lock['packages']['']['version'],
                read_json(ui / 'src-tauri/tauri.conf.json')['version'], cargo['package']['version']]
    versions += [p['version'] for p in cargo_lock['package'] if p['name'] == 'crucible-desktop']
    if len(versions) != 6 or any(v != expected for v in versions):
        raise ValueError(f'Tag {tag} does not match all six committed package/lockfile versions: {versions}')
    return expected


def identity():
    commit = run('git', 'rev-parse', 'HEAD')
    if os.environ.get('GITHUB_SHA', commit) != commit:
        raise ValueError('Checkout differs from the workflow commit.')
    return commit


def check(tag):
    if not tag and os.environ.get('RELEASE_DRY_RUN') == 'true':
        tag = 'v' + read_json(ROOT / 'src/Crucible.Ui/package.json')['version']
    version(tag)
    commit = identity()
    if os.environ.get('RELEASE_DRY_RUN') != 'true':
        tagged = run('git', 'rev-parse', '--verify', f'refs/tags/{tag}^{{commit}}')
        if tagged != commit:
            raise ValueError('Release tag must point to the exact workflow commit. Select the tag when dispatching.')
        branch = os.environ['RELEASE_DEFAULT_BRANCH']
        run('git', 'merge-base', '--is-ancestor', commit, f'refs/remotes/origin/{branch}')
    if output := os.environ.get('GITHUB_OUTPUT'):
        with open(output, 'a', encoding='utf-8') as stream:
            stream.write(f'tag={tag}\n')
    print(f'Validated {tag} at {commit}')


def source_archive(output, tag, commit):
    spec = importlib.util.spec_from_file_location('source_export', ROOT / 'scripts/export-public-source.py')
    export = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(export)
    paths = export.source_files(ROOT)
    export.verify(ROOT, paths)
    # A source archive must represent exactly the tested commit, never a dirty workspace.
    if run('git', 'status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Refusing to archive a dirty checkout.')
    epoch = int(run('git', 'show', '-s', '--format=%ct', commit))
    with output.open('wb') as raw, gzip.GzipFile(fileobj=raw, filename='', mode='wb', mtime=epoch) as compressed:
        with tarfile.open(fileobj=compressed, mode='w') as archive:
            for rel in paths:
                path = ROOT / rel
                info = tarfile.TarInfo(f'crucible-{tag[1:]}/{rel.as_posix()}')
                info.size = path.stat().st_size
                info.mode = 0o755 if os.access(path, os.X_OK) else 0o644
                info.mtime = epoch
                archive.addfile(info, io.BytesIO(path.read_bytes()))


def bundle(tag, windows, destination):
    release_version = version(tag)
    commit = identity()
    if destination.exists():
        raise ValueError('Release output already exists; choose an empty path.')
    installers = list(windows.glob('*-setup.exe'))
    if len(installers) != 1:
        raise ValueError('Expected exactly one Windows installer.')
    installer = installers[0]
    if f'_{release_version}_x64-setup.exe' not in installer.name:
        raise ValueError('Installer filename does not match the release version and architecture.')
    summary = read_json(windows / 'evidence/summary.json')
    provenance = read_json(windows / 'evidence/installer-provenance.json')
    digest = sha(installer)
    if summary.get('passed') is not True or not summary.get('checks'):
        raise ValueError('Installed-app validation did not pass.')
    if (provenance.get('sha256') != digest or provenance.get('installer') != installer.name
            or provenance.get('testCommit') != commit
            or provenance.get('installerRunId') != os.environ['GITHUB_RUN_ID']):
        raise ValueError('Installer bytes, tested commit, or build run do not match.')
    sums = (windows / 'SHA256SUMS.txt').read_text(encoding='utf-8-sig').strip()
    if sums != f'{digest}  {installer.name}':
        raise ValueError('Build checksum does not match the tested installer.')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination.parent) as temporary:
        staged = Path(temporary)
        shutil.copy2(installer, staged / installer.name)
        source = staged / f'crucible-{release_version}-source.tar.gz'
        source_archive(source, tag, commit)
        write_json(staged / 'validation.json', {'summary': summary, 'installer': provenance})
        run_url = f"{os.environ.get('GITHUB_SERVER_URL', 'https://github.com')}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
        write_json(staged / 'release-manifest.json', {
            'schemaVersion': 1, 'tag': tag, 'version': release_version, 'commit': commit,
            'runUrl': run_url, 'platform': 'windows-x64', 'channel': 'beta',
            'assets': {p.name: sha(p) for p in sorted(staged.iterdir())},
        })
        (staged / 'RELEASE_NOTES.md').write_text(
            f'# Crucible {tag} — Windows Beta\n\n'
            f'Commit: `{commit}`. [Build and validation]({run_url}).\n\n'
            'Windows x64 installer with automated hosted Windows validation. '
            'Human Windows 11 installation, display scaling, accessibility, and performance validation remain pending. '
            'The installer is unsigned. Initial engine setup requires internet access.\n\n'
            f'Matching Crucible source and build/install scripts: `{source.name}` beside the installer. '
            'Dependency versions and retrieval instructions are in its lockfiles and build scripts. '
            'See `LICENSE`, `THIRD_PARTY.md`, and `docs/license-review.md` in that source for licensing '
            'and the remaining dependency-notice/source and card-content distribution review.\n\n'
            '`SHA256SUMS.txt` covers every other attached asset. '
            '`release-manifest.json` identifies the tested commit and asset hashes; '
            '`validation.json` records the installed-app checks.\n\n'
            'This draft is for release review. Resolve the distribution review in '
            '`docs/license-review.md` before publishing it.\n', encoding='utf-8')
        files = sorted(staged.iterdir())
        (staged / 'SHA256SUMS.txt').write_text(''.join(f'{sha(p)}  {p.name}\n' for p in files), encoding='utf-8')
        shutil.copytree(staged, destination)
    print(f'Assembled {tag} assets in {destination}')


def verify_bundle(directory, tag, commit):
    manifest = read_json(directory / 'release-manifest.json')
    if manifest['tag'] != tag or manifest['commit'] != commit:
        raise ValueError('Release bundle belongs to a different tag or commit.')
    expected = set(manifest['assets']) | {'release-manifest.json', 'RELEASE_NOTES.md'}
    actual = {p.name for p in directory.iterdir()}
    if actual != expected | {'SHA256SUMS.txt'}:
        raise ValueError('Unexpected or missing release assets.')
    lines = (directory / 'SHA256SUMS.txt').read_text().splitlines()
    if lines != [f'{sha(directory / name)}  {name}' for name in sorted(expected)]:
        raise ValueError('Release checksums do not match.')
    for name, digest in manifest['assets'].items():
        if sha(directory / name) != digest:
            raise ValueError(f'Manifest checksum mismatch: {name}')
    return manifest


def publish(tag, directory):
    commit = identity()
    verify_bundle(directory, tag, commit)
    repository = os.environ['GITHUB_REPOSITORY']
    # Listing errors fail closed; an API outage is never mistaken for an absent release.
    pages = json.loads(run('gh', 'api', '--paginate', '--slurp', f'repos/{repository}/releases'))
    existing = next((r for page in pages for r in page if r['tag_name'] == tag), None)
    if existing:
        if not existing['draft']:
            raise ValueError('Refusing to modify an already published release. Create a new version.')
        assets = {a['name'] for a in existing['assets']}
        if not assets <= {p.name for p in directory.iterdir()}:
            raise ValueError('Existing draft has unexpected assets; review them before retrying.')
        if 'release-manifest.json' in assets:
            with tempfile.TemporaryDirectory() as temporary:
                run('gh', 'release', 'download', tag, '--repo', repository, '--pattern', 'release-manifest.json', '--dir', temporary)
                prior = read_json(Path(temporary) / 'release-manifest.json')
                if prior['commit'] != commit:
                    raise ValueError('Existing draft belongs to a different commit.')
    else:
        run('gh', 'release', 'create', tag, '--repo', repository, '--verify-tag', '--draft', '--prerelease',
            '--title', f'Crucible {tag} — Windows Beta', '--notes-file', str(directory / 'RELEASE_NOTES.md'))
    run('gh', 'release', 'upload', tag, '--repo', repository, '--clobber', *map(str, sorted(directory.iterdir())))
    run('gh', 'release', 'edit', tag, '--repo', repository, '--draft', '--prerelease',
        '--title', f'Crucible {tag} — Windows Beta', '--notes-file', str(directory / 'RELEASE_NOTES.md'))
    print(run('gh', 'release', 'view', tag, '--repo', repository, '--json', 'url', '--jq', '.url'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['check', 'bundle', 'publish'])
    parser.add_argument('--tag', default=os.environ.get('RELEASE_TAG', ''))
    parser.add_argument('--windows', type=Path, default=ROOT / 'dist/windows')
    parser.add_argument('--output', type=Path, default=ROOT / 'dist/release')
    args = parser.parse_args()
    if args.action == 'check':
        check(args.tag)
    elif args.action == 'bundle':
        bundle(args.tag, args.windows, args.output)
    else:
        publish(args.tag, args.output)


if __name__ == '__main__':
    main()
