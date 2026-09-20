# Changelog

All notable changes to LuminaIDE are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Build Studio**: a graphical builder for Windows, macOS and Linux with prerequisite checks, live compiler output, cancellation, tests and self-contained download packaging.
- **Build from any browser**: manually trigger the Downloads workflow on GitHub to build Linux x64/ARM64, macOS Intel/Apple silicon and Windows x64 packages. Downloadable artifacts include SHA-256 checksums.
- **Binary installers** in Linux and Windows downloads add app-menu / Start-menu entries without requiring source code or developer tools.

### Changed
- **A calmer interface**: opaque surfaces, a neutral Vanta Night palette, clearer secondary text, compact welcome cards and shorter release-note previews.
- **Build scripts** now show numbered stages, check dependencies before building, preserve CMake generator choices, and expose build, run, test, package and GUI commands. Windows discovers Visual Studio Build Tools automatically.
- Updated Intel macOS release runners and documented platform setup, architecture selection and browser builds.

### Removed
- **Liquid Glass** themes, window blur settings and animated welcome-page orbs. Existing Liquid Glass selections migrate to Vanta Night; legacy translucent UI colours become opaque.

### Fixed
- Linux release downloads no longer include a source-only installer that cannot work from the archive.

## [0.2.0] - 2026-09-20

Looks, images and licenses.

### Added
- **Images in Markdown previews**: local files, `data:` URIs, HTML `<img>` tags (including centered logos), linked badges, and `https://` images. **SVG** works, so shields.io-style badges render. Remote images load in the background with size and time limits, are cached, and are never downgraded to http. A setting turns them off, and a blocked image can be loaded with one click.
- **Animated GIFs** in Markdown previews. Optimised GIFs are composed correctly, frame delays and loop counts are honoured (delays of 10 ms or less play at 100 ms, like browsers), all GIFs share one timer, and a GIF only plays while it is on screen. Click one to pause it; a setting turns animation off.
- **License guide**: opening a `LICENSE` / `COPYING` / `UNLICENSE`-style file shows which license it is and what it allows, in plain language (22 licenses; unknown ones say so). The command palette gains *License: Explain This File* and *Licenses: Browse the Guide…*. Licenses are extension data, so you can add your own.
- **File icons** in the explorer, tabs and quick open: colour-coded type badges and theme-tinted folders that open and close.

### Changed
- **A visual overhaul**: Inter (interface) and JetBrains Mono (code, terminal, Markdown code blocks) are now bundled, so text looks the same on every OS.
- Redesigned **tabs** (file icon, and a dirty dot that turns into a close button on hover), icon toolbars, a settings page made of cards, hover and fade transitions, and a quieter status bar.
- Avalonia updated to 11.2.7 (needed for SVG support).

### Fixed
- Markdown images written as `/assets/logo.png` now resolve from the **repository root** (the nearest folder with `.git` above the Markdown file, as on GitHub) instead of the folder open in the editor, so they load even when a parent folder is open.
- Images scale down to fit a narrow preview instead of being cropped, and `width=` / `height=` on `<img>` tags are honoured.

## [0.1.0] - 2026-09-20

The first build.

### Added
- **A small, fast editor**: explorer, project search (Ctrl+Shift+F), tabs, find (Ctrl+F), fuzzy quick open (Ctrl+P), a command palette (Ctrl+Shift+P), an integrated terminal (Ctrl+`), auto-save, zoom, and per-file language override.
- **89 languages** highlighted out of the box, including Nix, Haskell, OCaml, Elixir, Swift, Dart, PHP, Ruby, HTML/CSS/SCSS, Vue, Svelte, GLSL and more. A language is a small JSON file, so adding your own takes minutes.
- **Liquid Glass themes** (light and night) that blur the window behind them, plus Vanta Night, Graphite, Daylight, Ember, Nord, Dracula and Solarized Light.
- **Custom themes**: drop a JSON file into your themes folder, or start from the current theme in Settings. Saving a theme file updates it live.
- **Settings page** and a documented `settings.json` (comments allowed, applied the moment you save it).
- **Web & npm panel**: detects `package.json`, the package manager and frameworks, runs scripts in one click, links to the Vite dev server, and can scaffold a Vite app or a plain static site.
- **Markdown preview** beside the editor: bold, italics, lists, tables, task lists, quotes, local images and highlighted code blocks.
- **Run code with F5** in the built-in terminal for Python, Node, Rust, C/C++, Go, Java, Ruby, PHP, Vanta and many more; HTML files open in your browser.
- **AI agent panel** for Claude Code and Codex (uses the CLIs you already have installed).
- **Extensions**: folders with an `extension.json` that add languages and themes, loaded from your config folder.
- **A new welcome screen** with recent folders, quick actions and these release notes.
- **Safer file opening**: files over 8 MB, binary files, and files with extremely long lines are refused with a message instead of freezing the editor. Unexpected errors are written to `crash.log` and most are survived instead of closing the app.
- **Command line and installers**: `luminaide .` opens a folder (and returns immediately), `--version`, `--help` and `--selftest` (checks that the native libraries and shell work), `install.sh` / `install.ps1`, a macOS app-bundle builder, and CI and release workflows.
- **macOS and Windows** builds alongside Linux (see the README for what is verified where).

### Known limitations
- The built-in terminal is line-based: fine for commands and scripts, not for full-screen programs.
- Highlighting is lexical (no language servers yet), and the Markdown preview does not scroll in sync with the editor.
- Liquid Glass approximates the look using the platform's window blur; it is not Apple's private Liquid Glass API.
