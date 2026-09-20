#!/usr/bin/env bash
# Wraps a `dotnet publish` folder into LuminaIDE.app.
# Usage: make-app.sh <publish-dir> <output-dir> <version>
set -euo pipefail
PUB="$1"; OUT="$2"; VER="$3"
HERE="$(cd "$(dirname "$0")" && pwd)"
APP="$OUT/LuminaIDE.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
cp "$HERE/icon.icns" "$APP/Contents/Resources/icon.icns"
sed "s/__VERSION__/$VER/g" "$HERE/Info.plist.in" > "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/LuminaIDE"

# Ad-hoc signature: enough for Apple-silicon Macs to run it locally. It is NOT a Developer ID signature,
# so a downloaded copy still triggers Gatekeeper (right-click > Open, or notarize; see docs/RELEASING.md).
if command -v codesign >/dev/null; then codesign --force --deep --sign - "$APP" || echo "codesign failed (continuing unsigned)"; fi
echo "Built $APP"
