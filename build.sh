#!/usr/bin/env bash
# Native builds for Linux and macOS. Windows: build.ps1.
set -Eeuo pipefail
cd "$(dirname "$0")"
MODE="${1:-build}"
if [[ $# -gt 0 ]]; then shift; fi
case "$MODE" in
  -h|--help|help)
    echo 'LuminaIDE build studio'
    echo 'Usage: ./build.sh [build|run [app args...]|test|package|--check|--gui]'
    echo 'package creates a self-contained archive in dist/ for this computer.'
    exit 0 ;;
  build|run|test|package|--check|--gui) ;;
  *) echo "Unknown command: $MODE. Use --help." >&2; exit 2 ;;
esac
ACCENT='' RESET=''
if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then ACCENT=$'\033[1;36m'; RESET=$'\033[0m'; fi
step() { printf '\n%s%s%s\n' "$ACCENT" "$1" "$RESET"; }
trap 'printf "\nBuild stopped (line %s). See the error above; help: docs/BUILDING.md\n" "$LINENO" >&2' ERR
step 'LuminaIDE / Build studio'
printf 'Host: %s · %s\n' "$(uname -s)" "$(uname -m)"
if [[ "$MODE" == --gui ]]; then
  command -v dotnet >/dev/null || { echo 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'; exit 1; }
  exec dotnet run --project tools/Builder/Builder.csproj -c Release
fi
missing=0
for tool in dotnet cargo cmake; do
  if command -v "$tool" >/dev/null; then printf '  OK       %s\n' "$tool"; else printf '  MISSING  %s\n' "$tool"; missing=1; fi
done
if command -v c++ >/dev/null; then echo '  OK       C++ compiler'; else echo '  MISSING  C++ compiler'; missing=1; fi
if [[ "$missing" == 1 ]]; then echo 'Install the missing tools using docs/BUILDING.md, then try again.'; exit 1; fi
if ! dotnet --list-sdks | grep -q '^8\.'; then echo 'The .NET 8 SDK is required. See docs/BUILDING.md.'; exit 1; fi
[[ "$MODE" == --check ]] && exit 0
START=$SECONDS
LIB="$PWD/build/lib"
mkdir -p "$LIB"
step '[1/3] Rust engine'
cargo build --release --locked --manifest-path core/Cargo.toml
case "$(uname -s)" in
  Linux) NATIVE_EXT=so; OS_RID=linux ;;
  Darwin) NATIVE_EXT=dylib; OS_RID=osx ;;
  *) echo 'Use build.ps1 on Windows.'; exit 1 ;;
esac
cp "core/target/release/liblumina_core.$NATIVE_EXT" "$LIB/"
step '[2/3] C++ terminal'
GEN=()
if [[ ! -f build/native/CMakeCache.txt ]] && command -v ninja >/dev/null; then GEN=(-G Ninja); fi
cmake -S native -B build/native "${GEN[@]}" -DCMAKE_BUILD_TYPE=Release "-DLUMINA_OUT=$LIB"
cmake --build build/native --config Release --parallel
step '[3/3] Desktop app'
dotnet build app/LuminaIDE.csproj -c Release --nologo -v minimal
if [[ "$MODE" == test ]]; then
  step 'Verification'
  cargo test --locked --manifest-path core/Cargo.toml
  dotnet test tests/LuminaIDE.Tests -c Release --nologo -v minimal
  dotnet app/bin/Release/net8.0/LuminaIDE.dll --selftest
fi
if [[ "$MODE" == package ]]; then
  case "$(uname -m)" in x86_64) ARCH=x64 ;; arm64|aarch64) ARCH=arm64 ;; *) echo 'Supported architectures: x64 and arm64'; exit 1 ;; esac
  RID="$OS_RID-$ARCH"
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' app/LuminaIDE.csproj)"
  NAME="LuminaIDE-v$VERSION-$RID"
  STAGE="$(mktemp -d)"
  trap 'rm -rf "$STAGE"' EXIT
  step "Packaging / $RID"
  dotnet publish app/LuminaIDE.csproj -c Release -r "$RID" --self-contained true -o "$STAGE/LuminaIDE" --nologo -v minimal
  "$STAGE/LuminaIDE/LuminaIDE" --selftest
  mkdir -p dist
  if [[ "$OS_RID" == osx ]]; then
    packaging/macos/make-app.sh "$STAGE/LuminaIDE" "$STAGE/bundle" "$VERSION"
    ARCHIVE="dist/$NAME.zip"
    ditto -c -k --keepParent "$STAGE/bundle/LuminaIDE.app" "$ARCHIVE"
  else
    cp packaging/linux/install-binary.sh "$STAGE/LuminaIDE/install.sh"
    cp packaging/linux/luminaide.svg "$STAGE/LuminaIDE/"
    ARCHIVE="dist/$NAME.tar.gz"
    tar -C "$STAGE" -czf "$ARCHIVE" LuminaIDE
  fi
  (cd dist; if command -v sha256sum >/dev/null; then sha256sum "$(basename "$ARCHIVE")"; else shasum -a 256 "$(basename "$ARCHIVE")"; fi) > "$ARCHIVE.sha256"
  printf '\nPackage: %s/%s\n' "$PWD" "$ARCHIVE"
fi
step "Finished in $((SECONDS - START))s"
echo 'App: app/bin/Release/net8.0/LuminaIDE'
if [[ "$MODE" == run ]]; then exec dotnet app/bin/Release/net8.0/LuminaIDE.dll "$@"; fi
