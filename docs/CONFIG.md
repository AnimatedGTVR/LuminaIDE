# Configuration

Settings live in one file, `settings.json`. Change them on the **Settings page** (Ctrl+,) or edit the file directly.

| OS | Config folder |
|---|---|
| Linux | `~/.config/luminaide` (or `$XDG_CONFIG_HOME/luminaide`) |
| macOS | `~/.config/luminaide` |
| Windows | `%APPDATA%\luminaide` |

Open it from **Settings → Open settings.json** or the command palette. Files in that folder:

    settings.json       your settings
    themes/             custom themes (see THEMES.md)
    extensions/         your language/theme extensions (see LANGUAGES.md)
    crash.log           written if something unexpected goes wrong

## The file

`settings.json` allows `// comments` and trailing commas, keys are case-insensitive, and anything you leave out uses its default.
It is applied **the moment you save it in LuminaIDE**. If the file has an error the defaults are used, your file is kept as
`settings.json.broken`, and a message tells you so; the app never overwrites a file it couldn't read.

Changing a setting on the Settings page rewrites the file and drops any comments in it.

    {
      "theme": "Vanta Night",
      "editorFontFamily": "JetBrains Mono",
      "editorFontSize": 14,
      "tabSize": 2,
      "insertSpaces": true,
      "autoSave": "afterDelay",
      "trimTrailingWhitespace": true
    }

## Options

| Key | Default | Meaning |
|---|---|---|
| `theme` | `"Vanta Night"` | Name of any built-in, extension or custom theme. |
| `editorFontFamily` | `""` | Any installed font; falls back to the bundled JetBrains Mono, then Cascadia Code, Fira Code, DejaVu Sans Mono… |
| `editorFontSize` | `14` | 8–40. Ctrl + / Ctrl − / Ctrl 0 change it on the fly. |
| `tabSize` | `4` | 1–16. |
| `insertSpaces` | `true` | Tab key inserts spaces. |
| `wordWrap` | `false` | Wrap long lines. |
| `lineNumbers` | `true` | Show the gutter. |
| `highlightCurrentLine` | `true` | Tint the line the cursor is on. |
| `autoSave` | `"off"` | `"afterDelay"` (1.5 s after you stop typing) or `"onFocusLost"`. |
| `trimTrailingWhitespace` | `false` | Remove trailing spaces when saving. |
| `insertFinalNewline` | `false` | End files with a newline when saving. |
| `terminalFontSize` | `12.5` | 8–32. |
| `markdownMode` | `"split"` | `"split"`, `"preview"` or `"editor"` for Markdown files. |
| `markdownRemoteImages` | `true` | Show `https://` images in Markdown previews (badges, screenshots). Loading one contacts the site hosting it. `false` downloads nothing. |
| `markdownAnimateGifs` | `true` | Play animated GIFs in Markdown previews (click one to pause it). `false` shows the first frame only. |
| `agent` | `"claude"` | Default AI agent: `"claude"` or `"codex"`. |
| `agentMode` | `"read"` | `"read"` (plan, cannot change files) or `"edit"`. |
| `lastVersion` | | Bookkeeping: which release notes you have seen. |
| `recentFolders` | | Bookkeeping: the welcome screen's recent list. |
