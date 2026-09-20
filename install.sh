#!/usr/bin/env bash
# Installs LuminaIDE for the current user (Linux and macOS). Windows: install.ps1.
#
#   Linux : ~/.local/bin/luminaide  +  ~/.local/share/luminaide/  +  an app-menu launcher and icon
#   macOS : ~/Applications/LuminaIDE.app  +  ~/.local/bin/luminaide
#
#   ./install.sh              build and install (run again after changing the code)
#   ./install.sh --uninstall  remove everything it installed
#
# `luminaide` opens detached, like `code`; `luminaide -f` stays in the terminal to show output.
# LUMINA_PREFIX=/some/dir installs somewhere other than ~/.local.
set -euo pipefail
cd "$(dirname "$0")"

PREFIX="${LUMINA_PREFIX:-$HOME/.local}"
BIN="$PREFIX/bin/luminaide"
APP_DIR="$PREFIX/share/luminaide"                                  # Linux
DESKTOP="$PREFIX/share/applications/luminaide.desktop"             # Linux
ICON="$PREFIX/share/icons/hicolor/scalable/apps/luminaide.svg"     # Linux
APPS="${LUMINA_APPS:-$HOME/Applications}"                          # macOS
MAC_APP="$APPS/LuminaIDE.app"
IS_MAC=false; [[ "$(uname -s)" == "Darwin" ]] && IS_MAC=true

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$APP_DIR" "$BIN" "$DESKTOP" "$ICON" "$MAC_APP"
  echo "Removed LuminaIDE (your settings in ~/.config/luminaide are kept)."
  exit 0
fi

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' app/LuminaIDE.csproj | head -1)"

echo "==> Building"
./build.sh

echo "==> Publishing"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
dotnet publish app/LuminaIDE.csproj -c Release -o "$STAGE/app" --no-self-contained --nologo -v q
mkdir -p "$(dirname "$BIN")"

if $IS_MAC; then
  echo "==> Installing $MAC_APP"
  mkdir -p "$APPS"
  packaging/macos/make-app.sh "$STAGE/app" "$APPS" "$VERSION"
  TARGET="$MAC_APP/Contents/MacOS/LuminaIDE"
else
  echo "==> Installing to $APP_DIR"
  rm -rf "$APP_DIR"
  mkdir -p "$(dirname "$APP_DIR")" "$(dirname "$DESKTOP")" "$(dirname "$ICON")"
  cp -R "$STAGE/app" "$APP_DIR"
  cp packaging/linux/luminaide.svg "$ICON"
  cat > "$DESKTOP" <<DESKTOP_FILE
[Desktop Entry]
Type=Application
Name=LuminaIDE
Comment=A small code editor with an agent panel and Markdown preview
Exec=$BIN %F
Icon=luminaide
Terminal=false
Categories=Development;TextEditor;
MimeType=text/plain;text/markdown;
StartupWMClass=LuminaIDE
DESKTOP_FILE
  command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
  command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q "$PREFIX/share/icons/hicolor" 2>/dev/null || true
  TARGET="dotnet $APP_DIR/LuminaIDE.dll"
fi

# The command. Detached by default; -f / --foreground (or LUMINA_FOREGROUND=1) keeps it attached.
# Anything it prints while detached goes to ~/.local/state/luminaide/last-run.log.
cat > "$BIN" <<LAUNCHER
#!/bin/sh
if [ "\${1:-}" = "-f" ] || [ "\${1:-}" = "--foreground" ]; then shift; exec $TARGET "\$@"; fi
if [ -n "\${LUMINA_FOREGROUND:-}" ]; then exec $TARGET "\$@"; fi
LOG="\${XDG_STATE_HOME:-\$HOME/.local/state}/luminaide"
mkdir -p "\$LOG"
nohup $TARGET "\$@" >>"\$LOG/last-run.log" 2>&1 </dev/null &
LAUNCHER
chmod +x "$BIN"

echo
echo "Installed LuminaIDE $VERSION."
echo "  luminaide .          open the current folder"
echo "  luminaide notes.md   open a file"
echo "  luminaide --selftest check that everything works on this machine"
case ":$PATH:" in
  *":$(dirname "$BIN"):"*) ;;
  *) echo
     echo "Note: $(dirname "$BIN") is not on your PATH. Add this to your shell profile, then open a new terminal:"
     echo "      export PATH=\"$(dirname "$BIN"):\$PATH\"" ;;
esac
