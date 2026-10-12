#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
exec env MTG_HOST_MODE=ui MTG_PORT="${MTG_UI_PORT:-5077}" MTG_BACKEND_URL="${MTG_BACKEND_URL:-http://127.0.0.1:${MTG_SERVICE_PORT:-5079}/}" dotnet run --project "$project_root/src/MtgSimulator/MtgSimulator.csproj" "$@"
