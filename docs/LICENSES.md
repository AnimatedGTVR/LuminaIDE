# License guide

Open a license file and LuminaIDE tells you, above the text, **which license it is** and **what it allows**, in plain language:

> **MIT License** · `MIT` · Permissive
> *You can:* use commercially · modify · distribute · use privately
> *You must:* keep the copyright notice
> *You can't count on:* liability · warranty

This is a plain-language summary, **not legal advice**. The license text below the banner is what actually applies.

## What counts as a license file

`LICENSE`, `LICENCE`, `COPYING`, `UNLICENSE`, `OFL-*`, and variants such as `LICENSE.md`, `LICENSE-MIT`, `COPYING.LESSER`, `LICENSE_third_party.txt`.
Any text file inside a folder called `licenses/` also counts. Code files that merely have "license" in the name (`license-checker.js`) do not.

For any other file, run **License: Explain This File** from the command palette (Ctrl+Shift+P): if the text is a known license, the banner appears.

## What it can tell you

| License | SPDX id | Type |
|---|---|---|
| Apache License 2.0 | `Apache-2.0` | Permissive |
| Boost Software License 1.0 | `BSL-1.0` | Permissive |
| BSD 2-Clause "Simplified" License | `BSD-2-Clause` | Permissive |
| BSD 3-Clause "New" or "Revised" License | `BSD-3-Clause` | Permissive |
| BSD Zero Clause License | `0BSD` | Public domain-like |
| Creative Commons Attribution 4.0 | `CC-BY-4.0` | Creative Commons |
| Creative Commons Attribution-ShareAlike 4.0 | `CC-BY-SA-4.0` | Creative Commons |
| Creative Commons Zero v1.0 Universal | `CC0-1.0` | Public domain |
| Do What The F*ck You Want To Public License | `WTFPL` | Public domain-like |
| Eclipse Public License 2.0 | `EPL-2.0` | Weak copyleft (per module) |
| GNU Affero General Public License v3.0 | `AGPL-3.0` | Network copyleft |
| GNU General Public License v2.0 | `GPL-2.0` | Strong copyleft |
| GNU General Public License v3.0 | `GPL-3.0` | Strong copyleft |
| GNU Lesser General Public License v2.1 | `LGPL-2.1` | Weak copyleft (library) |
| GNU Lesser General Public License v3.0 | `LGPL-3.0` | Weak copyleft (library) |
| ISC License | `ISC` | Permissive |
| MIT License | `MIT` | Permissive |
| MIT No Attribution | `MIT-0` | Permissive |
| Mozilla Public License 2.0 | `MPL-2.0` | Weak copyleft (per file) |
| SIL Open Font License 1.1 | `OFL-1.1` | Font license |
| The Unlicense | `Unlicense` | Public domain |
| zlib License | `Zlib` | Permissive |

If a file looks like a license but isn't one of these, the banner says so and tells you to read it carefully instead of guessing.
**Licenses: Browse the Guide…** in the palette lists all of them with a full explanation of every term.

## How the license is recognised

Each license has a few distinctive phrases ("fingerprints") that must all appear in the file, and optionally phrases that must not
(that's how MIT is told apart from MIT-0, and BSD-2 from BSD-3). Text is compared case-insensitively with punctuation and line breaks ignored,
so wrapped lines, comment markers and quote styles don't matter. If several licenses match, the one with more matching phrases wins, and
on a tie the one named **first** in the file wins (the MPL-2.0 text mentions the LGPL, but it names itself at the top).

Detection is a text match, not a legal analysis: a file that is *mostly* MIT with extra terms added is still recognised as MIT.
Always read the actual text.

## Adding your own licenses

A license is one JSON file, listed under `"licenses"` in an extension's `extension.json` (see [LANGUAGES.md](LANGUAGES.md) for extensions).

    {
      "id": "Apache-2.0",
      "name": "Apache License 2.0",
      "category": "Permissive",
      "summary": "A permissive license like MIT, plus an explicit patent grant...",
      "permissions": ["commercial-use", "modifications", "distribution", "patent-use", "private-use"],
      "conditions": ["include-copyright", "document-changes"],
      "limitations": ["trademark-use", "liability", "warranty"],
      "notes": ["If the project ships a NOTICE file, you must pass it on."],
      "url": "https://choosealicense.com/licenses/apache-2.0/",
      "matchAll": ["apache license version 2.0"],
      "matchNone": []
    }

`matchAll` is required (at least one phrase; without it a license could match anything). Tags you can use:

| Kind | Tags |
|---|---|
| `permissions` | `commercial-use`, `modifications`, `distribution`, `private-use`, `patent-use` |
| `conditions` | `include-copyright`, `include-copyright--source`, `document-changes`, `disclose-source`, `network-use-disclose`, `same-license`, `same-license--file`, `same-license--library` |
| `limitations` | `liability`, `warranty`, `trademark-use`, `patent-use` |

Unknown tags still show, using the tag text itself. The Rust tests (`core/tests/licenses.rs`) check every shipped license against real license texts.
