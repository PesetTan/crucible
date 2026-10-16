#!/bin/bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
dotnet_bin="${MTG_DOTNET:-$(command -v dotnet)}"
build_root="${MTG_MONITOR_BUILD_ROOT:-$project_root/macos/CrucibleMonitor/.build}"
export CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-$build_root/clang-cache}"
export SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$build_root/swift-cache}"
mkdir -p "$project_root/dist"
stage="$(mktemp -d "${TMPDIR:-/private/tmp}/crucible-monitor-package.XXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
app="$stage/Crucible Monitor.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/runtime"
swift build --package-path "$project_root/macos/CrucibleMonitor" --scratch-path "$build_root" --configuration release --product CrucibleMonitor --disable-sandbox
bin_path="$(swift build --package-path "$project_root/macos/CrucibleMonitor" --scratch-path "$build_root" --configuration release --show-bin-path --disable-sandbox)"
cp "$bin_path/CrucibleMonitor" "$app/Contents/MacOS/CrucibleMonitor"
MTG_DOTNET="$dotnet_bin" "$project_root/scripts/publish-ui.sh" "$app/Contents/Resources/runtime"
case "$(uname -m)" in
    arm64) service_rid=osx-arm64 ;;
    x86_64) service_rid=osx-x64 ;;
    *) printf 'Unsupported Mac architecture\n' >&2; exit 1 ;;
esac
"$dotnet_bin" publish "$project_root/src/Crucible.Service/Crucible.Service.csproj" --configuration Release --runtime "$service_rid" --output "$app/Contents/Resources/runtime/service" --nologo --disable-build-servers
printf '%s\n' "$project_root" > "$app/Contents/Resources/project-root.txt"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>CrucibleMonitor</string>
<key>CFBundleIdentifier</key><string>local.crucible.monitor</string>
<key>CFBundleName</key><string>Crucible Monitor</string>
<key>CFBundleDisplayName</key><string>Crucible Monitor</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.0.3</string>
<key>CFBundleVersion</key><string>4</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
xattr -cr "$app"
codesign --force --sign - "$app"
codesign --verify --strict "$app"
ditto -c -k --keepParent --norsrc --noextattr "$app" "$project_root/dist/Crucible Monitor.zip"
if [ -e "$project_root/dist/Crucible Monitor.app" ]; then
  # This is generated output; per-component rollback snapshots live in data/monitor/builds.
  rm -rf -- "$project_root/dist/Crucible Monitor.app"
fi
mv "$app" "$project_root/dist/Crucible Monitor.app"
xattr -cr "$project_root/dist/Crucible Monitor.app"
codesign --verify --strict "$project_root/dist/Crucible Monitor.app"
printf 'Created %s\n' "$project_root/dist/Crucible Monitor.app"
