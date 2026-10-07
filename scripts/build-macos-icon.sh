#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
icon_work="$(mktemp -d "${TMPDIR:-/tmp}/papergraph-icon.XXXXXX")"
trap 'rm -rf "$icon_work"' EXIT
mkdir -p "$icon_work/Papergraph.iconset"
# Both platforms use the PNG master. Decoding paletted ICO files with sips
# can incorrectly turn full opacity into alpha 15/255; avoid that conversion.
cp "$repo_dir/Assets/AppIcon.png" "$icon_work/source.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$icon_work/source.png" --out "$icon_work/Papergraph.iconset/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -z "$retina" "$retina" "$icon_work/source.png" --out "$icon_work/Papergraph.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icon_work/Papergraph.iconset" -o "$repo_dir/Mac/Assets/Papergraph.icns"
