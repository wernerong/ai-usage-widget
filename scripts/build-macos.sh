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
iconset="$stage_root/widget.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$repo_dir/source/Assets/widget.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double_size=$((size * 2))
    sips -z "$double_size" "$double_size" "$repo_dir/source/Assets/widget.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil --convert icns "$iconset" --output "$app/Contents/Resources/widget.icns"
test -s "$app/Contents/Resources/widget.icns"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.wernerong.ai-usage-widget</string>
<key>CFBundleName</key><string>AI Usage Widget</string>
<key>CFBundleDisplayName</key><string>AI Usage Widget</string>
<key>CFBundleExecutable</key><string>AIUsageWidget</string>
<key>CFBundleIconFile</key><string>widget.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>$version</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$app/Contents/Info.plist"
# Ad-hoc signing is for local builds, not Developer ID/notarized distribution.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

# Use the same update-ready bundle for the native installer and update feed.
# Ad-hoc signing remains the default until Developer ID credentials are supplied.
tools="$repo_dir/work/vpk"
if [[ ! -x "$tools/vpk" ]]; then dotnet tool install vpk --tool-path "$tools" --version 1.2.158; fi
updates="$stage_root/updates"
sign_args=(--signAppIdentity "${MACOS_SIGN_IDENTITY:--}")
if [[ -n "${MACOS_NOTARY_PROFILE:-}" ]]; then sign_args+=(--notaryProfile "$MACOS_NOTARY_PROFILE"); fi
"$tools/vpk" pack --packId AIUsageWidget.Desktop --packVersion "$version" --packDir "$app" \
    --mainExe AIUsageWidget --packTitle 'AI Usage Widget' --channel "$rid" --runtime "$rid" \
    --outputDir "$updates" --noInst "${sign_args[@]}"
ditto -x -k "$updates/AIUsageWidget.Desktop-$rid-Portable.zip" "$stage_root/update-ready"
app="$stage_root/update-ready/AI Usage Widget.app"
test -x "$app/Contents/MacOS/UpdateMac"
codesign --verify --deep --strict "$app"
cp "$updates/"*.nupkg "$updates/"releases.*.json "$repo_dir/dist/"
cp "$repo_dir/scripts/install-macos.sh" "$stage/Install.command"
cp "$repo_dir/scripts/uninstall-macos.sh" "$stage/Uninstall.command"
cp "$repo_dir/README.md" "$repo_dir/CHANGELOG.md" "$stage/"
chmod +x "$stage/Install.command" "$stage/Uninstall.command"
archive="$repo_dir/dist/AIUsageWidget-$version-$rid.zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
echo "Built: $archive"
echo "Application: $app"

# A native per-user Installer package. Preserve the path used by startup entries.
component="$stage_root/widget-component.pkg"
package_root="$stage_root/payload"
mkdir -p "$package_root"
ditto "$app" "$package_root/AI Usage Widget.app"
pkgbuild --analyze --root "$package_root" "$stage_root/components.plist"
/usr/libexec/PlistBuddy -c 'Set :0:BundleIsRelocatable false' "$stage_root/components.plist"
/usr/libexec/PlistBuddy -c 'Set :0:BundleHasStrictIdentifier true' "$stage_root/components.plist"
pkgbuild --root "$package_root" --component-plist "$stage_root/components.plist" \
    --identifier com.wernerong.ai-usage-widget.pkg --version "$version" \
    --install-location /Applications "$component"
architecture=arm64
[[ "$rid" == osx-x64 ]] && architecture=x86_64
cat > "$stage_root/Distribution.xml" <<DIST
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="2">
  <title>AI Usage Widget</title>
  <welcome file="welcome.html" mime-type="text/html"/>
  <conclusion file="conclusion.html" mime-type="text/html"/>
  <options customize="never" require-scripts="false" hostArchitectures="$architecture"/>
  <domains enable_anywhere="false" enable_currentUserHome="true" enable_localSystem="false"/>
  <volume-check><allowed-os-versions><os-version min="13.0"/></allowed-os-versions></volume-check>
  <choices-outline><line choice="widget"/></choices-outline>
  <choice id="widget" visible="false" title="AI Usage Widget"><pkg-ref id="com.wernerong.ai-usage-widget.pkg"/></choice>
  <pkg-ref id="com.wernerong.ai-usage-widget.pkg" version="$version" onConclusion="None">widget-component.pkg</pkg-ref>
  <pkg-ref id="com.wernerong.ai-usage-widget.pkg"><must-close><app id="com.wernerong.ai-usage-widget"/></must-close></pkg-ref>
</installer-gui-script>
DIST
installer="$repo_dir/dist/AIUsageWidget-$version-$rid.pkg"
productbuild --distribution "$stage_root/Distribution.xml" --package-path "$stage_root" \
    --resources "$repo_dir/installer/macos" "$installer"
pkgutil --expand "$installer" "$stage_root/verify-package"
test -s "$stage_root/verify-package/Distribution"
echo "Installer: $installer"
