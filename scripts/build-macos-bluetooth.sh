#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$1"
arch="${2:-$(uname -m)}"
[[ "$arch" == "x64" ]] && arch=x86_64
mkdir -p "$output_dir"
# CI normally uses the active SDK. Local machines may have a newer SDK than swiftc;
# MACOS_BLUETOOTH_SDK can explicitly select a matching installed SDK.
sdk="${MACOS_BLUETOOTH_SDK:-$(xcrun --show-sdk-path)}"
xcrun swiftc -sdk "$sdk" -swift-version 5 -emit-library -O -target "$arch-apple-macos14.0" \
  "$repo_dir/Mac/Bluetooth/Radio.swift" -o "$output_dir/libPapergraphBluetooth.dylib"
