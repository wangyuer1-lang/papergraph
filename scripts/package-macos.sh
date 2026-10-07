#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
arch="${1:-arm64}"
case "$arch" in arm64|x64) ;; *) echo 'Usage: package-macos.sh [arm64|x64] [output-directory]' >&2; exit 2;; esac
output_dir="${2:-$repo_dir/artifacts/macos-$arch}"
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
app="$output_dir/Papergraph.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
bash "$repo_dir/scripts/build-macos-icon.sh"
"${DOTNET:-dotnet}" publish "$repo_dir/Mac/Papergraph.Mac.csproj" -c Release -r "osx-$arch" --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o "$app/Contents/MacOS"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Papergraph</string>
<key>CFBundleDisplayName</key><string>Papergraph</string>
<key>CFBundleIdentifier</key><string>org.papergraph.desktop</string>
<key>CFBundleVersion</key><string>0.15.7</string>
<key>CFBundleShortVersionString</key><string>0.15.0</string>
<key>CFBundleIconFile</key><string>Papergraph.icns</string>
<key>CFBundleExecutable</key><string>papergraph</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSPrincipalClass</key><string>NSApplication</string>
<key>NSHumanReadableCopyright</key><string>MIT License · papergraph contributors</string>
</dict></plist>
PLIST
cp "$repo_dir/Mac/Assets/Papergraph.icns" "$app/Contents/Resources/Papergraph.icns"
cp "$repo_dir/LICENSE" "$app/Contents/Resources/LICENSE"
chmod +x "$app/Contents/MacOS/papergraph"
if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
  codesign --force --deep --options runtime --entitlements "$repo_dir/Mac/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$app"
else
  codesign --force --deep --sign - "$app"
fi
codesign --verify --deep --strict "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$output_dir/Papergraph-0.15.0-preview.7-macos-$arch.zip"
echo "$output_dir/Papergraph-0.15.0-preview.7-macos-$arch.zip"
