#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
output="${1:?Usage: publish-ui.sh OUTPUT [RID]}"
case "$(uname -s):$(uname -m)" in
  Darwin:arm64) default_rid=osx-arm64;;
  Darwin:x86_64) default_rid=osx-x64;;
  Linux:aarch64|Linux:arm64) default_rid=linux-arm64;;
  Linux:x86_64) default_rid=linux-x64;;
  *) default_rid=win-x64;;
esac
rid="${2:-$default_rid}"
# Build tooling is unnecessary when running the published native UI.
if [ -z "${MTG_NODE:-}" ] && [ -x "$project_root/data/toolchains/node/bin/node" ]; then MTG_NODE="$project_root/data/toolchains/node/bin/node"; fi
if [ -n "${MTG_NODE:-}" ]; then export PATH="$(dirname -- "$MTG_NODE"):$PATH"; fi
if [ ! -d "$project_root/src/Crucible.Ui/node_modules" ]; then npm ci --prefix "$project_root/src/Crucible.Ui" --cache "$project_root/data/toolchains/npm-cache" --no-audit --no-fund; fi
node "$project_root/scripts/prepare-ui.mjs"
npm --prefix "$project_root/src/Crucible.Ui" run check
npm --prefix "$project_root/src/Crucible.Ui" run build
"${MTG_DOTNET:-dotnet}" publish "$project_root/src/Crucible.UiHost/Crucible.UiHost.csproj" -c Release -r "$rid" -o "$output" --disable-build-servers -p:UseSharedCompilation=false -m:1 --nologo
case "$rid" in win-*) "$output/CrucibleUi.exe" --check-runtime;; *) "$output/CrucibleUi" --check-runtime;; esac
