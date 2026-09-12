#!/bin/bash
set -euo pipefail
if [[ "$(uname -s)" != Darwin ]]; then echo 'Run this packaging script on macOS.' >&2; exit 1; fi
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
rid="${1:-osx-$(uname -m)}"
[[ "$rid" == osx-x86_64 ]] && rid=osx-x64
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'Choose osx-arm64 or osx-x64.' >&2; exit 1 ;; esac
version="$(dotnet msbuild "$repo_dir/source/UsageWidget.csproj" -getProperty:Version -nologo)"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid project version.' >&2; exit 1; }
mkdir -p "$repo_dir/work" "$repo_dir/dist"
stage_root="$(mktemp -d "$repo_dir/work/macos-package.XXXXXX")"
stage="$stage_root/AIUsageWidget-$version-$rid"
app="$stage/AI Usage Widget.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$repo_dir/source/UsageWidget.csproj" -c Release -r "$rid" --self-contained true -o "$app/Contents/MacOS"
chmod +x "$app/Contents/MacOS/AIUsageWidget"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.wernerong.ai-usage-widget</string>
<key>CFBundleName</key><string>AI Usage Widget</string>
<key>CFBundleDisplayName</key><string>AI Usage Widget</string>
<key>CFBundleExecutable</key><string>AIUsageWidget</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>$version</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$app/Contents/Info.plist"
# Ad-hoc signing is for local builds, not Developer ID/notarized distribution.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
cp "$repo_dir/scripts/install-macos.sh" "$stage/Install.command"
cp "$repo_dir/scripts/uninstall-macos.sh" "$stage/Uninstall.command"
cp "$repo_dir/README.md" "$repo_dir/CHANGELOG.md" "$stage/"
chmod +x "$stage/Install.command" "$stage/Uninstall.command"
archive="$repo_dir/dist/AIUsageWidget-$version-$rid.zip"
ditto -c -k --sequesterRsrc --keepParent "$stage" "$archive"
echo "Built: $archive"
echo "Application: $app"
