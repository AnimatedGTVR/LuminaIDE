# Languages and extensions

LuminaIDE highlights **89 languages** out of the box, and adding one is a single small JSON file.

## Built-in languages

### Config & Data Formats

| Language | Extensions | F5 runs it |
|---|---|---|
| CMake | `.cmake`, `CMakeLists.txt` |  |
| Diff / Patch | `.diff`, `.patch` |  |
| Dockerfile | `.dockerfile`, `Dockerfile`, `Containerfile` |  |
| Dotenv | `.env`, `.env`, `.env.local`, `.env.development` |  |
| Ignore file | `.gitignore`, `.dockerignore`, `.npmignore`, `.prettierignore`, `.eslintignore`, `.gitignore`, `.dockerignore`, `.npmignore` |  |
| INI / Config | `.ini`, `.cfg`, `.conf`, `.editorconfig`, `.gitconfig`, `.desktop` …, `.editorconfig`, `.gitconfig` |  |
| Java Properties | `.properties` |  |
| LaTeX | `.tex`, `.sty`, `.cls`, `.bib`, `.ltx` |  |
| Makefile | `.mk`, `.mak`, `Makefile`, `makefile`, `GNUmakefile` | `make` |
| Nginx | `.nginx`, `nginx.conf` |  |
| Protocol Buffers | `.proto` |  |
| Terraform / HCL | `.tf`, `.tfvars`, `.hcl` |  |

### Core Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| C | `.c`, `.h` | `cc {file} -o /tmp/lumina-run-{name}` |
| C# | `.cs` | `dotnet run` |
| C++ | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx` | `c++ {file} -o /tmp/lumina-run-{name}` |
| Go | `.go` | `go run {file}` |
| Java | `.java` | `java {file}` |
| JavaScript | `.js`, `.mjs`, `.cjs`, `.jsx` | `node {file}` |
| JSON | `.json`, `.jsonc`, `.geojson` |  |
| Kotlin | `.kt`, `.kts` |  |
| Lua | `.lua` | `lua {file}` |
| Python | `.py`, `.pyw`, `.pyi` | `python3 {file}` |
| Rust | `.rs` | `cargo run` |
| Shell | `.sh`, `.bash`, `.zsh`, `.bashrc`, `.zshrc`, `.profile` | `bash {file}` |
| SQL | `.sql` |  |
| TOML | `.toml` |  |
| TypeScript | `.ts`, `.tsx`, `.mts`, `.cts` | `npx --yes tsx {file}` |
| YAML | `.yml`, `.yaml` |  |
| Zig | `.zig` | `zig run {file}` |

### Functional & Lisp Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| Clojure | `.clj`, `.cljs`, `.cljc`, `.edn`, `.bb` | `clojure -M {file}` |
| Common Lisp / Emacs Lisp | `.lisp`, `.lsp`, `.cl`, `.asd`, `.el` | `sbcl --script {file}` |
| Elixir | `.ex`, `.exs`, `.heex`, `.eex` | `elixir {file}` |
| Elm | `.elm` |  |
| Erlang | `.erl`, `.hrl`, `.escript` | `escript {file}` |
| Gleam | `.gleam` | `gleam run` |
| Haskell | `.hs`, `.lhs`, `.hsc` | `runghc {file}` |
| Lean | `.lean` | `lean --run {file}` |
| OCaml | `.ml`, `.mli`, `.mll`, `.mly` | `ocaml {file}` |
| Prolog | `.pro`, `.prolog`, `.plg` | `swipl {file}` |
| PureScript | `.purs` |  |
| Racket | `.rkt`, `.rktl`, `.scrbl` | `racket {file}` |
| Scheme | `.scm`, `.ss`, `.sld`, `.sls` | `guile {file}` |

### JVM & .NET Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| F# | `.fs`, `.fsi`, `.fsx` | `dotnet fsi {file}` |
| Groovy | `.groovy`, `.gradle`, `.gvy` | `groovy {file}` |
| Scala | `.scala`, `.sc`, `.sbt` |  |
| Visual Basic | `.vb`, `.vbs`, `.bas` |  |

### Scripting Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| AWK | `.awk` | `awk -f {file}` |
| Batch | `.bat`, `.cmd` |  |
| Dart | `.dart` | `dart run {file}` |
| Julia | `.jl` | `julia {file}` |
| Perl | `.pl`, `.pm`, `.t`, `.pod` | `perl {file}` |
| PowerShell | `.ps1`, `.psm1`, `.psd1` | `pwsh {file}` |
| R | `.r` | `Rscript {file}` |
| Ruby | `.rb`, `.rake`, `.gemspec`, `.ru`, `.rbw`, `Rakefile`, `Gemfile`, `Guardfile` | `ruby {file}` |
| Tcl | `.tcl`, `.tk` | `tclsh {file}` |
| Vim Script | `.vim`, `.vimrc`, `vimrc`, `_vimrc` |  |

### Systems & Graphics Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| Ada | `.adb`, `.ads`, `.ada` |  |
| Assembly | `.asm`, `.s`, `.nasm`, `.masm`, `.inc` |  |
| Crystal | `.cr` | `crystal run {file}` |
| CUDA | `.cu`, `.cuh` | `nvcc {file} -o /tmp/lumina-run-{name}` |
| D | `.d`, `.di` | `rdmd {file}` |
| Fortran | `.f90`, `.f95`, `.f03`, `.f08`, `.f`, `.for` … | `gfortran {file} -o /tmp/lumina-run-{name}` |
| GDScript | `.gd` |  |
| GLSL | `.glsl`, `.vert`, `.frag`, `.geom`, `.comp`, `.tesc` … |  |
| Godot Resource | `.tscn`, `.tres`, `.godot`, `.import`, `.gdextension` |  |
| HLSL | `.hlsl`, `.hlsli`, `.fx`, `.cginc`, `.shader`, `.compute` |  |
| Nim | `.nim`, `.nims`, `.nimble` | `nim r {file}` |
| Nix | `.nix` | `nix eval --file {file}` |
| Objective-C | `.m`, `.mm` |  |
| Odin | `.odin` | `odin run {file} -file` |
| Pascal | `.pas`, `.pp`, `.dpr`, `.lpr` |  |
| Solidity | `.sol` |  |
| Swift | `.swift` | `swift {file}` |
| V | `.vv` | `v run {file}` |
| Verilog / SystemVerilog | `.v`, `.sv`, `.svh`, `.vh` |  |
| VHDL | `.vhd`, `.vhdl` |  |
| WGSL | `.wgsl` |  |

### Vanta Script

| Language | Extensions | F5 runs it |
|---|---|---|
| Vanta Script | `.vanta` | `vanta run {file}` |

### Web Languages

| Language | Extensions | F5 runs it |
|---|---|---|
| Astro | `.astro` |  |
| CoffeeScript | `.coffee`, `.cson` | `coffee {file}` |
| CSS | `.css` |  |
| GraphQL | `.graphql`, `.gql` |  |
| HTML | `.html`, `.htm`, `.xhtml`, `.jsp`, `.ejs` | `xdg-open {file}` |
| Less | `.less` |  |
| PHP | `.php`, `.phtml`, `.php3`, `.php4`, `.php5`, `.phps` | `php {file}` |
| SCSS / Sass | `.scss`, `.sass` |  |
| Svelte | `.svelte` |  |
| Vue | `.vue` |  |
| XML | `.xml`, `.xsd`, `.xsl`, `.xslt`, `.svg`, `.plist` … |  |

## Adding a language

An **extension** is a folder containing `extension.json`. Put it in your extensions folder (Settings → *Open extensions folder*)
and choose *Reload extensions & themes*. Extensions in your folder override built-in ones with the same language `id` or theme `name`.

    my-extension/
      extension.json
      languages/mylang.json
      themes/mytheme.json

    {
      "name": "my-extension", "displayName": "My Extension", "version": "1.0.0",
      "description": "…", "languages": ["languages/mylang.json"], "themes": ["themes/mytheme.json"],
      "licenses": ["licenses/mylicense.json"]
    }

An extension can also add **licenses** to the [license guide](LICENSES.md).

Themes can also be dropped straight into the `themes` folder without a manifest (see [THEMES.md](THEMES.md)).

## Language file reference

Highlighting is **lexical**: the tokenizer recognises comments, strings, numbers, keywords and a few structural patterns.
Every field is optional except `id`, `name` and a way to match files.

| Field | Meaning |
|---|---|
| `id`, `name` | Unique id (also the file name) and the display name. |
| `extensions` | File endings including the dot (`".rs"`, `".d.ts"`). Case-insensitive. The longest match wins. |
| `filenames` | Exact file names (`"Makefile"`, `".bashrc"`). |
| `lineComments`, `blockComments` | `["//"]` and `[["/*", "*/"]]`. |
| `strings` | Single-line string delimiters, e.g. `["\"", "'"]`. |
| `multilineStrings` | Delimiters whose strings may span lines (`"\"\"\""`, `` "`" ``, `"''"`). |
| `keywords` | Declaration keywords → *keyword* colour (`let`, `func`, `class`). |
| `control` | Flow keywords → *control* colour (`if`, `return`). |
| `types`, `constants` | Built-in types and constants (`int`, `true`). |
| `definitionKeywords` | The name after these is a *definition* (`func` → the function's name). |
| `attributePrefix` | `"@"` for decorators/directives, `"#["` for Rust attributes, `"\\"` for LaTeX commands. |
| `preprocessor` | Colour `#include`-style lines (C, C++, GLSL). |
| `pascalCaseTypes` | Default `true`: `PascalCase` words are types and `SCREAMING_CASE` are constants. |
| `markup` | HTML/XML-style: colour tag names and attributes; text between tags is left alone. |
| `dashIdents` | Allow `-` inside names (CSS properties, Lisp). |
| `hexColors` | Treat `#fff` / `#a1b2c3` as numbers (CSS). |
| `keyChars` | Characters that make the word before them a property key: `":"` (YAML, CSS), `"="` (INI, Nix, TOML). |
| `stringKeys` | A string followed by `:` is a key (JSON). |
| `caseInsensitive` | Keywords, types and constants ignore case (SQL, Pascal, Fortran). |
| `prefixCalls` | A word right after `(` is a call (Lisp family). |
| `linePrefixes` | Whole-line rules, e.g. `[["+", "string"], ["-", "control"]]` (diff). Kinds: keyword, control, type, function, definition, property, string, number, comment, constant, attribute. |
| `run` | What **F5** runs, e.g. `"python3 {file}"`. Placeholders `{file}`, `{dir}`, `{name}` are shell-quoted for you. |
| `runWindows`, `runMac` | Override `run` on that OS; an empty string switches running off there. |

The Rust tokenizer lives in `core/src/syntax.rs`; `core/tests/languages.rs` checks every shipped language file
(unique ids, no two languages claiming the same extension, and no panics or bad offsets on hostile input).
