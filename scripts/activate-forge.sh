#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
exec env MTG_PROJECT_ROOT="$project_root" dotnet run --project "$project_root/src/DeckTester/DeckTester.csproj" --no-restore -- --activate-engine
