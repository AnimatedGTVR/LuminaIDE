# Download or build LuminaIDE

## Use a ready-made download

Open the [Releases page](https://github.com/AnimatedGTVR/LuminaIDE/releases) and choose the package for your computer.
The archives include .NET; you do not need a compiler, Rust, Python or the .NET SDK to run them.

| Computer | Download | Open / install |
|---|---|---|
| Linux Intel/AMD 64-bit | `linux-x64.tar.gz` | Extract, run `./LuminaIDE`; optionally run the included `./install.sh` for an app-menu entry. |
| Linux ARM64 | `linux-arm64.tar.gz` | Same; requires a desktop Linux installation, not Android. |
| Mac Apple silicon | `osx-arm64.zip` | Extract and move `LuminaIDE.app` into Applications. |
| Mac Intel | `osx-x64.zip` | Same. |
| Windows Intel/AMD 64-bit | `win-x64.zip` | Extract all files and run `LuminaIDE.exe`; optionally right-click the included `install.ps1` and run with PowerShell for a Start-menu shortcut. |

Windows ARM users can try the x64 package through Windows' x64 emulation; a native Windows ARM package is not currently provided. Downloads are not yet signed/notarized. Follow your OS's standard approval flow for a trusted unsigned app; do not disable system-wide protections.

Linux builds require glibc and desktop libraries (X11, fontconfig and a working graphics stack); Alpine/musl is not supported. Packages are built on Ubuntu 22.04 for x64 and 24.04 for ARM64. Support on other distributions depends on their system libraries.

Each archive has a `.sha256` sidecar. On Linux run `sha256sum -c <archive>.sha256`; on macOS run `shasum -a 256 -c <archive>.sha256`. On Windows compare `Get-FileHash <archive> -Algorithm SHA256` with the sidecar. A matching hash checks file integrity, not publisher identity.

## Build from a browser — any device

1. [Fork the repository](https://github.com/AnimatedGTVR/LuminaIDE/fork) on GitHub, or use a repository where you have Actions write access.
2. Open **Actions → Downloads → Run workflow** and select the branch.
3. The workflow builds Linux x64/ARM64, macOS Intel/Apple silicon and Windows x64 on native runners.
4. Open the finished run and download the desired artifact. Extract the artifact ZIP first, then the package inside it. Manual-build artifacts expire after 14 days.

This works from a phone, tablet or Chromebook browser. Compilation runs on GitHub's computers; the desktop app itself does not run on iOS, Android or in the browser. Enable Actions on a new fork first. Runner availability and Actions usage limits depend on your GitHub account.

Tagged builds create a draft Release. The maintainer publishes it to make permanent downloads available without an Actions login. See [RELEASING.md](RELEASING.md).

## Get the source

Download the [source ZIP](https://github.com/AnimatedGTVR/LuminaIDE/archive/refs/heads/main.zip) and extract it, or clone it:

    git clone https://github.com/AnimatedGTVR/LuminaIDE.git
    cd LuminaIDE

## Local prerequisites

All platforms need the **.NET 8 SDK**, **Rust stable**, **CMake 3.20+**, and a **C++17 compiler**. Use a toolchain matching your computer's architecture. Network access is needed on the first build to fetch packages. Ninja is optional.

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — install the SDK, not just the runtime.
- [Rust](https://rustup.rs) — includes Cargo; restart your terminal after installing.
- [CMake](https://cmake.org/download/) — add it to PATH.

### Windows

Install Visual Studio 2022 Build Tools with **Desktop development with C++**, the Windows SDK and CMake tools. Use the x64 MSVC Rust toolchain (`x86_64-pc-windows-msvc`). The script locates Visual Studio automatically; a Developer PowerShell also works. Launch from a fresh PowerShell after installing dependencies.

    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 --check

The execution-policy option applies only to that invocation. Windows packages currently target x64.

### macOS

Install Apple's command-line compiler tools with `xcode-select --install`, then install the .NET 8 SDK, Rust and CMake. If using Homebrew, `brew install cmake ninja` supplies the build tools. Use native ARM64 tools on Apple silicon and x64 tools on Intel, without mixing Rosetta and native tools.

    ./build.sh --check

### Linux

On Debian/Ubuntu, `sudo apt install build-essential cmake ninja-build` supplies the C++ tools. Install the .NET 8 SDK and Rust using the links above. Other distributions have equivalent packages.

    ./build.sh --check

## Graphical Build Studio

After installing the .NET 8 SDK:

- **Windows:** double-click `Build.cmd`.
- **macOS:** open `Build.command` (or run `./build.sh --gui`).
- **Linux:** run `./build.sh --gui`.

Choose the extracted source folder, then use **Check tools**, **Build editor**, **Build + test** or **Create download**. Compiler output appears live. **Stop** terminates the active build and its child processes. **Save log** exports the displayed output (the last approximately 200–250 KB for long runs). **Open downloads** opens `dist/`.

Build Studio shares the editor's Inter / JetBrains Mono fonts, theme colours and controls. It reads your editor theme at startup; its Appearance picker previews built-in and custom themes without changing your editor settings.

The GUI compiles on first launch and uses the same scripts as CI. It does not silently install dependencies or require Python. Keep one build running per source folder; build output directories are shared.

## Terminal commands

| Action | Linux / macOS | Windows PowerShell |
|---|---|---|
| Check dependencies | `./build.sh --check` | `.\build.ps1 --check` |
| Build | `./build.sh` | `.\build.ps1` |
| Build and open | `./build.sh run .` | `.\build.ps1 run .` |
| Build and test | `./build.sh test` | `.\build.ps1 test` |
| Create download | `./build.sh package` | `.\build.ps1 package` |
| Graphical builder | `./build.sh --gui` | `.\build.ps1 --gui` |

The app build lands in `app/bin/Release/net8.0/`. Packaging creates an archive plus checksum in `dist/`, runs the published app's self-test, and bundles the native libraries, extensions, fonts and notices. Packages target the host OS/architecture; building Windows from Linux or macOS from Windows requires the browser workflow above.

Source installation is still available with `./install.sh` or `.\install.ps1`. The optional installers inside downloaded archives install existing binaries and never try to compile source.
