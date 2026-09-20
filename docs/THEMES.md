# Themes

A theme is one JSON file. LuminaIDE ships seven and you can add your own without touching the source.

## Making a theme

1. **Settings → New theme from the current one** copies the theme you are using into your themes folder and opens it.
2. Edit the colours and **save**. The theme reloads live, so you see each change immediately.
3. Or drop any `*.json` theme file into the themes folder yourself (Settings → *Open themes folder*).

Themes with the same `name` as a built-in one replace it. Broken files are listed under the Extensions panel instead of crashing anything.

Folder locations: Linux `~/.config/luminaide/themes`, macOS `~/Library/Application Support/luminaide/themes`, Windows `%APPDATA%\luminaide\themes`.

## File format

    {
      "name": "My Theme",
      "type": "dark",              // or "light": picks the matching control styling
      "ui": {
        "background": "#1c1a4b", "sidebar": "#16143b", "activityBar": "#110f30", "panel": "#16143b",
        "input": "#12102f", "border": "#2b2966", "foreground": "#e7e7eb", "muted": "#82819c",
        "accent": "#8b7cf6", "hover": "#25236a", "selection": "#3b3888", "lineHighlight": "#23215c",
        "lineNumber": "#5d5b7e", "statusBar": "#2a2770", "statusBarText": "#e7e7eb"
      },
      "syntax": {
        "keyword": "#ffb07c", "control": "#feaca9", "type": "#fea4e8", "function": "#ffc9a8",
        "definition": "#afbfff", "property": "#d7d7e6", "string": "#92cfa5", "number": "#f1b595",
        "comment": "#82819c", "constant": "#f1b595", "attribute": "#afbfff"
      }
    }

Any missing key falls back to Vanta Night, so a theme can be as short as a few colours.

| `ui` key | Used for |
|---|---|
| `background` | Editor and page background |
| `sidebar`, `activityBar`, `panel` | Side panel, the icon strip, tab bar / terminal / cards |
| `input` | Text boxes, code blocks, the terminal |
| `border` | Dividers and outlines |
| `foreground`, `muted` | Normal and secondary text |
| `accent` | Highlights, links, buttons, the active tab |
| `hover`, `selection`, `lineHighlight` | Hovered rows, selected rows/text, the current line |
| `lineNumber` | The gutter |
| `statusBar`, `statusBarText` | The bar at the bottom |

UI colours are opaque `#RRGGBB`. Legacy `#AARRGGBB` values still load, but their alpha is ignored for UI surfaces. Syntax colours retain their alpha.

Old Liquid Glass selections migrate to Vanta Night. The former `glass` and `windowEffect` fields are ignored; window blur and transparency are no longer used.
