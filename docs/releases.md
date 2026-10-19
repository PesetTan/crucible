# Release pipeline

The **Release** GitHub Actions workflow builds a Windows x64 beta, validates the installed application, packages the matching Crucible source, and creates a **draft prerelease**. A failed or cancelled required job prevents packaging and release creation. Pull requests that change the workflow or release tooling run the same build and packaging path without writing a release.

This pipeline covers the Windows beta. The current macOS desktop bundle refers to a local checkout and is not a portable release package. macOS distribution, Linux installers, code signing, and automatic updates are outside this workflow.

## What runs

1. Validate a `vMAJOR.MINOR.PATCH` tag (optional SemVer prerelease suffix) against all six version declarations: npm package, npm lockfile and its root package, Tauri config, Cargo package, and Cargo lockfile. For a release, the existing tag must resolve to the workflow commit, which must be on the default branch's history. A manual dry run or pull request can build an unmerged commit.
2. Call the existing **Crucible Tests** and **Windows Desktop Beta** workflows at that same commit. Required checks include the source boundary, starter catalog, Python/.NET/frontend suites, Forge, macOS Monitor/regressions, native hosts, NSIS build, native UI gateway, and the full installed-app scenario suite.
3. Download the installer from this run only. Require a passing installed-app summary, exact installer hash, matching build/test commit and run ID, correct version/architecture, and matching build checksum.
4. Create a source snapshot from the clean tested commit, without Git history, local state, or optional private packs. Run the existing public-source boundary guard before archiving. Record hashes, commit, workflow URL, and installed-app evidence.
5. Upload the release assets as a 30-day Actions artifact. A version-tag push, or an explicit non-dry manual run on that tag, also creates a draft prerelease. Only this final job gets `contents: write`; build and test jobs have read access. Reruns can refresh the same draft. They refuse to change a published release or a draft with a different recorded commit or unexpected assets.

Reusable workflows have separate concurrency groups for their callers, so an ordinary CI run cannot cancel the release's required checks.

## Prepare a version

Commit the same version in:

- `src/Crucible.Ui/package.json`
- `src/Crucible.Ui/package-lock.json` (both the top-level and root-package version)
- `src/Crucible.Ui/src-tauri/tauri.conf.json`
- `src/Crucible.Ui/src-tauri/Cargo.toml`
- `src/Crucible.Ui/src-tauri/Cargo.lock` (the `crucible-desktop` package)

Do not reuse a version already published. Keep the Windows product name and application identifier stable for upgrades. Merge the intended application and pipeline commits into the default branch before tagging.

## Dry run

After this workflow is on the default branch, select **Actions → Release → Run workflow**, choose the intended branch, supply its matching tag version, and leave **dry_run** checked. The tag does not have to exist for a dry run. With the CLI:

```sh
gh workflow run release.yml --ref main -f tag=v0.1.0 -F dry_run=true
```

Download `crucible-release` from the successful run. A dry run uses the same tests and packaging as a tag release, but never creates or changes a GitHub release. Before merge, a pull request changing the release tooling exercises this path automatically.

## Create the release draft

From the intended clean default-branch commit, create and push an annotated version tag:

```sh
git tag -a v0.1.0 -m 'Crucible 0.1.0 Windows beta'
git push origin v0.1.0
```

The tag triggers the complete pipeline; it does not promote an installer from an earlier run. The resulting draft contains:

| Asset | Purpose |
| --- | --- |
| `Crucible Desktop_<version>_x64-setup.exe` | The exact installer exercised by the installed-app checks |
| `crucible-<version>-source.tar.gz` | Matching Crucible source, build/install scripts, dependency locks, and license documents |
| `validation.json` | Installed-app check summary and installer provenance |
| `release-manifest.json` | Version, commit, platform, workflow URL, and asset hashes |
| `RELEASE_NOTES.md` | Reviewable release description and current support/distribution limits |
| `SHA256SUMS.txt` | SHA-256 hashes of every other attached asset |

Screenshots and detailed logs remain in the Windows run's diagnostic artifact (14 days), rather than being attached to the release. Preserve any evidence needed for a release review before artifact expiry. Once published, the attached source, manifest, summary, and checksums remain with that release.

For a transient failure, rerun the failed workflow jobs. To retry draft creation explicitly, dispatch **Release** with the existing tag selected, that same tag as input, and **dry_run** unchecked. This rebuilds and retests the tag. An already published release is immutable to this tooling; use a new version for corrections.

## Review and publish

Check the draft assets and release notes, verify `SHA256SUMS.txt`, and review the linked workflow results. The automated installed-app gate covers disposable Unicode/spaced installation paths, port/data ownership, initial Forge setup, native-host credentials, duplicate launch, Workshop, two real Forge games and exact seeds, results/replays, close/reopen with explicit recovery, upgrade, and uninstall retention.

Hosted `windows-2025` validation does not establish human Windows 11 compatibility. Releases remain labeled **Windows Beta**; hands-on installation, display scaling, accessibility, and performance evidence are still required for a fully supported claim. The installer remains unsigned.

The pipeline deliberately leaves a draft for the distribution review already documented in [license review](license-review.md): complete binary notices, applicable dependency Corresponding Source, and permission for bundled card material remain unresolved there. The matching Crucible source snapshot and locked dependency retrieval instructions improve traceability; they do not establish complete compliance or grant third-party rights. Complete that review before publishing the draft through GitHub Releases.

No repository secrets are required beyond GitHub's job token. The pipeline does not create signing credentials, change repository visibility, move existing tags, publish automatically, or modify a local running Crucible installation.
