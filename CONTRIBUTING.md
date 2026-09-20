# Contributing

Thanks for helping. LuminaIDE is three small projects that fit together:

| Folder | Language | What it does |
|---|---|---|
| `core/` | Rust | File/search engine, syntax tokenizer, Markdown parser, AI-agent runner. `cargo test --manifest-path core/Cargo.toml` |
| `native/` | C++17 | The terminal (PTY on Linux/macOS, pipes on Windows). |
| `app/` | C# / Avalonia | The window, editor, settings, panels. |
| `extensions/` | JSON | Built-in languages and themes. |

## Building

    ./build.sh            # Linux / macOS
    .\build.ps1           # Windows, from a Developer PowerShell for VS
    ./build.sh run .      # build and start it on the current folder

`luminaide --selftest` (or `dotnet app/bin/Release/net8.0/LuminaIDE.dll --selftest`) checks the native libraries without opening a window.

## Common contributions

- **A new language**: add `extensions/<bundle>/languages/<id>.json` and list it in that bundle's `extension.json`.
  `cargo test --manifest-path core/Cargo.toml` checks for duplicate ids, colliding extensions and tokenizer crashes. See `docs/LANGUAGES.md`.
- **A new license**: add `extensions/license-info/licenses/<id>.json` and list it in that extension's `extension.json`.
  The Rust tests check the data; add a fixture or an excerpt to `core/tests/licenses.rs` so detection is proven. See `docs/LICENSES.md`.
- **A new theme**: add a JSON file under `extensions/lumina-themes/themes/` (see `docs/THEMES.md`).
- **A bug fix**: add a test where you can (`core/` has unit and integration tests). Screenshots of UI changes help; the app can
  render itself to a PNG with `--screenshot out.png` (and `--page settings`, `--theme "Nord"`, ...).

Please keep changes focused, and update `CHANGELOG.md` under *Unreleased*.
