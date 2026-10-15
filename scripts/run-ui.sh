#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
if [ "${MTG_LEGACY_UI:-0}" = 1 ]; then
  ui_project="$project_root/src/DeckTester/DeckTester.csproj"
else
  if [ -z "${MTG_NODE:-}" ] && [ -x "$project_root/data/toolchains/node/bin/node" ]; then MTG_NODE="$project_root/data/toolchains/node/bin/node"; fi
  if [ -n "${MTG_NODE:-}" ]; then export PATH="$(dirname -- "$MTG_NODE"):$PATH"; fi
  if [ ! -d "$project_root/src/Crucible.Ui/node_modules" ]; then npm ci --prefix "$project_root/src/Crucible.Ui" --cache "$project_root/data/toolchains/npm-cache" --no-audit --no-fund; fi
  npm --prefix "$project_root/src/Crucible.Ui" run build
  ui_project="$project_root/src/Crucible.UiHost/Crucible.UiHost.csproj"
fi
exec env MTG_HOST_MODE=ui MTG_PORT="${MTG_UI_PORT:-5077}" MTG_BACKEND_URL="${MTG_BACKEND_URL:-http://127.0.0.1:${MTG_SERVICE_PORT:-5079}/}" dotnet run --project "$ui_project" "$@"
