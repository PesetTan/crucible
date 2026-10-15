#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
if [ "$(uname -s)" != Darwin ]; then printf 'This packaging script currently verifies macOS builds. See docs/svelte-desktop.md for other targets.\n' >&2; exit 1; fi
if [ -d "$project_root/data/toolchains/cargo/bin" ]; then
 export CARGO_HOME="$project_root/data/toolchains/cargo" RUSTUP_HOME="$project_root/data/toolchains/rustup"
 export PATH="$CARGO_HOME/bin:$PATH"
fi
case "$(uname -m)" in arm64) rid=osx-arm64;; x86_64) rid=osx-x64;; *) exit 1;; esac
snapshot="$project_root/data/desktop/builds/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$snapshot"
"$project_root/scripts/publish-ui.sh" "$snapshot/ui" "$rid"
if [ -n "${1:-}" ]; then
 backend="$(cd -- "$1" && pwd)"
else
 backend="$snapshot/service"
 "${MTG_DOTNET:-dotnet}" publish "$project_root/src/Crucible.Service/Crucible.Service.csproj" -c Release -r "$rid" -o "$backend" --disable-build-servers -p:UseSharedCompilation=false -m:1 --nologo
fi
python3 - "$project_root" "$snapshot/ui" "$backend" <<'PY'
import pathlib,shutil,sys
root,ui,backend=map(pathlib.Path,sys.argv[1:]);resources=root/'src/Crucible.Ui/src-tauri/resources';resources.mkdir(exist_ok=True)
for source,name in [(ui,'ui'),(backend,'service')]:
 target=resources/name
 if target.exists():shutil.rmtree(target)
 shutil.copytree(source,target,ignore=shutil.ignore_patterns('*.dSYM','*.pdb','*.dbg'))
(resources/'project-root.txt').write_text(str(root)+'\n')
PY
cargo build --release --locked --manifest-path "$project_root/src/Crucible.Ui/src-tauri/Cargo.toml" -j "${CARGO_BUILD_JOBS:-2}"
output="$project_root/dist/desktop-$(date -u +%Y%m%dT%H%M%SZ)/Crucible.app"
python3 "$project_root/scripts/package-desktop.py" --ui-runtime "$snapshot/ui" --backend-runtime "$backend" --output "$output"
