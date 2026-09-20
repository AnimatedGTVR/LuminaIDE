#!/usr/bin/env bash
# Installs an extracted binary download; no source code or SDK is needed.
set -euo pipefail
SOURCE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${LUMINA_PREFIX:-$HOME/.local}"
DEST="$PREFIX/share/luminaide"
[[ -x "$SOURCE/LuminaIDE" ]] || { echo 'Extract the complete download before running install.sh.' >&2; exit 1; }
[[ "$SOURCE" != "$DEST" ]] || { echo 'Run this installer from the extracted download, not the installed folder.' >&2; exit 1; }
mkdir -p "$DEST" "$PREFIX/bin" "$PREFIX/share/applications" "$PREFIX/share/icons/hicolor/scalable/apps"
cp -R "$SOURCE"/. "$DEST/"
ln -sfn "$DEST/LuminaIDE" "$PREFIX/bin/luminaide"
cp "$SOURCE/luminaide.svg" "$PREFIX/share/icons/hicolor/scalable/apps/luminaide.svg"
# Escape the desktop-entry Exec grammar (including spaces in the install path).
EXE="$DEST/LuminaIDE"
EXE="${EXE//\\/\\\\}"; EXE="${EXE//\"/\\\"}"; EXE="${EXE//\`/\\\`}"; EXE="${EXE//\$/\\\$}"
cat > "$PREFIX/share/applications/luminaide.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=LuminaIDE
Comment=A focused code editor
Exec="$EXE" %F
Icon=luminaide
Terminal=false
Categories=Development;TextEditor;
StartupWMClass=LuminaIDE
DESKTOP
echo "Installed to $DEST. Open LuminaIDE from your app menu."
echo "Command: $PREFIX/bin/luminaide"
