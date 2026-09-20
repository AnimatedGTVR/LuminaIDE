> Historical 0.2.0 design brief. For the current opaque design, seven themes, and download/build flows, apply `v0-downloads-prompt.txt` after this prompt. Its instructions override this brief.

# BUILD THE MARKETING WEBSITE FOR "LuminaIDE"

You are building the one-page marketing and download website for **LuminaIDE**, a small, fast, good-looking desktop code editor. Build a polished, production-quality, single-page site (plus a couple of tiny utility routes) with Next.js, TypeScript and Tailwind. Read this whole brief before writing code: it contains the product facts (your only source of truth), the visual system, the exact copy, the interactive demos with their data, and a list of things you must not do. Where I give copy, use it verbatim. Where I say "placeholder", leave an obvious, easy-to-replace placeholder.

The single most important instruction: **the site must be truthful.** LuminaIDE is a real, young project (version 0.2.0, an early release). Do not invent features, numbers, benchmarks, testimonials, download counts, GitHub stars, user logos, awards or press quotes. If something is not in the facts below, it does not exist. A confident, honest, beautifully designed page beats an inflated one.

---

## 1. WHAT LUMINAIDE IS (SOURCE OF TRUTH)

LuminaIDE is a desktop code editor for Linux, macOS and Windows. It is free and open source (MIT license). It is built with Rust (the engine), C++ (the terminal) and C# with Avalonia (the interface). Tagline used inside the app: "Light, fast and out of your way." The README's positioning: a small, fast code editor for people who like the idea of VS Code but not the weight of it. Its scope is deliberately small.

### Everything it does (and nothing more)
1. **Editor**: tabs, find in file (Ctrl+F), fuzzy quick open for files (Ctrl+P), a command palette (Ctrl+Shift+P), auto-save (off / after a pause / when focus leaves), editor zoom, per-file language override, save clean-ups (trim trailing whitespace, final newline), file-type icons in the explorer and tabs.
2. **89 languages** highlighted out of the box, including Nix, Haskell, OCaml, Elixir, Swift, Dart, PHP, Ruby, Zig, Kotlin, GLSL, WGSL, Terraform, Dockerfile, HTML, CSS, Vue, Svelte and more (full list in section 7). A language is one small JSON file, so adding your own takes minutes. Highlighting is lexical (comments, strings, numbers, keywords, structure), not a full language server.
3. **9 themes**: Vanta Night (the default), Graphite, Daylight, Ember, Nord, Dracula, Solarized Light, and two **Liquid Glass** themes (light and night) that make the window translucent and blur what is behind it. Custom themes: drop a JSON file into the themes folder, or start from the current theme in Settings; **saving the file updates the theme live**. Honest note that must appear on the site: Liquid Glass is a translucent theme plus each operating system's own window blur (macOS, Windows, and Linux compositors that support it). It is an approximation; it is *not* Apple's Liquid Glass material.
4. **Markdown preview** beside the editor (side by side, preview only, or editor only). It renders headings, bold, italics, strikethrough, inline code, links, nested and task lists, quotes, tables, rules and syntax-highlighted code blocks, and it follows the theme. It shows **images**: local files, `data:` images, HTML `<img>` tags (including centred logos), linked badges, `https://` images including **SVG badges** (shields.io style), and **animated GIFs** (click one to pause it). Remote images are only loaded over https, are size-limited and cached, and there is a setting to turn them off.
5. **Web & npm panel**: detects `package.json`, the package manager (npm, pnpm, yarn or bun, from the lockfile), and frameworks (Vite, React, Vue, Svelte, Next.js, Nuxt, Astro, Angular, TypeScript, Tailwind and others). Lists your scripts with a one-click run in the built-in terminal, detects `http://localhost:5173/`-style dev-server addresses in the terminal output and offers an "Open in browser" link, and can scaffold a Vite app (vanilla, React, Vue, Svelte, with or without TypeScript) or a plain static website. It refuses to run script names that contain shell metacharacters.
6. **Run code with F5** in the built-in terminal, using a per-language run command (Python, Node, TypeScript, Rust, C, C++, C#, Go, Java, Ruby, PHP, Lua, Zig, Julia, Dart, Swift, Haskell, Elixir, and more). HTML files open in your browser. Shift+F5 stops.
7. **AI agent panel** for **Claude Code** and **Codex**: it runs the CLI you already have installed inside your open folder and streams its answer and tool calls into a chat panel. **Plan / read-only mode is the default**; "Edit files" mode is an explicit switch. Your open file and selection can be sent as context. When a turn ends, open tabs whose files changed are reloaded, and tabs with unsaved edits are never overwritten. LuminaIDE stores no API keys and makes no AI requests itself. (The Claude Code integration was built and tested against output captured from the real CLI; Codex support follows its documented event format and has not been run against a live Codex CLI yet. Do not overstate either.)
8. **License guide**: open a `LICENSE` / `COPYING` / `UNLICENSE`-style file and a banner above the text says which license it is and, in plain language, what it allows: "You can", "You must", "You can't count on". It knows 22 licenses (MIT, Apache-2.0, the GPL family, AGPL, MPL, BSD, ISC, CC0, OFL, Creative Commons and more), says so honestly when it does not recognise one, and always states that it is a plain-language summary, not legal advice. Licenses are data, so extensions can add more.
9. **Settings page and `settings.json`**: every option documented; the file allows comments and is applied the moment you save it; a broken file is never overwritten (it is kept as `settings.json.broken`).
10. **Extensions**: a folder with an `extension.json` that adds languages, themes and licenses. (These are data extensions, not a code-plugin API.)
11. **Built-in terminal** (line-based: great for commands and scripts; not for full-screen programs like vim or htop), **welcome screen** with recent folders and a live "What's new", **release notes** inside the app, and a self-test command (`luminaide --selftest`).
12. Bundled fonts: **Inter** (interface) and **JetBrains Mono** (code). Both open-licensed.

### What LuminaIDE does NOT do (yet). Say this plainly on the site.
No language server (so no autocomplete or go-to-definition), no debugger, no git panel, no code-plugin/marketplace system, no multi-cursor, no minimap, no full terminal emulator (vim/htop do not work in the built-in terminal), the Markdown preview does not scroll in sync with the editor. Never imply otherwise.

### Platforms: be precise
- **Linux (x64)**: developed and tested. Ready.
- **macOS (Apple silicon and Intel)** and **Windows (x64)**: builds are produced by CI and have a self-test, but the author has not yet run the app on a Mac or on Windows. Present these as **"Preview"** builds and invite people to try them and report problems. The macOS app is not notarized yet (first launch: right-click, Open); the Windows exe is not code-signed yet (SmartScreen may warn). Show these two facts in the download section, in small honest print.
- Releases will be on GitHub. Use `https://github.com/AnimatedGTVR/LuminaIDE` and define it once as a constant in `lib/site.ts` (`REPO_URL`), used everywhere.

### Facts you may quote
Version 0.2.0 (an early release). MIT license. 89 languages. 9 themes. 22 licenses in the license guide. Built with Rust, C++ and C# (Avalonia). Three operating systems. Bundled fonts Inter and JetBrains Mono. Nothing else is a fact. **Do not** state startup times, memory use, install size, or "faster than X".

### Privacy statements you may make
"No telemetry. No account." LuminaIDE never phones home. The two things that can touch the network are ones you switch on or invoke yourself: the AI agent panel (it runs your own Claude Code / Codex CLI, which talks to its provider) and remote images in Markdown previews (https only, can be turned off in Settings).

---

## 2. AUDIENCE, POSITIONING AND VOICE

Audience: developers who love a great editor but want something smaller and calmer: hobbyists, students, people who write a lot of Markdown and web code, Linux and Nix folks, and anyone who likes tools that feel designed. Secondary: people curious about Rust/Avalonia apps.

Positioning: **a small editor with taste.** Not "the VS Code killer". Never disparage another product by name. If you must compare, say "the familiar layout (explorer, tabs, command palette) without the weight". Do not claim it is lighter or faster than anything; you have no data.

Voice: warm, confident, concrete, a little playful, never hype. Short sentences. Verbs over adjectives. No buzzwords ("revolutionary", "next-gen", "blazing", "supercharge", "seamless", "unleash", "game-changing"). No emoji in headings. Sentence case for headings. Use "you". Prefer a specific detail ("a `LICENSE` file tells you what it allows") over a general promise.

---

## 3. BRAND AND DESIGN SYSTEM

The site should feel like the app: a deep indigo canvas, soft glowing colour, glass-like cards, crisp type, and syntax-highlight colours used as accents. It should look designed and distinctive, not like a generic AI-generated SaaS template (avoid: white cards on purple gradients, stock 3D blobs, giant emoji, endless identical feature cards).

### Logo
A rounded square with a diagonal gradient from #8b7cf6 to #fea4e8 and a white rounded "L" stroke. Use this exact SVG as the logo and as the favicon (`app/icon.svg`):
```
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#8b7cf6"/>
      <stop offset="1" stop-color="#fea4e8"/>
    </linearGradient>
  </defs>
  <rect width="64" height="64" rx="14.4" fill="url(#g)"/>
  <path d="M21.76 15.36v28.16H44.8" fill="none" stroke="#fff" stroke-width="7.4" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
```
Wordmark: "LuminaIDE" in Inter, weight 700, letter-spacing -0.02em. In the wordmark "IDE" is the same weight; give the whole word a subtle horizontal gradient (foreground to accent) on large sizes only.

### Colour tokens (dark is the default theme)
Define as CSS variables on `:root` (dark) and `[data-theme="light"]`, wire them into Tailwind's theme (`bg-background`, `text-foreground`, etc.).
- Dark ("Vanta Night", from the app): `--bg #14122f` (page), `--surface #1c1a4b` (editor-like surfaces), `--surface-2 #16143b`, `--border #2b2966`, `--fg #e7e7eb`, `--muted #82819c`, `--accent #8b7cf6`, `--accent-2 #fea4e8`.
- Syntax accents you may use for tags, chips and highlights: orange `#ffb07c`, peach `#f1b595`, salmon `#feaca9`, pink `#fea4e8`, green `#92cfa5`, blue `#afbfff`.
- Light ("Daylight"): `--bg #fbfbfd`, `--surface #ffffff`, `--surface-2 #f0f1f6`, `--border #d9dce6`, `--fg #23252e`, `--muted #7a7f92`, `--accent #5b5bd6`, `--accent-2 #c2278f`.
The site follows the system colour scheme by default and has a sun/moon toggle in the nav that persists in localStorage. No flash of the wrong theme (set the attribute in a tiny inline script in `<head>`).

### Typography
Use `next/font`: **Inter** for text and **JetBrains Mono** for code and small technical labels. Hero heading 56-72px (clamp), weight 700, tight leading (1.05), tracking -0.03em. Section headings 36-44px. Body 17-18px with 1.65 line-height. Small caps section eyebrows: 12px, weight 600, letter-spacing 0.14em, uppercase, colour muted. Keep line length under 70 characters.

### Shape, depth, texture
Radius scale: 8 / 12 / 16 / 24. Cards are "glass": translucent surface (fg at 5-7% opacity over the background), 1px border (fg at 14-16% opacity), `backdrop-filter: blur(12px)`, a soft shadow, and on hover they lift 2px and brighten. Subtle film grain over the hero (an inline SVG noise at 3-4% opacity). Generous spacing: sections have 96-140px vertical padding on desktop; content max width 1120px; hero max width 1280px.

### Background "orbs"
The hero (and softly the whole page) has three large blurred radial-gradient orbs in accent violet (#8b7cf6), pink (#fea4e8) and mint (#92cfa5), 35-45% opacity in dark and 20-25% in light, drifting slowly with CSS keyframes (translate and slight scale, 30-45 seconds, ease-in-out, alternate). Position them absolutely with `pointer-events: none`, `will-change: transform`. Disable animation under `prefers-reduced-motion`.

### Motion
Framer Motion (or CSS): sections fade and rise 16px into view once (`whileInView`, `once: true`, viewport margin -80px), staggered by 60ms for grids. Hover micro-interactions on cards and buttons (120-160ms). The hero screenshot gently tilts a few degrees with the pointer on desktop (max 4deg), flat on touch. Nothing loops except the orbs, the language marquee and the GIF-like demo caret blink. Respect `prefers-reduced-motion` everywhere (no movement, instant transitions).

---

## 4. TECH STACK AND PROJECT STRUCTURE

- **Next.js 15 (App Router), React 19, TypeScript (strict), Tailwind CSS 4 (or 3.4 if 4 is unavailable), shadcn/ui** (Button, Badge, Tabs, Accordion, Dialog/Command, Tooltip, Switch, ScrollArea), **lucide-react** icons, **framer-motion**.
- Everything is static and client-side: no database, no API keys, no analytics, no cookies, no third-party scripts, no external network requests at runtime. Deploys to Vercel as a static export-friendly site.
- Suggested structure (adapt as needed):
  - `app/layout.tsx` (fonts, metadata, theme script), `app/page.tsx` (composes sections), `app/icon.svg`, `app/opengraph-image.tsx`, `app/sitemap.ts`, `app/robots.ts`, `app/not-found.tsx`
  - `lib/site.ts` (`REPO_URL`, `VERSION`, `RELEASES_URL`, nav links, all copy constants), `lib/data/themes.ts`, `lib/data/languages.ts`, `lib/data/licenses.ts`, `lib/data/commands.ts`
  - `components/` : `Nav`, `Hero`, `ScreenshotFrame`, `Orbs`, `Section`, `FeatureGrid`, `ThemeShowcase`, `FakeEditor`, `MarkdownDemo`, `WebPanelDemo`, `AgentDemo`, `LicenseExplorer`, `LanguageWall`, `CustomizeTabs`, `Downloads`, `HonestLimits`, `Faq`, `Footer`, `CommandPalette`, `ThemeToggle`, `CopyButton`, `Kbd`
- Screenshots: reference these files by path and I will drop them into `public/screenshots/` afterwards. Every one is a 1280x820 PNG. Wrap them in a `ScreenshotFrame` component (rounded 16px, 1px border, deep shadow, subtle glow) that shows a tasteful skeleton if the image is missing, and use `next/image` with explicit width/height and meaningful alt text.
  - `/screenshots/editor.png`: the editor with file explorer (icons), two tabs, and the terminal open.
  - `/screenshots/welcome.png`: the welcome screen with glass tiles, recent folders and "What's new".
  - `/screenshots/markdown.png`: Markdown side-by-side preview with a centred logo, two SVG badges, a table and a code block (light theme).
  - `/screenshots/web.png`: the Web & npm panel in the Liquid Glass Night theme.
  - `/screenshots/license.png`: the license banner over an MIT license.
  - `/screenshots/palette.png`: the command palette (Dracula theme).
  - `/screenshots/settings.png`: the settings page (Nord theme).
  Because the real screenshots may not exist when you generate the page, the **theme showcase, Markdown demo, web-panel demo, agent demo and license explorer must be built as real interactive React components** (not screenshots), so the page is impressive on its own.

---

## 5. PAGE STRUCTURE, SECTION BY SECTION

Use a sticky, blurred top nav and one long scrolling page with anchor sections. Order: Nav, Hero, Under-the-hood strip, Feature grid, Themes, Markdown, Web, AI agent, License guide, Languages, Customize, Download, Honest limits, FAQ, Footer.

### 5.1 Nav
Left: logo + "LuminaIDE". Centre (desktop): Features, Themes, Markdown, Web, AI, Licenses, Download. Right: a "Search or jump to…" button showing the shortcut hint (Ctrl K, or the Command symbol K on Mac) that opens the demo command palette (see 6.5), the theme toggle, a GitHub icon button (opens `REPO_URL`), and a small primary "Download" button. On mobile: logo, a Download button and a hamburger opening a full-screen sheet with the same links. The nav gains a border and blur after 8px of scroll.

### 5.2 Hero
- Eyebrow badge (glass pill, small dot in green): "v0.2.0 · Free and open source".
- H1 (verbatim): **"The editor that stays out of your way."**
- Sub (verbatim): "LuminaIDE is a small, beautiful code editor with the things you reach for every day: 89 languages, a live Markdown preview, npm and Vite tools, and an AI agent panel. No clutter, no account, no telemetry."
- Buttons: primary **"Download for [Linux | macOS | Windows]"** (detect the OS from `navigator` on the client; fall back to "Download"; the label must not cause a hydration mismatch, so resolve it after mount) and secondary **"View on GitHub"** (outline, with the GitHub icon).
- Micro-copy under the buttons (muted, 13px): "Free and open source (MIT) · Linux, macOS and Windows · macOS and Windows builds are early previews".
- Visual: a large `ScreenshotFrame` of `/screenshots/editor.png` floating over the orbs, slightly tilted with the pointer. Around it, three small floating glass chips positioned at the frame's corners (each gently bobbing 6px, offset phases): "89 languages", "9 themes", "Liquid Glass".
- On mobile the screenshot sits below the buttons, full width, no tilt.

### 5.3 Under-the-hood strip
A quiet row of small glass chips with icons, label above: "Under the hood": "Rust engine", "C++ terminal", "C# + Avalonia interface", "MIT licensed", "No telemetry". Nothing else. No customer logos.

### 5.4 Feature grid (id="features")
Eyebrow "Everything you reach for". H2 (verbatim): **"Small on purpose. Complete where it counts."** A bento grid (12 columns desktop, 1 column mobile) with 8 tiles of different sizes, each with a lucide icon, a title, one or two sentences, and a small purely visual mini-illustration built with CSS (not images). Tiles (copy verbatim):
1. **Quick open and a command palette.** "Press Ctrl P to jump to any file by typing a few letters, or Ctrl Shift P to run any command by name." (Mini visual: a tiny fuzzy-search field with three result rows, the first highlighted.)
2. **89 languages, ready to go.** "From Rust and Python to Nix, Haskell and GLSL. A language is a single JSON file, so adding one takes minutes." (Mini visual: a few coloured file-type badges: RS, PY, TS, NX, HS, GL.)
3. **Themes that are actually designed.** "Nine of them, including two Liquid Glass themes. Make your own from Settings and watch it update as you save." (Mini visual: five overlapping colour swatches.)
4. **Run it with F5.** "Run the current file in the built-in terminal. HTML files open in your browser." (Mini visual: a play triangle and a terminal line printing "Hello".)
5. **Web tools built in.** "Your npm scripts, your Vite dev server and a starter for new sites, one click away."
6. **An AI agent, on your terms.** "Chat with Claude Code or Codex about your project. Read-only by default, and it runs the CLI you already have."
7. **It reads licenses for you.** "Open a LICENSE file and LuminaIDE tells you which license it is and what it allows."
8. **Settings you can read.** "One `settings.json`: comments allowed, applied the moment you save."
Each tile links (smooth scroll) to its deeper section where one exists.

### 5.5 Themes (id="themes")
Eyebrow "Themes". H2 (verbatim): **"Pick a mood. Or make your own."** Sub: "Nine themes ship with LuminaIDE. Try them here. The colours below are the real ones."
Interactive `ThemeShowcase`: a row of theme chips (name + four tiny colour dots for background, accent, keyword, string) that scroll horizontally on mobile, and below it a `FakeEditor` that re-themes instantly (CSS variables set on the editor element; 200ms colour transition). Details in 6.1. Under the editor, two lines: "Glass themes blur what is behind the window. It is a translucent theme plus your OS's own window blur, not Apple's Liquid Glass material." and a link "How to make your own theme" that opens a small dialog showing a 12-line JSON theme snippet (with copy button).

### 5.6 Markdown (id="markdown")
Eyebrow "Markdown". H2 (verbatim): **"Write on the left. Watch it come alive on the right."** Sub: "A live preview that follows your theme, with real images: local files, web images, SVG badges and animated GIFs."
Interactive `MarkdownDemo` (details in 6.2): a two-pane component on a glass card; left is an editable textarea with a monospaced style, right is the rendered preview. Below it a small list with check icons: "Tables and task lists", "Highlighted code blocks", "Local images, `<img>` tags, https images", "SVG badges", "Animated GIFs (click to pause)", "Remote images can be turned off in Settings".

### 5.7 Web (id="web")
Eyebrow "For the web". H2 (verbatim): **"npm and Vite, without leaving the editor."** Sub: "LuminaIDE reads your `package.json`, works out your package manager and frameworks, and runs scripts in the built-in terminal."
Interactive `WebPanelDemo` (details in 6.3): a mock sidebar panel (glass card, 340px wide, next to a mock terminal) showing the project, the badge row, the scripts list with play buttons, and a "Dev server running" card that appears after you click "dev".
Three small bullets beside it: "Detects npm, pnpm, yarn or bun from your lockfile.", "Spots the dev-server address and links to it.", "Starts a new Vite app or a plain static site." and a line in muted text: "Script names with shell characters are shown disabled, never run."

### 5.8 AI agent (id="ai")
Eyebrow "AI agent". H2 (verbatim): **"Ask your project a question."** Sub: "Claude Code or Codex, in a side panel, working inside your open folder. LuminaIDE runs the CLI you already have and stores no keys."
Interactive `AgentDemo` (details in 6.4): a mock agent panel with two provider chips, two mode chips (Plan · read-only selected by default, Edit files), a scripted conversation that plays when scrolled into view (respect reduced motion by showing it fully rendered), and an input.
Beside it, three plain-language points as a list with icons: "Read-only by default. Plan mode can read your files but not change them.", "Changed files reload for you. Tabs with unsaved edits are never overwritten.", "Your open file and selection can go along as context." Small print (muted): "Codex support follows its documented output format and is still being tested against the live CLI."

### 5.9 License guide (id="licenses")
Eyebrow "License guide". H2 (verbatim): **"Open a LICENSE file. Know what it means."** Sub: "LuminaIDE recognises 22 licenses and explains each one in plain language. Pick one to see what the banner says."
Interactive `LicenseExplorer` (details in 6.6). Footer of the section (muted): "A plain-language summary, not legal advice. The license text itself is what actually applies."

### 5.10 Languages (id="languages")
Eyebrow "Languages". H2 (verbatim): **"89 languages. One JSON file each."** Sub: "Highlighting is lexical: comments, strings, numbers, keywords and structure. Adding your own language is a small JSON file."
`LanguageWall`: a search input ("Filter 89 languages…") and a wrapped cloud of language pills that filter as you type (with a count, "Showing 12 of 89", and an empty state), plus two slow, opposite-direction marquee rows above it made of the same names (pause on hover, static under reduced motion). Full list in section 7.3. Beside the wall, a compact code card showing a 10-line language definition (JSON) for a tiny made-up example language (see 7.5), with a copy button.

### 5.11 Customize (id="customize")
Eyebrow "Make it yours". H2 (verbatim): **"Everything is a plain file."** Sub: "Settings, themes, languages and licenses are ordinary JSON you can read, diff and share."
`CustomizeTabs` (shadcn Tabs) with four tabs, each showing a code block (dark, copy button, filename badge on top): **settings.json**, **A theme**, **A language**, **Extension layout**. Content in 7.5. Under the tabs, a line: "Save a theme file and it updates live. Save `settings.json` and it applies instantly."

### 5.12 Download (id="download")
Eyebrow "Get LuminaIDE". H2 (verbatim): **"Free, and it stays that way."** Sub: "MIT licensed. Pick your platform."
Three glass cards (Linux, macOS, Windows). The card for the visitor's detected OS gets an accent ring and "Recommended for your system". Each card has: OS icon, name, a status badge (**Linux: "Tested"** green; **macOS: "Preview"** amber; **Windows: "Preview"** amber), the package name pattern in mono (`LuminaIDE-v0.2.0-linux-x64.tar.gz`, `LuminaIDE-v0.2.0-osx-arm64.zip` (Apple silicon) and `osx-x64.zip` (Intel), `LuminaIDE-v0.2.0-win-x64.zip`), a **Download** button linking to `RELEASES_URL`, and 2-3 lines of honest first-run notes:
- Linux: "Unpack and run `./LuminaIDE`. Also builds from source with a one-line installer."
- macOS: "Unzip and drag LuminaIDE.app to Applications. Not notarized yet: the first time, right-click and choose Open. This build has not been tried on a Mac by the author yet; please tell us how it goes."
- Windows: "Unzip and run LuminaIDE.exe. Not code-signed yet, so SmartScreen may warn. This build has not been tried on Windows by the author yet; please tell us how it goes."
Below the cards, a shadcn Tabs block **"Build from source"** with these commands and a copy button (verbatim):
```
# Linux / macOS  (needs the .NET 8 SDK, Rust, CMake, Ninja and a C++17 compiler)
git clone https://github.com/AnimatedGTVR/LuminaIDE.git
cd LuminaIDE
./install.sh          # builds and installs the `luminaide` command
luminaide .           # open the current folder

# Windows  (from a "Developer PowerShell for VS")
.\install.ps1
```
and a mono line: "Check your install with `luminaide --selftest`."

### 5.13 Honest limits (id="limits")
A calm, well-designed section (not hidden in a footer) with eyebrow "Honesty" and H2 **"What LuminaIDE isn't (yet)."** A two-column list with a subtle "not yet" pill on each item: "No language server: no autocomplete or go-to-definition", "No debugger", "No git panel", "No code-plugin marketplace (extensions are data files)", "No multi-cursor or minimap", "The terminal is line-based: no vim or htop", "The Markdown preview doesn't scroll in sync with the editor", "macOS and Windows builds are still early previews". Closing line (verbatim): "It's version 0.2.0. It's small on purpose, and it's growing carefully."

### 5.14 FAQ (id="faq")
shadcn Accordion, single-open, verbatim Q&A:
1. **Is LuminaIDE free?** "Yes. It's open source under the MIT license."
2. **Does it collect any data?** "No. There's no telemetry and no account. Two things can use the network, and both are yours to control: the AI agent panel runs your own Claude Code or Codex CLI, and Markdown previews can load https images, which you can switch off in Settings."
3. **Which AI does it use?** "None of its own. The agent panel runs the Claude Code or Codex command-line tool you already have installed, inside your open folder. It's read-only by default."
4. **Does it work on macOS and Windows?** "Builds exist and are checked by automated tests, but they're early previews: the author hasn't run them on a Mac or on Windows yet. Linux is the most tested. Try it and tell us what you find."
5. **Is Liquid Glass the real Apple material?** "No. It's a translucent theme combined with your operating system's own window blur. It looks great, but it isn't Apple's Liquid Glass."
6. **Does it have autocomplete, a debugger or git?** "Not yet. Highlighting is lexical (comments, strings, keywords and structure). There's no language server, debugger or git panel."
7. **Can I add my own language or theme?** "Yes. A language, theme or license is a small JSON file in your config folder. Theme changes update live when you save."
8. **How do I make it my own?** "Open Settings with Ctrl comma, or edit `settings.json`. Comments are allowed and changes apply the moment you save."
9. **Is the license guide legal advice?** "No. It's a plain-language summary to help you understand a license quickly. The license text is what actually applies."
10. **Where do I report a bug?** "On GitHub Issues." (link to `REPO_URL` + `/issues`)

### 5.15 Footer
Logo, "Light, fast and out of your way.", link columns (Product: Features, Themes, Download; Project: GitHub, Releases, Issues, Changelog (link `REPO_URL/blob/main/CHANGELOG.md`), License (MIT); Docs: Configuration, Themes, Languages, License guide, each linking to `REPO_URL/blob/main/docs/CONFIG.md`, `THEMES.md`, `LANGUAGES.md`, `LICENSES.md`), a theme toggle, and small print: "MIT licensed. Made by Animated." plus "Inter and JetBrains Mono are open-licensed fonts (SIL OFL)."

---

## 6. INTERACTIVE COMPONENTS: EXACT BEHAVIOUR

### 6.1 ThemeShowcase and FakeEditor
Use the `THEMES` data in section 7.1 (real colours from the app). Selecting a theme sets CSS variables on the `FakeEditor` root only: `--ed-bg`, `--ed-sidebar`, `--ed-border`, `--ed-fg`, `--ed-muted`, `--ed-accent`, `--ed-selection`, `--ed-keyword`, `--ed-fn`, `--ed-type`, `--ed-string`, `--ed-number`, `--ed-comment`. For themes with `glass: true`, render a colourful gradient backdrop behind the editor (violet to teal to coral) and give the editor `backdrop-filter: blur(18px)` so the translucency is visible; for others the backdrop is hidden. `FakeEditor` looks like the real app: a 46px activity bar with 4 simple line icons, a 200px sidebar file tree (folders tinted with the accent; files with small coloured type badges RS, TS, MD, JSON), a tab strip with two tabs (active tab has a 2px accent top line), a code area with line numbers and a highlighted current line, and a 24px status bar. The code sample (highlight it with hand-written spans using the theme variables; do not pull in a highlighter):
```rust
//! A tiny inventory example.
use std::collections::HashMap;

#[derive(Debug, Clone)]
pub struct Item {
    pub name: String,
    pub qty: u32,
}

fn main() {
    let mut stock: HashMap<String, Item> = HashMap::new();
    stock.insert("bolt".to_string(), Item { name: "bolt".into(), qty: 10 });
    // total quantity across all items
    let total: u32 = stock.values().map(|it| it.qty).sum();
    println!("{} items, {total} units", stock.len());
}
```
Colour rules: keywords (`use`, `pub`, `struct`, `fn`, `let`, `mut`) = keyword; types (`HashMap`, `String`, `Item`, `u32`) = type; function calls (`insert`, `to_string`, `values`, `map`, `sum`, `println!`, `len`) = fn; strings = string; numbers = number; comments = comment (italic off); the attribute `#[derive(...)]` = fn colour. Default the showcase to **Vanta Night**. Chip order: Vanta Night, Graphite, Daylight, Ember, Nord, Dracula, Solarized Light, Liquid Glass, Liquid Glass Night. Keyboard: arrow keys move between chips (roving tabindex), Enter/Space selects. Announce the change politely for screen readers.

### 6.2 MarkdownDemo
Two panes. Left: a `<textarea>` (JetBrains Mono, 14px) pre-filled with the text below. Right: a live renderer. Implement a small safe renderer yourself or use `react-markdown` + `remark-gfm`; **never use `dangerouslySetInnerHTML` on user input** (escape everything). Render: headings with a bottom hairline for h1/h2, bold, italic, strikethrough, inline code (a subtle chip), links (accent, underlined), task lists (checkbox glyphs), tables (bordered, alignment supported), block quotes (accent left bar) and fenced code blocks (dark surface, mono). Images render as `<img>` with `loading="lazy"` and a fixed max width. Pre-filled content:
```
# Release checklist

A quick guide for **shipping** a version — *no surprises*, ~~no rushed builds~~.

- [x] Update the [changelog](#)
- [ ] Tag and push

> Tip: `git tag v0.2.0` is all it takes to start the release workflow.

| Platform | Package | Signed |
|:---------|:-------:|-------:|
| Linux    | `.tar.gz` | n/a |
| macOS    | `.zip`    | ad-hoc |

```rust
fn main() { println!("shipping {}", "0.2.0"); }
```
```
Include a small "Reset" button. Add a row of 3 toggle chips under the preview that demonstrate image kinds by inserting a line into the textarea: "SVG badge" (inserts a shields-style badge drawn as an **inline SVG data URI** so no network is needed), "Local image" (inserts an image referencing a data-URI PNG of a small violet-to-pink gradient square), and "Animated GIF" (inserts a data-URI GIF you generate in code: a 2-frame 24x24 animation alternating violet and pink, ~600ms per frame, built as a base64 constant; if that is too fiddly, simulate it with a small CSS keyframe animation on an element and label it "animated"). Never fetch anything from the network.

### 6.3 WebPanelDemo
Mock, not functional. Left: the panel (glass card) with: project name "aurora-landing", "v1.4.2", a row of small tags (pnpm, Vite, React, TypeScript, Tailwind), then a "SCRIPTS" list: `dev` (vite), `build` (tsc && vite build), `preview` (vite preview --port 4173), `lint` (eslint src --ext ts), and one disabled, dimmed row named `deploy:prod; rm -rf /` with a tooltip: "Script names with shell characters are shown but never run." Each enabled row has a play triangle in the accent colour. Clicking `dev` types a command into the mock terminal on the right (typewriter effect, 30ms/char): `cd ~/aurora-landing && pnpm run dev`, then prints:
```
  VITE v5.4.0  ready in 312 ms

  ➜  Local:   http://localhost:5173/
```
and then a "Dev server running" card slides into the top of the panel with an "Open in browser" button (does nothing but pulse) and the same address; a small `⚡ localhost:5173` chip appears in a mock status bar. Clicking `build` prints three plausible-looking lines of build output ("vite v5.4.0 building for production…", "✓ 34 modules transformed.", "✓ built in 1.2s"). Below the panel, a "New web project" mini-form: a name input ("my-site"), template chips (Vanilla, React, Vue, Svelte) and two buttons "Create Vite app" and "New static website" (clicking prints the corresponding `npm create vite@latest my-site -- --template react` line in the mock terminal).

### 6.4 AgentDemo
Mock, scripted. A glass panel with: header "AGENT", provider chips **Claude Code** (selected) and **Codex**, mode chips **Plan · read-only** (selected) and **Edit files**, a one-line hint that changes with the mode ("The agent can read and search your files but cannot change anything." / "The agent may create and change files in this folder."). The conversation plays once on scroll-into-view: a user bubble "Why does `total` come out as 0?", then a tool line "● Read  src/main.rs", another "● Grep  qty", then a streamed assistant answer typed at ~40 characters per second: "The map is filled after `total` is computed, so `values()` is empty at that point. Move the `sum()` below the `insert` calls, or compute it inside the loop." Then the status line "Done". The input box and a "Send" button are decorative except that clicking a suggested prompt chip ("Explain this function", "Find the bug", "Write a test") replays a short canned reply. Toggling to "Edit files" changes the hint text and shows a small amber note: "Edit mode can change files in your open folder."

### 6.5 CommandPalette (site-wide)
Ctrl/Cmd+K (and the nav button) opens a shadcn `Command` dialog styled like the app's palette (glass, 640px). It lists these real LuminaIDE commands with their shortcuts, and choosing one does something simple on the site (scroll to a relevant section, toggle the theme, open the repo, or copy the install command). Items: Open Folder… (Ctrl O), New File (Ctrl N), Go to File… (Ctrl P), Save (Ctrl S), Run Current File (F5), Toggle Terminal (Ctrl `), Toggle AI Agent Panel (Ctrl Shift A), View: Web & npm (Ctrl Shift W), Settings (Ctrl ,), Color Theme… , License: Explain This File, Licenses: Browse the Guide…, Markdown: Change Preview Layout (Ctrl Shift V), Release Notes. Below the input, a muted hint: "This is a demo. The real palette lives in the app." Show `⌘` instead of `Ctrl` on macOS.

### 6.6 LicenseExplorer
Uses the `LICENSES` data in 7.4. A shadcn Tabs-like row of six pills (MIT, Apache-2.0, GPL-3.0, AGPL-3.0, MPL-2.0, Unlicense). The main area shows a faithful re-creation of the in-app banner: a header with a small document-check icon, the license name in semibold, two small tags (the id and the category), then the summary paragraph, then three labelled rows of chips: **You can** (green dot chips), **You must** (amber), **You can't count on** (red), then the notes as muted bullets and the muted line "Plain-language summary, not legal advice. The license text itself is what actually applies." Each chip has a tooltip with the plain-language explanation of that term (write a sensible one-sentence explanation for each label; they are provided in 7.4 as the label text itself, so add concise tooltips such as "Use commercially: you can use it in commercial projects and products, including ones you sell."). Switching license animates the chips (fade/stagger). Below the banner, show a dimmed, monospaced block imitating the top 6 lines of a generic license text (blurred at the bottom) to convey "this appears above the file". Include a small "Unrecognised license" variant selectable as a seventh pill named "Something else…" that shows the honest banner: title "Unrecognized license" and the text "This looks like a license file, but it isn't one LuminaIDE knows. Licenses differ a lot in what they allow, so read it carefully. If you're unsure what it means for you, ask the author or a lawyer."

---

## 7. DATA (USE EXACTLY THESE)

### 7.1 Themes (`lib/data/themes.ts`)
Real colours from the app (translucent glass colours are already converted to CSS `rgba()`):
```ts
const THEMES = [
  {
    "name": "Daylight",
    "kind": "light",
    "glass": false,
    "bg": "#fbfbfd",
    "sidebar": "#f0f1f6",
    "border": "#d9dce6",
    "fg": "#23252e",
    "muted": "#7a7f92",
    "accent": "#5b5bd6",
    "selection": "#cfd4f7",
    "keyword": "#b4531f",
    "fn": "#1a6fb5",
    "type": "#a12fa1",
    "string": "#2b8a4b",
    "number": "#b4531f",
    "comment": "#8a8fa3"
  },
  {
    "name": "Dracula",
    "kind": "dark",
    "glass": false,
    "bg": "#282a36",
    "sidebar": "#21222c",
    "border": "#343746",
    "fg": "#f8f8f2",
    "muted": "#6272a4",
    "accent": "#bd93f9",
    "selection": "#44475a",
    "keyword": "#ff79c6",
    "fn": "#50fa7b",
    "type": "#8be9fd",
    "string": "#f1fa8c",
    "number": "#bd93f9",
    "comment": "#6272a4"
  },
  {
    "name": "Ember",
    "kind": "dark",
    "glass": false,
    "bg": "#211b18",
    "sidebar": "#1b1614",
    "border": "#372d28",
    "fg": "#eadfd7",
    "muted": "#8f7f75",
    "accent": "#f08a3c",
    "selection": "#5a3a26",
    "keyword": "#f08a3c",
    "fn": "#ffd08a",
    "type": "#f2c15c",
    "string": "#a8c97a",
    "number": "#f2a35c",
    "comment": "#8f7f75"
  },
  {
    "name": "Graphite",
    "kind": "dark",
    "glass": false,
    "bg": "#1e1f22",
    "sidebar": "#191a1d",
    "border": "#2e3035",
    "fg": "#d4d7dd",
    "muted": "#7d8290",
    "accent": "#4d9bf0",
    "selection": "#2f4a6d",
    "keyword": "#c678dd",
    "fn": "#61afef",
    "type": "#e5c07b",
    "string": "#98c379",
    "number": "#d19a66",
    "comment": "#6b7280"
  },
  {
    "name": "Liquid Glass Night",
    "kind": "dark",
    "glass": true,
    "bg": "rgba(15, 17, 28, 0.77)",
    "sidebar": "rgba(23, 26, 43, 0.66)",
    "border": "rgba(255, 255, 255, 0.25)",
    "fg": "#eef1fa",
    "muted": "#9ba4be",
    "accent": "#64a8ff",
    "selection": "rgba(59, 130, 246, 0.33)",
    "keyword": "#ff7ab2",
    "fn": "#67b7a4",
    "type": "#5dd8ff",
    "string": "#fc6a5d",
    "number": "#d0bf69",
    "comment": "#7f8c98"
  },
  {
    "name": "Liquid Glass",
    "kind": "light",
    "glass": true,
    "bg": "rgba(244, 246, 251, 0.78)",
    "sidebar": "rgba(255, 255, 255, 0.65)",
    "border": "rgba(107, 122, 153, 0.23)",
    "fg": "#1b2333",
    "muted": "#66738f",
    "accent": "#0a84ff",
    "selection": "rgba(0, 122, 255, 0.27)",
    "keyword": "#ad3da4",
    "fn": "#326d74",
    "type": "#0b4f79",
    "string": "#d12f1b",
    "number": "#272ad8",
    "comment": "#707f8c"
  },
  {
    "name": "Nord",
    "kind": "dark",
    "glass": false,
    "bg": "#2e3440",
    "sidebar": "#2b303b",
    "border": "#3b4252",
    "fg": "#d8dee9",
    "muted": "#7b88a1",
    "accent": "#88c0d0",
    "selection": "#434c5e",
    "keyword": "#81a1c1",
    "fn": "#88c0d0",
    "type": "#8fbcbb",
    "string": "#a3be8c",
    "number": "#b48ead",
    "comment": "#616e88"
  },
  {
    "name": "Solarized Light",
    "kind": "light",
    "glass": false,
    "bg": "#fdf6e3",
    "sidebar": "#eee8d5",
    "border": "#d9d2bc",
    "fg": "#586e75",
    "muted": "#93a1a1",
    "accent": "#268bd2",
    "selection": "#dde6e0",
    "keyword": "#859900",
    "fn": "#268bd2",
    "type": "#b58900",
    "string": "#2aa198",
    "number": "#d33682",
    "comment": "#93a1a1"
  },
  {
    "name": "Vanta Night",
    "kind": "dark",
    "glass": false,
    "bg": "#1c1a4b",
    "sidebar": "#16143b",
    "border": "#2b2966",
    "fg": "#e7e7eb",
    "muted": "#82819c",
    "accent": "#8b7cf6",
    "selection": "#3b3888",
    "keyword": "#ffb07c",
    "fn": "#ffc9a8",
    "type": "#fea4e8",
    "string": "#92cfa5",
    "number": "#f1b595",
    "comment": "#82819c"
  }
] as const;
```

### 7.2 Shortcuts (for tooltips, the palette and a compact "Keyboard" strip if you add one)
Ctrl+P quick open · Ctrl+Shift+P command palette · Ctrl+O open folder · Ctrl+N new file · Ctrl+S save · Ctrl+W close tab · Ctrl+F find · F5 run · Shift+F5 stop · Ctrl+B toggle sidebar · Ctrl+` terminal · Ctrl+Shift+A AI agent panel · Ctrl+Shift+W Web & npm · Ctrl+Shift+E explorer · Ctrl+Shift+F search · Ctrl+Shift+V Markdown layout · Ctrl+, settings · Ctrl + / − / 0 zoom. (On macOS, read Ctrl as the Command key.)

### 7.3 Languages (`lib/data/languages.ts`), all 89
```ts
export const LANGUAGES = ["Ada", "Assembly", "Astro", "AWK", "Batch", "C", "C#", "C++", "Clojure", "CMake", "CoffeeScript", "Common Lisp / Emacs Lisp", "Crystal", "CSS", "CUDA", "D", "Dart", "Diff / Patch", "Dockerfile", "Dotenv", "Elixir", "Elm", "Erlang", "F#", "Fortran", "GDScript", "Gleam", "GLSL", "Go", "Godot Resource", "GraphQL", "Groovy", "Haskell", "HLSL", "HTML", "Ignore file", "INI / Config", "Java", "Java Properties", "JavaScript", "JSON", "Julia", "Kotlin", "LaTeX", "Lean", "Less", "Lua", "Makefile", "Nginx", "Nim", "Nix", "Objective-C", "OCaml", "Odin", "Pascal", "Perl", "PHP", "PowerShell", "Prolog", "Protocol Buffers", "PureScript", "Python", "R", "Racket", "Ruby", "Rust", "Scala", "Scheme", "SCSS / Sass", "Shell", "Solidity", "SQL", "Svelte", "Swift", "Tcl", "Terraform / HCL", "TOML", "TypeScript", "V", "Vanta Script", "Verilog / SystemVerilog", "VHDL", "Vim Script", "Visual Basic", "Vue", "WGSL", "XML", "YAML", "Zig"] as const;
```

### 7.4 Licenses (`lib/data/licenses.ts`)
```ts
const LICENSES = [
  {
    "id": "MIT",
    "name": "MIT License",
    "category": "Permissive",
    "summary": "A short, permissive license. You can do almost anything with the code, including using it in closed-source commercial products, as long as you keep the copyright notice and license text with it.",
    "canDo": [
      "Use commercially",
      "Modify",
      "Distribute",
      "Use privately"
    ],
    "mustDo": [
      "Keep the copyright notice"
    ],
    "cantCountOn": [
      "No liability",
      "No warranty"
    ],
    "notes": [
      "One of the most widely used licenses, and compatible with almost every other license."
    ]
  },
  {
    "id": "Apache-2.0",
    "name": "Apache License 2.0",
    "category": "Permissive",
    "summary": "A permissive license like MIT, plus an explicit patent grant: contributors license their patents to you, and that grant ends if you sue over patents in the software. You must keep notices and say what you changed.",
    "canDo": [
      "Use commercially",
      "Modify",
      "Distribute",
      "Patent grant",
      "Use privately"
    ],
    "mustDo": [
      "Keep the copyright notice",
      "State your changes"
    ],
    "cantCountOn": [
      "No trademark rights",
      "No liability",
      "No warranty"
    ],
    "notes": [
      "If the project ships a NOTICE file, you must pass it on with your distribution.",
      "Generally considered incompatible with GPL-2.0-only, but compatible with GPL-3.0."
    ]
  },
  {
    "id": "GPL-3.0",
    "name": "GNU General Public License v3.0",
    "category": "Strong copyleft",
    "summary": "Strong copyleft: you can use, change and sell the software, but if you distribute it (or something built on it) you must share the complete source under the same license. Adds an explicit patent grant.",
    "canDo": [
      "Use commercially",
      "Modify",
      "Distribute",
      "Patent grant",
      "Use privately"
    ],
    "mustDo": [
      "Share the source",
      "Keep the copyright notice",
      "State your changes",
      "Same license"
    ],
    "cantCountOn": [
      "No liability",
      "No warranty"
    ],
    "notes": []
  },
  {
    "id": "AGPL-3.0",
    "name": "GNU Affero General Public License v3.0",
    "category": "Network copyleft",
    "summary": "Like the GPL-3.0, with one addition: if you run a modified version as a network service, the people using it over the network must be offered the source code too.",
    "canDo": [
      "Use commercially",
      "Modify",
      "Distribute",
      "Patent grant",
      "Use privately"
    ],
    "mustDo": [
      "Share the source",
      "Keep the copyright notice",
      "State your changes",
      "Share source for network use",
      "Same license"
    ],
    "cantCountOn": [
      "No liability",
      "No warranty"
    ],
    "notes": [
      "Many companies avoid AGPL code in hosted products because of the network clause."
    ]
  },
  {
    "id": "MPL-2.0",
    "name": "Mozilla Public License 2.0",
    "category": "Weak copyleft (per file)",
    "summary": "Copyleft at the file level: if you change an MPL-licensed file you must share your changes to that file under the MPL, but you can combine it with code under other licenses, even proprietary ones, in a larger work.",
    "canDo": [
      "Use commercially",
      "Distribute",
      "Modify",
      "Patent grant",
      "Use privately"
    ],
    "mustDo": [
      "Share the source",
      "Keep the copyright notice",
      "Same license (per file)"
    ],
    "cantCountOn": [
      "No liability",
      "No trademark rights",
      "No warranty"
    ],
    "notes": []
  },
  {
    "id": "Unlicense",
    "name": "The Unlicense",
    "category": "Public domain",
    "summary": "Dedicates the work to the public domain: anyone may copy, change, sell or share it for any purpose, with no conditions.",
    "canDo": [
      "Use commercially",
      "Modify",
      "Distribute",
      "Use privately"
    ],
    "mustDo": [],
    "cantCountOn": [
      "No liability",
      "No warranty"
    ],
    "notes": []
  }
] as const;
```

### 7.5 Customize snippets
**settings.json**
```json
// LuminaIDE settings. Edit and save this file: changes apply immediately.
{
  "theme": "Liquid Glass Night",
  "editorFontFamily": "JetBrains Mono",
  "editorFontSize": 14,
  "tabSize": 2,
  "autoSave": "afterDelay",
  "trimTrailingWhitespace": true,
  "markdownMode": "split",
  "markdownRemoteImages": true,
  "agent": "claude",
  "agentMode": "read"
}
```
**A theme** (`~/.config/luminaide/themes/my-theme.json`; colours are `#RRGGBB`, or `#AARRGGBB` with the alpha first)
```json
{
  "name": "Midnight Mint",
  "type": "dark",
  "ui": {
    "background": "#0f1a1a", "sidebar": "#0b1414", "foreground": "#e6f2ef",
    "muted": "#7a948e", "accent": "#5eead4", "selection": "#1f4a44"
  },
  "syntax": {
    "keyword": "#5eead4", "string": "#f9c97a", "function": "#a5b4fc",
    "type": "#f0abfc", "number": "#fca5a5", "comment": "#5b736e"
  }
}
```
**A language** (a made-up, tiny example: a language called "Glow")
```json
{
  "id": "glow",
  "name": "Glow",
  "extensions": [".glow"],
  "lineComments": ["#"],
  "strings": ["\""],
  "keywords": ["let", "fn", "shine"],
  "control": ["if", "else", "return"],
  "types": ["int", "text"],
  "constants": ["true", "false"],
  "definitionKeywords": ["fn"],
  "run": "glow run {file}"
}
```
**Extension layout**
```
~/.config/luminaide/
  settings.json
  themes/
    my-theme.json
  extensions/
    my-extension/
      extension.json
      languages/glow.json
      licenses/my-license.json
```

---

## 8. SEO, METADATA, ACCESSIBILITY, PERFORMANCE

- **Metadata**: title "LuminaIDE — a small, beautiful code editor"; description "LuminaIDE is a free, open-source code editor for Linux, macOS and Windows: 89 languages, live Markdown preview, npm and Vite tools, an AI agent panel and themes that are actually designed."; canonical, Open Graph and Twitter card (an `opengraph-image.tsx` rendering the logo, the H1 and the orbs at 1200x630); `theme-color` per scheme; JSON-LD `SoftwareApplication` (name, operatingSystem "Linux, macOS, Windows", applicationCategory "DeveloperApplication", offers price 0, license MIT URL, softwareVersion "0.2.0"). No aggregateRating, no reviews.
- **Accessibility (WCAG 2.2 AA)**: semantic landmarks (`header`, `nav`, `main`, `section` with `aria-labelledby`, `footer`), a skip-to-content link, visible focus rings (2px accent, 2px offset), full keyboard operation for every interactive demo, roving tabindex for chip groups, `aria-live="polite"` where content changes (theme chips, license switcher, mock terminal), colour contrast checked for both schemes (muted text must stay above 4.5:1; do not put muted text on glass without a solid enough surface), decorative elements `aria-hidden`, alt text on every image, `prefers-reduced-motion` and `prefers-color-scheme` honoured, tap targets at least 44px on touch.
- **Performance**: no layout shift (reserve space for every demo and image), lazy-load below-the-fold images and heavy demos (`next/dynamic`), one font request per family via `next/font`, no client JS for static sections (use server components; mark only the interactive ones `"use client"`), CSS-only orbs, no large libraries beyond those listed. Lighthouse targets: Performance 95+, Accessibility 100, Best Practices 100, SEO 100.
- **Responsive**: design mobile-first; check 360, 390, 768, 1024, 1280 and 1536px. On mobile the bento grid becomes a single column, demos become vertically stacked, chip rows scroll horizontally with snap, the nav collapses, and the hero screenshot is full width.
- **i18n/units**: English only. Use a real typographic apostrophe and en/em dashes in copy. `<html lang="en">`.

---

## 9. THINGS YOU MUST NOT DO

- No invented statistics, benchmarks, "trusted by" logos, testimonials, reviews, star counts, download counts, roadmap dates or prices.
- No claims of being faster, lighter or better than any named product. Never name competitors in a negative way.
- No claims of features listed under "does NOT do (yet)". No claim that macOS or Windows are fully tested.
- Do not call Liquid Glass Apple's. Do not call the AI panel "built-in AI": it runs the user's own Claude Code / Codex CLI.
- No third-party scripts, analytics, trackers, cookie banners, chat widgets, newsletter forms or fake email capture.
- No stock photos. No emoji in headings or buttons. No lorem ipsum anywhere. No placeholder text other than `REPO_URL` and the screenshot files.
- Do not use `dangerouslySetInnerHTML` with anything user-typed. Do not fetch from the network at runtime.
- Do not use generic "AI startup" boilerplate ("Supercharge your workflow", "Unlock the power of…").

---

## 10. DELIVERABLE AND ACCEPTANCE CHECKLIST

Generate the complete working project: layout, page, all components, data files, styles, metadata files and a short README explaining how to set `REPO_URL`, where to drop the screenshots, and how to deploy on Vercel. When done, verify each item:

1. The page reads top to bottom as one coherent, beautiful story, in the app's deep-indigo glass style, with drifting orbs and no layout shift.
2. Every section from 5.1 to 5.15 exists with the verbatim headings and copy.
3. All six interactive demos work with the mouse and the keyboard: theme showcase (9 themes, glass variants blur), Markdown demo, web panel demo, agent demo, command palette (Ctrl/Cmd K), license explorer (6 licenses + "Something else…").
4. The language wall filters 89 names and shows a count and an empty state.
5. The download section detects the OS, highlights it, shows Linux as "Tested" and macOS/Windows as "Preview" with the honest first-run notes, and every link comes from `REPO_URL` / `RELEASES_URL` in `lib/site.ts`.
6. The "What LuminaIDE isn't (yet)" section is present and prominent.
7. Light and dark schemes both look intentional; the theme toggle persists and has no flash.
8. `prefers-reduced-motion` turns off every animation; the page is fully usable without it.
9. Lighthouse-style checks: no console errors or hydration warnings, correct heading order (one h1), all images have alt text, focus is always visible.
10. There is no text anywhere that makes a claim listed in section 9.

Build it in one pass, then review your own output against this checklist and fix anything that fails before you finish.
