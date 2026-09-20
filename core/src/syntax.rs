//! Data-driven syntax highlighting. A language is a small JSON document (see
//! `extensions/*/languages/*.json`); the tokenizer turns text into flat
//! `[start, len, kind, start, len, kind, ...]` triples where offsets are UTF-16
//! code units, so the C# side can use them as document offsets directly.

use std::collections::HashSet;
use std::ffi::c_char;
use std::path::Path;
use std::ptr;
use std::sync::{Arc, RwLock};

use serde::Deserialize;

use crate::{arg, out};

// Token kinds. Keep in sync with `SyntaxKinds` in app/Theme.cs.
pub const KEYWORD: u32 = 0;
pub const CONTROL: u32 = 1;
pub const TYPE: u32 = 2;
pub const FUNCTION: u32 = 3;
pub const DEFINITION: u32 = 4;
pub const PROPERTY: u32 = 5;
pub const STRING: u32 = 6;
pub const NUMBER: u32 = 7;
pub const COMMENT: u32 = 8;
pub const CONSTANT: u32 = 9;
pub const ATTRIBUTE: u32 = 10;

#[derive(Deserialize, Default, Clone)]
#[serde(rename_all = "camelCase", default)]
pub struct Language {
    pub id: String,
    pub name: String,
    /// File endings including the dot, e.g. ".rs" or ".d.ts".
    pub extensions: Vec<String>,
    /// Exact file names, e.g. "Makefile".
    pub filenames: Vec<String>,
    pub line_comments: Vec<String>,
    pub block_comments: Vec<[String; 2]>,
    /// Single-line string delimiters.
    pub strings: Vec<String>,
    /// Delimiters whose strings may span lines.
    pub multiline_strings: Vec<String>,
    /// Declaration-style keywords (`let`, `func`, `struct`...).
    pub keywords: Vec<String>,
    /// Flow-control keywords (`return`, `break`...).
    pub control: Vec<String>,
    pub types: Vec<String>,
    pub constants: Vec<String>,
    /// Keywords after which the next identifier is a definition (`func`, `class`...).
    pub definition_keywords: Vec<String>,
    /// `@` for decorators/directives, `#[` for Rust attributes.
    pub attribute_prefix: String,
    /// Colour `#include`-style lines at the start of a line.
    pub preprocessor: bool,
    /// Treat `PascalCase` words as types and `SCREAMING_CASE` as constants (default true).
    pub pascal_case_types: Option<bool>,
    /// HTML/XML-like: colour tag names and attributes, leave text between tags alone.
    pub markup: bool,
    /// Allow `-` inside identifiers (CSS properties, Lisp names).
    pub dash_idents: bool,
    /// `#rgb` / `#rrggbb` / `#rrggbbaa` are numbers (CSS).
    pub hex_colors: bool,
    /// Characters that make the identifier before them a property key: ":" for YAML/CSS, "=" for INI/Nix.
    pub key_chars: String,
    /// A string directly followed by `:` is a key (JSON).
    pub string_keys: bool,
    /// Keywords, types and constants ignore case (SQL, Pascal, Fortran...).
    pub case_insensitive: bool,
    /// An identifier right after `(` is a call (Lisp family).
    pub prefix_calls: bool,
    /// Whole lines starting with a prefix get a token kind, e.g. `[["+", "string"], ["-", "control"]]` (diff).
    pub line_prefixes: Vec<[String; 2]>,
}

fn kind_by_name(name: &str) -> Option<u32> {
    Some(match name {
        "keyword" => KEYWORD,
        "control" => CONTROL,
        "type" => TYPE,
        "function" => FUNCTION,
        "definition" => DEFINITION,
        "property" => PROPERTY,
        "string" => STRING,
        "number" => NUMBER,
        "comment" => COMMENT,
        "constant" => CONSTANT,
        "attribute" => ATTRIBUTE,
        _ => return None,
    })
}

pub struct Compiled {
    pub lang: Language,
    keywords: HashSet<String>,
    control: HashSet<String>,
    types: HashSet<String>,
    constants: HashSet<String>,
    defs: HashSet<String>,
    lines: Vec<Vec<char>>,
    blocks: Vec<(Vec<char>, Vec<char>)>,
    delims: Vec<(Vec<char>, bool)>,
    attr: Vec<char>,
    pascal: bool,
    ci: bool,
    key_chars: Vec<char>,
    line_pfx: Vec<(Vec<char>, u32)>,
}

fn set(v: &[String]) -> HashSet<String> {
    v.iter().cloned().collect()
}

fn chars(s: &str) -> Vec<char> {
    s.chars().collect()
}

impl Compiled {
    pub fn new(lang: Language) -> Self {
        let mut delims: Vec<(Vec<char>, bool)> = lang
            .multiline_strings
            .iter()
            .map(|s| (chars(s), true))
            .chain(lang.strings.iter().map(|s| (chars(s), false)))
            .filter(|(d, _)| !d.is_empty())
            .collect();
        // Longest opener first so `"""` wins over `"`.
        delims.sort_by_key(|(d, _)| std::cmp::Reverse(d.len()));

        let ci = lang.case_insensitive;
        let words = |v: &[String]| -> HashSet<String> {
            if ci {
                v.iter().map(|s| s.to_lowercase()).collect()
            } else {
                set(v)
            }
        };
        let mut line_pfx: Vec<(Vec<char>, u32)> = lang
            .line_prefixes
            .iter()
            .filter_map(|[p, k]| kind_by_name(k).map(|k| (chars(p), k)))
            .filter(|(p, _)| !p.is_empty())
            .collect();
        line_pfx.sort_by_key(|(p, _)| std::cmp::Reverse(p.len()));

        Compiled {
            keywords: words(&lang.keywords),
            control: words(&lang.control),
            types: words(&lang.types),
            constants: words(&lang.constants),
            defs: words(&lang.definition_keywords),
            ci,
            key_chars: lang.key_chars.chars().collect(),
            line_pfx,
            lines: lang.line_comments.iter().map(|s| chars(s)).filter(|s| !s.is_empty()).collect(),
            blocks: lang
                .block_comments
                .iter()
                .map(|[a, b]| (chars(a), chars(b)))
                .filter(|(a, b)| !a.is_empty() && !b.is_empty())
                .collect(),
            delims,
            attr: chars(&lang.attribute_prefix),
            pascal: lang.pascal_case_types.unwrap_or(true),
            lang,
        }
    }
}

fn starts(text: &[char], at: usize, pat: &[char]) -> bool {
    text.len() >= at + pat.len() && &text[at..at + pat.len()] == pat
}

fn ident_start(c: char) -> bool {
    c.is_alphabetic() || c == '_' || c == '$'
}

fn ident_part(c: char) -> bool {
    c.is_alphanumeric() || c == '_' || c == '$'
}

fn is_pascal(w: &str) -> bool {
    let mut it = w.chars();
    it.next().is_some_and(char::is_uppercase) && w.chars().count() >= 2 && w.chars().any(char::is_lowercase)
}

fn is_screaming(w: &str) -> bool {
    w.chars().count() >= 2
        && w.chars().next().is_some_and(char::is_uppercase)
        && w.chars().all(|c| c.is_uppercase() || c.is_ascii_digit() || c == '_')
}

fn emit(out: &mut Vec<u32>, off: &[u32], s: usize, e: usize, kind: u32) {
    out.extend_from_slice(&[off[s], off[e] - off[s], kind]);
}

pub fn tokenize(c: &Compiled, text: &str) -> Vec<u32> {
    let ch: Vec<char> = text.chars().collect();
    let n = ch.len();

    let mut off = Vec::with_capacity(n + 1);
    let mut o = 0u32;
    for &x in &ch {
        off.push(o);
        o += x.len_utf16() as u32;
    }
    off.push(o);

    let mut out = Vec::new();
    let mut i = 0;
    let mut after_def = false;
    let mut line_start = true;
    let mut in_tag = false; // markup languages: between `<name` and `>`

    while i < n {
        let c0 = ch[i];
        if c0 == '\n' {
            line_start = true;
            i += 1;
            continue;
        }
        if c0.is_whitespace() {
            i += 1;
            continue;
        }
        let at_line_start = std::mem::replace(&mut line_start, false);

        // Whole-line rules (diff hunks, log levels...).
        if at_line_start {
            if let Some((_, kind)) = c.line_pfx.iter().find(|(p, _)| starts(&ch, i, p)) {
                let mut j = i;
                while j < n && ch[j] != '\n' {
                    j += 1;
                }
                emit(&mut out, &off, i, j, *kind);
                i = j;
                continue;
            }
        }

        // Comments (they never interrupt a `func <name>` pair).
        if let Some((open, close)) = c.blocks.iter().find(|(o, _)| starts(&ch, i, o)) {
            let mut j = i + open.len();
            while j < n && !starts(&ch, j, close) {
                j += 1;
            }
            let end = if j < n { j + close.len() } else { n };
            emit(&mut out, &off, i, end, COMMENT);
            i = end;
            continue;
        }
        if c.lines.iter().any(|l| starts(&ch, i, l)) {
            let mut j = i;
            while j < n && ch[j] != '\n' {
                j += 1;
            }
            emit(&mut out, &off, i, j, COMMENT);
            i = j;
            continue;
        }

        // HTML/XML-like languages: only tags matter outside of them.
        if c.lang.markup {
            if !in_tag {
                if c0 == '<' && i + 1 < n && (ch[i + 1].is_alphabetic() || matches!(ch[i + 1], '/' | '!' | '?')) {
                    let mut j = i + 1;
                    if matches!(ch[j], '/' | '!' | '?') {
                        j += 1;
                    }
                    let name_start = j;
                    while j < n && (ch[j].is_alphanumeric() || matches!(ch[j], '-' | '_' | ':' | '.')) {
                        j += 1;
                    }
                    if j > name_start {
                        emit(&mut out, &off, name_start, j, KEYWORD);
                    }
                    in_tag = true;
                    i = j;
                    continue;
                }
                if c0 == '&' {
                    let mut j = i + 1;
                    while j < n && j - i < 12 && (ch[j].is_alphanumeric() || ch[j] == '#') {
                        j += 1;
                    }
                    if j < n && ch[j] == ';' && j > i + 1 {
                        emit(&mut out, &off, i, j + 1, CONSTANT);
                        i = j + 1;
                        continue;
                    }
                }
                i += 1;
                continue;
            }
            if c0 == '>' {
                in_tag = false;
                i += 1;
                continue;
            }
        }

        // Attributes / directives.
        if !c.attr.is_empty() && starts(&ch, i, &c.attr) {
            let mut j = i + c.attr.len();
            if j < n && ident_start(ch[j]) {
                while j < n && ident_part(ch[j]) {
                    j += 1;
                }
                emit(&mut out, &off, i, j, ATTRIBUTE);
                i = j;
                after_def = false;
                continue;
            }
        }
        if c.lang.preprocessor && c0 == '#' && at_line_start {
            let mut j = i + 1;
            while j < n && (ch[j] == ' ' || ch[j] == '\t') {
                j += 1;
            }
            let word_start = j;
            while j < n && ident_part(ch[j]) {
                j += 1;
            }
            if j > word_start {
                emit(&mut out, &off, i, j, ATTRIBUTE);
                i = j;
                after_def = false;
                continue;
            }
        }

        // Strings.
        if let Some((open, multiline)) = c.delims.iter().find(|(o, _)| starts(&ch, i, o)) {
            let mut j = i + open.len();
            while j < n {
                if ch[j] == '\\' {
                    j = (j + 2).min(n);
                } else if starts(&ch, j, open) {
                    j += open.len();
                    break;
                } else if !*multiline && ch[j] == '\n' {
                    break;
                } else {
                    j += 1;
                }
            }
            let mut kind = STRING;
            if c.lang.string_keys && ch[j.min(n)..].iter().find(|c| !c.is_whitespace()) == Some(&':') {
                kind = PROPERTY;
            }
            emit(&mut out, &off, i, j, kind);
            i = j;
            after_def = false;
            continue;
        }

        // Hex colours (#fff, #a1b2c3).
        if c.lang.hex_colors && c0 == '#' {
            let mut j = i + 1;
            while j < n && ch[j].is_ascii_hexdigit() {
                j += 1;
            }
            if matches!(j - i - 1, 3 | 4 | 6 | 8) && (j >= n || !ident_part(ch[j])) {
                emit(&mut out, &off, i, j, NUMBER);
                i = j;
                after_def = false;
                continue;
            }
        }

        // Numbers.
        let after_operand = i > 0 && (ident_part(ch[i - 1]) || ch[i - 1] == ')' || ch[i - 1] == ']');
        if c0.is_ascii_digit()
            || (c0 == '.' && i + 1 < n && ch[i + 1].is_ascii_digit() && !after_operand)
        {
            let hex = c0 == '0' && i + 1 < n && (ch[i + 1] == 'x' || ch[i + 1] == 'X');
            let mut j = i + 1;
            while j < n {
                let x = ch[j];
                let exp_sign = (x == '+' || x == '-')
                    && !hex
                    && (ch[j - 1] == 'e' || ch[j - 1] == 'E')
                    && j + 1 < n
                    && ch[j + 1].is_ascii_digit();
                let frac = x == '.' && j + 1 < n && ch[j + 1].is_ascii_digit();
                if x.is_alphanumeric() || x == '_' || frac || exp_sign {
                    j += 1;
                } else {
                    break;
                }
            }
            emit(&mut out, &off, i, j, NUMBER);
            i = j;
            after_def = false;
            continue;
        }

        // Identifiers.
        if ident_start(c0) {
            let mut j = i + 1;
            while j < n && (ident_part(ch[j]) || (c.lang.dash_idents && ch[j] == '-' && j + 1 < n && ident_part(ch[j + 1]))) {
                j += 1;
            }
            if c.lang.markup {
                // Inside a tag every word is an attribute name.
                emit(&mut out, &off, i, j, PROPERTY);
                i = j;
                continue;
            }
            let word: String = ch[i..j].iter().collect();
            let lookup = if c.ci { word.to_lowercase() } else { word.clone() };

            let prev = ch[..i].iter().rev().find(|c| !c.is_whitespace()).copied();
            let mut q = j;
            while q < n && (ch[q] == ' ' || ch[q] == '\t') {
                q += 1;
            }
            let next = ch.get(q).copied();

            let kind = if c.keywords.contains(&lookup) {
                Some(KEYWORD)
            } else if c.control.contains(&lookup) {
                Some(CONTROL)
            } else if c.types.contains(&lookup) {
                Some(TYPE)
            } else if c.constants.contains(&lookup) {
                Some(CONSTANT)
            } else if next.is_some_and(|nc| c.key_chars.contains(&nc) && ch.get(q + 1) != Some(&nc)) {
                Some(PROPERTY)
            } else if after_def {
                Some(DEFINITION)
            } else if prev == Some('.') {
                Some(if next == Some('(') { FUNCTION } else { PROPERTY })
            } else if c.lang.prefix_calls && prev == Some('(') {
                Some(FUNCTION)
            } else if next == Some('(') {
                Some(FUNCTION)
            } else if c.pascal && is_pascal(&word) {
                Some(TYPE)
            } else if c.pascal && is_screaming(&word) {
                Some(CONSTANT)
            } else {
                None
            };

            if let Some(k) = kind {
                emit(&mut out, &off, i, j, k);
            }
            after_def = c.defs.contains(&lookup);
            i = j;
            continue;
        }

        after_def = false;
        i += 1;
    }
    out
}

// ------------------------------------------------------------- registry --

static REGISTRY: RwLock<Vec<Arc<Compiled>>> = RwLock::new(Vec::new());

fn find(id: &str) -> Option<Arc<Compiled>> {
    REGISTRY.read().ok()?.iter().find(|l| l.lang.id == id).cloned()
}

/// The language whose extension/file name matches `path` best (longest match;
/// later registrations win ties so user extensions can override built-ins).
pub fn language_for_path(path: &str) -> Option<String> {
    let name = Path::new(path).file_name()?.to_str()?;
    let lower = name.to_lowercase();
    let reg = REGISTRY.read().ok()?;

    let mut best: Option<(usize, &Compiled)> = None;
    for l in reg.iter() {
        let exact = l.lang.filenames.iter().any(|f| f == name).then_some(usize::MAX);
        let ext = l
            .lang
            .extensions
            .iter()
            .filter(|e| lower.ends_with(&e.to_lowercase()))
            .map(|e| e.len())
            .max();
        if let Some(score) = exact.or(ext) {
            if best.map_or(true, |(s, _)| score >= s) {
                best = Some((score, l));
            }
        }
    }
    best.map(|(_, l)| l.lang.id.clone())
}

// --------------------------------------------------------------- C ABI --

/// Registers (or replaces) a language from its JSON definition. 0 = ok, -1 = invalid.
#[no_mangle]
pub extern "C" fn lumina_register_language(json: *const c_char) -> i32 {
    let Some(text) = arg(json) else { return -1 };
    let Ok(lang) = serde_json::from_str::<Language>(text) else { return -1 };
    if lang.id.is_empty() {
        return -1;
    }
    let compiled = Arc::new(Compiled::new(lang));
    let Ok(mut reg) = REGISTRY.write() else { return -1 };
    match reg.iter().position(|l| l.lang.id == compiled.lang.id) {
        Some(i) => reg[i] = compiled,
        None => reg.push(compiled),
    }
    0
}

/// Drops every registered language (used before reloading extensions).
#[no_mangle]
pub extern "C" fn lumina_clear_languages() {
    if let Ok(mut reg) = REGISTRY.write() {
        reg.clear();
    }
}

/// Language id for a file path, or null when nothing matches.
#[no_mangle]
pub extern "C" fn lumina_language_for_path(path: *const c_char) -> *mut c_char {
    match arg(path).and_then(language_for_path) {
        Some(id) => out(id),
        None => ptr::null_mut(),
    }
}

/// JSON array of `start,len,kind` triples (UTF-16 offsets). Null if the language is unknown.
#[no_mangle]
pub extern "C" fn lumina_tokenize(lang_id: *const c_char, text: *const c_char) -> *mut c_char {
    let (Some(id), Some(text)) = (arg(lang_id), arg(text)) else {
        return ptr::null_mut();
    };
    let Some(lang) = find(id) else { return ptr::null_mut() };
    let spans = tokenize(&lang, text);

    let mut s = String::with_capacity(spans.len() * 5 + 2);
    s.push('[');
    for (i, v) in spans.iter().enumerate() {
        if i > 0 {
            s.push(',');
        }
        s.push_str(&v.to_string());
    }
    s.push(']');
    out(s)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vanta() -> Compiled {
        Compiled::new(
            serde_json::from_str(
                r##"{"id":"vanta-test","name":"Vanta","extensions":[".vanta"],
                "lineComments":["#"],"blockComments":[["#|","|#"]],"strings":["\""],
                "keywords":["module","func","let","mut","if"],"control":["return"],
                "types":["float","void"],"constants":["true","false"],
                "definitionKeywords":["func"],"attributePrefix":"@","pascalCaseTypes":false}"##,
            )
            .unwrap(),
        )
    }

    /// Decodes triples into (text, kind) pairs for readable assertions.
    fn tokens(c: &Compiled, src: &str) -> Vec<(String, u32)> {
        let u16s: Vec<u16> = src.encode_utf16().collect();
        tokenize(c, src)
            .chunks(3)
            .map(|t| {
                let s = t[0] as usize;
                (String::from_utf16(&u16s[s..s + t[1] as usize]).unwrap(), t[2])
            })
            .collect()
    }

    #[test]
    fn classifies_a_vanta_script() {
        let src = "module Main;\nfunc Tick(let dt::float)::void {\n  ctx.Move(4.0, \"hi {dt}\"); # note\n  return;\n}\n";
        let t = tokens(&vanta(), src);
        let has = |w: &str, k: u32| t.iter().any(|(s, kind)| s == w && *kind == k);
        assert!(has("module", KEYWORD));
        assert!(has("Tick", DEFINITION));
        assert!(has("float", TYPE));
        assert!(has("Move", FUNCTION));
        assert!(!t.iter().any(|(s, _)| s == "ctx"), "plain identifiers stay uncoloured");
        assert!(has("4.0", NUMBER));
        assert!(has("\"hi {dt}\"", STRING));
        assert!(has("# note", COMMENT));
        assert!(has("return", CONTROL));
    }

    #[test]
    fn block_comments_directives_and_escapes() {
        let t = tokens(&vanta(), "@use \"a\\\"b\"; #| one\ntwo |# let");
        assert_eq!(t[0], ("@use".into(), ATTRIBUTE));
        assert_eq!(t[1], ("\"a\\\"b\"".into(), STRING));
        assert_eq!(t[2], ("#| one\ntwo |#".into(), COMMENT));
        assert_eq!(t[3], ("let".into(), KEYWORD));
    }

    #[test]
    fn offsets_are_utf16_code_units() {
        // The emoji is 2 UTF-16 units, so `let` starts at offset 5, not 4.
        let spans = tokenize(&vanta(), "\"😀\" let");
        assert_eq!(&spans[..3], &[0, 4, STRING]);
        assert_eq!(&spans[3..6], &[5, 3, KEYWORD]);
    }

    fn compile(json: &str) -> Compiled {
        Compiled::new(serde_json::from_str(json).unwrap())
    }

    #[test]
    fn markup_colours_tags_and_attributes_but_not_text() {
        let c = compile(r#"{"id":"m","markup":true,"blockComments":[["<!--","-->"]],"strings":["\"","'"]}"#);
        let t = tokens(&c, "<!-- c --><div class=\"a b\" id='x'>it's &amp; <b>ok</b></div>");
        let has = |w: &str, k: u32| t.iter().any(|(s, kind)| s == w && *kind == k);
        assert!(has("<!-- c -->", COMMENT));
        assert!(has("div", KEYWORD) && has("b", KEYWORD));
        assert!(has("class", PROPERTY) && has("id", PROPERTY));
        assert!(has("\"a b\"", STRING) && has("'x'", STRING));
        assert!(has("&amp;", CONSTANT));
        // The apostrophe in "it's" must not start a string that swallows the rest.
        assert!(!t.iter().any(|(s, k)| *k == STRING && s.contains("ok")), "{t:?}");
    }

    #[test]
    fn css_style_properties_colors_and_units() {
        let c = compile(r##"{"id":"c","dashIdents":true,"hexColors":true,"keyChars":":","strings":["\""],"attributePrefix":"@","pascalCaseTypes":false}"##);
        let t = tokens(&c, "@media a { .x { background-color: #ff8800; margin: 10px; } }");
        let has = |w: &str, k: u32| t.iter().any(|(s, kind)| s == w && *kind == k);
        assert!(has("@media", ATTRIBUTE));
        assert!(has("background-color", PROPERTY) && has("margin", PROPERTY));
        assert!(has("#ff8800", NUMBER) && has("10px", NUMBER));
    }

    #[test]
    fn json_keys_yaml_keys_and_case_insensitive_keywords() {
        let j = compile(r#"{"id":"j","strings":["\""],"stringKeys":true,"constants":["true"]}"#);
        let t = tokens(&j, r#"{"name": "x", "ok": true}"#);
        assert!(t.contains(&("\"name\"".into(), PROPERTY)) && t.contains(&("\"x\"".into(), STRING)));

        let y = compile(r##"{"id":"y","keyChars":":","lineComments":["#"],"pascalCaseTypes":false}"##);
        assert!(tokens(&y, "name: dev # c\nurl: http://x").contains(&("name".into(), PROPERTY)));

        let sql = compile(r#"{"id":"s","caseInsensitive":true,"keywords":["select","from"]}"#);
        let t = tokens(&sql, "SELECT a FrOm t");
        assert!(t.contains(&("SELECT".into(), KEYWORD)) && t.contains(&("FrOm".into(), KEYWORD)));
    }

    #[test]
    fn lisp_calls_diff_lines_and_nix_style_keys() {
        let l = compile(r#"{"id":"l","dashIdents":true,"prefixCalls":true,"lineComments":[";"],"keywords":["defun"],"definitionKeywords":["defun"],"pascalCaseTypes":false}"#);
        let t = tokens(&l, "(defun my-fn (x) (do-thing x)) ; hi");
        assert!(t.contains(&("defun".into(), KEYWORD)) && t.contains(&("my-fn".into(), DEFINITION)));
        assert!(t.contains(&("do-thing".into(), FUNCTION)) && t.contains(&("; hi".into(), COMMENT)));

        let d = compile(r#"{"id":"d","linePrefixes":[["+","string"],["-","control"],["@@","attribute"],["+++","comment"]]}"#);
        let t = tokens(&d, "+++ b/x\n@@ -1 +1 @@\n-old\n+new\n ctx");
        assert_eq!(t[0].1, COMMENT);
        assert_eq!(t[1].1, ATTRIBUTE);
        assert_eq!((t[2].1, t[3].1), (CONTROL, STRING));

        let n = compile(r##"{"id":"n","keyChars":"=","lineComments":["#"],"multilineStrings":["''"],"strings":["\""],"keywords":["let","in"],"pascalCaseTypes":false}"##);
        let t = tokens(&n, "let a = 1; b == 2; s = \'\'multi\nline\'\'; in a");
        assert!(t.contains(&("a".into(), PROPERTY)) && t.contains(&("s".into(), PROPERTY)));
        assert!(!t.contains(&("b".into(), PROPERTY)), "== is not an assignment");
        assert!(t.iter().any(|(s, k)| *k == STRING && s.contains("line")));
    }

    #[test]
    fn picks_language_by_extension_and_prefers_later_registration() {
        for (id, ext) in [("zz-a", ".zzq"), ("zz-b", ".zzq")] {
            let json = format!(r#"{{"id":"{id}","extensions":["{ext}"]}}"#);
            let c = std::ffi::CString::new(json).unwrap();
            assert_eq!(lumina_register_language(c.as_ptr()), 0);
        }
        assert_eq!(language_for_path("/x/File.ZZQ").as_deref(), Some("zz-b"));
        assert_eq!(language_for_path("/x/file.unknown"), None);
    }
}
