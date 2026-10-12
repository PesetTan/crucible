#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
exec env MTG_HOST_MODE=service MTG_PORT="${MTG_SERVICE_PORT:-5079}" MTG_PROJECT_ROOT="$project_root" dotnet run --project "$project_root/src/DeckTester/DeckTester.csproj" "$@"
