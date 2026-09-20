//! Markdown -> a small JSON document tree for the preview pane.
//!
//! Blocks (`t`): `h` {l, c}, `p` {c}, `code` {lang, text}, `quote` {c: blocks},
//! `list` {ord, start, items: [{task, c: blocks}]}, `hr`, `table` {al, head, rows}, `html` {text}.
//! Inline runs (in `c` / cells): `{t:"text", s, b?, i?, x?, code?, href?}`, `{t:"br"}`, `{t:"img", src, alt}`.
//! Style flags are already flattened onto each run, so the renderer never has to track nesting.

use std::ffi::c_char;
use std::iter::Peekable;
use std::ptr;

use pulldown_cmark::{Alignment, CodeBlockKind, Event, HeadingLevel, Options, Parser, Tag, TagEnd};
use serde_json::{json, Value};

use crate::{arg, out};

type Events<'a> = Peekable<Parser<'a>>;

#[derive(Clone, Default)]
struct Style {
    bold: bool,
    italic: bool,
    strike: bool,
    href: Option<String>,
}

fn run(text: &str, st: &Style, code: bool) -> Value {
    let mut v = json!({ "t": "text", "s": text });
    let o = v.as_object_mut().unwrap();
    if st.bold {
        o.insert("b".into(), true.into());
    }
    if st.italic {
        o.insert("i".into(), true.into());
    }
    if st.strike {
        o.insert("x".into(), true.into());
    }
    if code {
        o.insert("code".into(), true.into());
    }
    if let Some(h) = &st.href {
        o.insert("href".into(), h.clone().into());
    }
    v
}

/// Value of an HTML attribute (`src="x"`, `src='x'` or `src=x`), case-insensitive on the name.
fn html_attr(tag: &str, name: &str) -> Option<String> {
    let lower = tag.to_ascii_lowercase();
    let mut from = 0;
    while let Some(pos) = lower[from..].find(name) {
        let start = from + pos;
        from = start + name.len();
        // must be a whole attribute name: preceded by whitespace, followed by optional spaces then '='
        let before_ok = start == 0 || lower.as_bytes()[start - 1].is_ascii_whitespace();
        let rest = tag[from..].trim_start();
        if !before_ok || !rest.starts_with('=') {
            continue;
        }
        let value = rest[1..].trim_start();
        return Some(match value.chars().next() {
            Some(q @ ('"' | '\'')) => value[1..].split(q).next().unwrap_or("").to_string(),
            _ => value.split(|c: char| c.is_whitespace() || c == '>' || c == '/').next().unwrap_or("").to_string(),
        });
    }
    None
}

/// Every `<img ...>` in a chunk of HTML as an image run.
fn html_images(html: &str) -> Vec<Value> {
    let mut found = vec![];
    let lower = html.to_ascii_lowercase();
    let mut from = 0;
    while let Some(pos) = lower[from..].find("<img") {
        let start = from + pos;
        let end = lower[start..].find('>').map_or(html.len(), |e| start + e + 1);
        let tag = &html[start..end];
        from = end;
        if let Some(src) = html_attr(tag, "src").filter(|s| !s.is_empty()) {
            let mut img = json!({ "t": "img", "src": src, "alt": html_attr(tag, "alt").unwrap_or_default() });
            if let Some(w) = html_attr(tag, "width").and_then(|w| w.trim_end_matches("px").parse::<u32>().ok()) {
                img["w"] = w.into();
            }
            if let Some(h) = html_attr(tag, "height").and_then(|h| h.trim_end_matches("px").parse::<u32>().ok()) {
                img["h"] = h.into();
            }
            found.push(img);
        }
    }
    found
}

fn is_break_tag(html: &str) -> bool {
    let t = html.trim().to_ascii_lowercase();
    t == "<br>" || t == "<br/>" || t == "<br />"
}

/// Reads inline events until it meets something that isn't inline (an `End`, or a block `Start`),
/// which it leaves unconsumed for the caller.
fn inlines(it: &mut Events, st: &Style) -> Vec<Value> {
    let mut out: Vec<Value> = vec![];
    while let Some(ev) = it.peek() {
        match ev {
            Event::Text(_) | Event::Code(_) | Event::SoftBreak | Event::HardBreak | Event::InlineHtml(_) | Event::FootnoteReference(_) => {
                let ev = it.next().unwrap();
                match ev {
                    Event::Text(t) => out.push(run(&t, st, false)),
                    Event::Code(t) => out.push(run(&t, st, true)),
                    Event::SoftBreak => out.push(run(" ", st, false)),
                    Event::HardBreak => out.push(json!({ "t": "br" })),
                    Event::InlineHtml(h) if is_break_tag(&h) => out.push(json!({ "t": "br" })),
                    Event::InlineHtml(h) => out.extend(html_images(&h)),
                    _ => {} // other inline HTML and footnote refs are dropped
                }
            }
            Event::Start(Tag::Emphasis) => {
                it.next();
                out.extend(inlines(it, &Style { italic: true, ..st.clone() }));
                it.next(); // End(Emphasis)
            }
            Event::Start(Tag::Strong) => {
                it.next();
                out.extend(inlines(it, &Style { bold: true, ..st.clone() }));
                it.next();
            }
            Event::Start(Tag::Strikethrough) => {
                it.next();
                out.extend(inlines(it, &Style { strike: true, ..st.clone() }));
                it.next();
            }
            Event::Start(Tag::Link { dest_url, .. }) => {
                let href = dest_url.to_string();
                it.next();
                out.extend(inlines(it, &Style { href: Some(href), ..st.clone() }));
                it.next();
            }
            Event::Start(Tag::Image { dest_url, .. }) => {
                let src = dest_url.to_string();
                it.next();
                let mut alt = String::new();
                while let Some(e) = it.next() {
                    match e {
                        Event::End(TagEnd::Image) => break,
                        Event::Text(t) | Event::Code(t) => alt.push_str(&t),
                        Event::SoftBreak | Event::HardBreak => alt.push(' '),
                        _ => {}
                    }
                }
                let mut img = json!({ "t": "img", "src": src, "alt": alt });
                if let Some(h) = &st.href {
                    img["href"] = h.clone().into(); // [![badge](img)](url): the picture is the link
                }
                out.push(img);
            }
            _ => break,
        }
    }
    out
}

/// Consumes events up to and including the matching end of the block we are already inside.
fn blocks(it: &mut Events) -> Vec<Value> {
    let mut out: Vec<Value> = vec![];
    while let Some(ev) = it.peek() {
        match ev {
            Event::End(_) => break,
            Event::Start(Tag::Paragraph) => {
                it.next();
                let c = inlines(it, &Style::default());
                it.next();
                out.push(json!({ "t": "p", "c": c }));
            }
            Event::Start(Tag::Heading { level, .. }) => {
                let l = match level {
                    HeadingLevel::H1 => 1,
                    HeadingLevel::H2 => 2,
                    HeadingLevel::H3 => 3,
                    HeadingLevel::H4 => 4,
                    HeadingLevel::H5 => 5,
                    HeadingLevel::H6 => 6,
                };
                it.next();
                let c = inlines(it, &Style::default());
                it.next();
                out.push(json!({ "t": "h", "l": l, "c": c }));
            }
            Event::Start(Tag::BlockQuote(_)) => {
                it.next();
                let c = blocks(it);
                it.next();
                out.push(json!({ "t": "quote", "c": c }));
            }
            Event::Start(Tag::CodeBlock(kind)) => {
                let lang = match kind {
                    CodeBlockKind::Fenced(info) => info.split_whitespace().next().unwrap_or("").to_string(),
                    CodeBlockKind::Indented => String::new(),
                };
                it.next();
                let mut text = String::new();
                for e in it.by_ref() {
                    match e {
                        Event::End(TagEnd::CodeBlock) => break,
                        Event::Text(t) => text.push_str(&t),
                        _ => {}
                    }
                }
                out.push(json!({ "t": "code", "lang": lang, "text": text.trim_end_matches('\n') }));
            }
            Event::Start(Tag::List(start)) => {
                let (ord, first) = (start.is_some(), start.unwrap_or(1));
                it.next();
                let mut items = vec![];
                while let Some(Event::Start(Tag::Item)) = it.peek() {
                    it.next();
                    let task = if let Some(Event::TaskListMarker(done)) = it.peek() {
                        let d = *done;
                        it.next();
                        Value::Bool(d)
                    } else {
                        Value::Null
                    };
                    let c = blocks(it);
                    it.next(); // End(Item)
                    items.push(json!({ "task": task, "c": c }));
                }
                it.next(); // End(List)
                out.push(json!({ "t": "list", "ord": ord, "start": first, "items": items }));
            }
            Event::Start(Tag::Table(aligns)) => {
                let al: Vec<&str> = aligns
                    .iter()
                    .map(|a| match a {
                        Alignment::Left => "l",
                        Alignment::Center => "c",
                        Alignment::Right => "r",
                        Alignment::None => "n",
                    })
                    .collect();
                let al = json!(al);
                it.next();
                let mut head: Vec<Value> = vec![];
                let mut rows: Vec<Value> = vec![];
                let cells = |it: &mut Events| -> Vec<Value> {
                    let mut cells = vec![];
                    while let Some(Event::Start(Tag::TableCell)) = it.peek() {
                        it.next();
                        cells.push(Value::Array(inlines(it, &Style::default())));
                        it.next(); // End(TableCell)
                    }
                    cells
                };
                while let Some(e) = it.peek() {
                    match e {
                        Event::Start(Tag::TableHead) => {
                            it.next();
                            head = cells(it);
                            it.next(); // End(TableHead)
                        }
                        Event::Start(Tag::TableRow) => {
                            it.next();
                            rows.push(Value::Array(cells(it)));
                            it.next(); // End(TableRow)
                        }
                        _ => break,
                    }
                }
                it.next(); // End(Table)
                out.push(json!({ "t": "table", "al": al, "head": head, "rows": rows }));
            }
            Event::Start(Tag::HtmlBlock) => {
                it.next();
                let mut text = String::new();
                for e in it.by_ref() {
                    match e {
                        Event::End(TagEnd::HtmlBlock) => break,
                        Event::Html(t) | Event::Text(t) => text.push_str(&t),
                        _ => {}
                    }
                }
                out.push(json!({ "t": "html", "text": text, "imgs": html_images(&text) }));
            }
            Event::Rule => {
                it.next();
                out.push(json!({ "t": "hr" }));
            }
            Event::Html(_) => {
                if let Some(Event::Html(t)) = it.next() {
                    out.push(json!({ "t": "html", "text": t.to_string(), "imgs": html_images(&t) }));
                }
            }
            // Bare inline content (tight list items) becomes an implicit paragraph.
            Event::Text(_) | Event::Code(_) | Event::SoftBreak | Event::HardBreak | Event::InlineHtml(_) | Event::FootnoteReference(_)
            | Event::Start(Tag::Emphasis | Tag::Strong | Tag::Strikethrough | Tag::Link { .. } | Tag::Image { .. }) => {
                let c = inlines(it, &Style::default());
                if !c.is_empty() {
                    out.push(json!({ "t": "p", "c": c }));
                }
            }
            // Anything else we don't model (footnote definitions, metadata...): render its children.
            Event::Start(_) => {
                it.next();
                out.extend(blocks(it));
                it.next();
            }
            _ => {
                it.next();
            }
        }
    }
    out
}

pub fn parse(markdown: &str) -> Value {
    let opts = Options::ENABLE_TABLES | Options::ENABLE_STRIKETHROUGH | Options::ENABLE_TASKLISTS;
    let mut it: Events = Parser::new_ext(markdown, opts).peekable();
    let mut all = vec![];
    while it.peek().is_some() {
        all.extend(blocks(&mut it));
        it.next(); // stray End at top level, if any
    }
    Value::Array(all)
}

/// JSON block tree for a Markdown document. Null on invalid input.
#[no_mangle]
pub extern "C" fn lumina_markdown(text: *const c_char) -> *mut c_char {
    match arg(text) {
        Some(t) => out(parse(t).to_string()),
        None => ptr::null_mut(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn runs(v: &Value) -> Vec<(String, String)> {
        v["c"].as_array().unwrap().iter().filter(|r| r["t"] == "text").map(|r| {
            let mut flags = String::new();
            for (k, f) in [("b", 'b'), ("i", 'i'), ("x", 'x'), ("code", 'c')] {
                if r[k] == true {
                    flags.push(f);
                }
            }
            if r["href"].is_string() {
                flags.push('L');
            }
            (r["s"].as_str().unwrap().to_string(), flags)
        }).collect()
    }

    #[test]
    fn inline_styles_flatten_and_nest() {
        let doc = parse("plain **bold *both* bold** ~~gone~~ `code` [site](https://x.dev)");
        let p = &doc[0];
        assert_eq!(p["t"], "p");
        assert_eq!(runs(p), [
            ("plain ".into(), "".into()),
            ("bold ".into(), "b".into()),
            ("both".into(), "bi".into()),
            (" bold".into(), "b".into()),
            (" ".into(), "".into()),
            ("gone".into(), "x".into()),
            (" ".into(), "".into()),
            ("code".into(), "c".into()),
            (" ".into(), "".into()),
            ("site".into(), "L".into()),
        ]);
        assert_eq!(p["c"][9]["href"], "https://x.dev");
    }

    #[test]
    fn headings_code_rules_and_quotes() {
        let doc = parse("# Title\n\n> quoted *text*\n\n---\n\n```rust title=x\nfn main() {}\n```\n");
        assert_eq!(doc[0], json!({ "t": "h", "l": 1, "c": [{ "t": "text", "s": "Title" }] }));
        assert_eq!(doc[1]["t"], "quote");
        assert_eq!(doc[1]["c"][0]["c"][1]["i"], true);
        assert_eq!(doc[2]["t"], "hr");
        assert_eq!(doc[3], json!({ "t": "code", "lang": "rust", "text": "fn main() {}" }));
    }

    #[test]
    fn nested_lists_tasks_and_ordering() {
        let doc = parse("1. one\n2. two\n   - [x] done\n   - [ ] todo\n");
        let list = &doc[0];
        assert_eq!(list["ord"], true);
        assert_eq!(list["start"], 1);
        let second = &list["items"][1];
        assert_eq!(second["c"][0]["c"][0]["s"], "two"); // tight item -> implicit paragraph
        let inner = &second["c"][1];
        assert_eq!(inner["t"], "list");
        assert_eq!(inner["ord"], false);
        assert_eq!(inner["items"][0]["task"], true);
        assert_eq!(inner["items"][1]["task"], false);
        assert_eq!(list["items"][0]["task"], Value::Null);
    }

    #[test]
    fn tables_images_and_breaks() {
        let doc = parse("| a | b |\n|:-:|--:|\n| **1** | 2 |\n\n![logo](img/l.png)\n\nline one  \nline two<br>three");
        let t = &doc[0];
        assert_eq!(t["t"], "table");
        assert_eq!(t["al"], json!(["c", "r"]));
        assert_eq!(t["head"][1][0]["s"], "b");
        assert_eq!(t["rows"][0][0][0]["b"], true);

        assert_eq!(doc[1]["c"][0], json!({ "t": "img", "src": "img/l.png", "alt": "logo" }));
        let kinds: Vec<_> = doc[2]["c"].as_array().unwrap().iter().map(|r| r["t"].as_str().unwrap()).collect();
        assert_eq!(kinds, ["text", "br", "text", "br", "text"]);
    }

    #[test]
    fn html_img_tags_become_images_in_blocks_and_inline() {
        let doc = parse("<p align=\"center\">\n  <img src=\"logo.png\" alt=\"The Logo\" width=\"120px\">\n  <IMG SRC='b.svg'/>\n</p>\n\ntext <img src=x.png alt=\"inline one\"> more");
        let block = &doc[0];
        assert_eq!(block["t"], "html");
        assert_eq!(block["imgs"], json!([
            { "t": "img", "src": "logo.png", "alt": "The Logo", "w": 120 },
            { "t": "img", "src": "b.svg", "alt": "" },
        ]));
        let sized = parse("<a href=\"x\">\n  <img height=\"28\" src=\"https://x/b.svg\" alt=\"Badge\">\n</a>");
        assert_eq!(sized[0]["imgs"][0], json!({ "t": "img", "src": "https://x/b.svg", "alt": "Badge", "h": 28 }));
        let inline: Vec<_> = doc[1]["c"].as_array().unwrap().iter().filter(|r| r["t"] == "img").collect();
        assert_eq!(inline.len(), 1);
        assert_eq!(inline[0]["src"], "x.png");
        assert_eq!(inline[0]["alt"], "inline one");
    }

    #[test]
    fn images_inside_links_keep_the_link() {
        let doc = parse("[![build](https://x/badge.svg)](https://x/ci) and ![plain](p.png)");
        let runs = doc[0]["c"].as_array().unwrap();
        assert_eq!(runs[0], json!({ "t": "img", "src": "https://x/badge.svg", "alt": "build", "href": "https://x/ci" }));
        assert_eq!(runs[2], json!({ "t": "img", "src": "p.png", "alt": "plain" }));
    }

    #[test]
    fn html_attr_parsing_is_strict_about_names() {
        assert_eq!(html_attr("<img data-src=\"no\" src=\"yes\">", "src").as_deref(), Some("yes"));
        assert_eq!(html_attr("<img srcset=\"no\">", "src"), None);
        assert_eq!(html_attr("<img src = 'a b.png' >", "src").as_deref(), Some("a b.png"));
        assert_eq!(html_attr("<img alt=hello>", "alt").as_deref(), Some("hello"));
    }

    #[test]
    fn html_blocks_and_odd_input_do_not_panic() {
        let doc = parse("<p align=\"center\">hi</p>\n\ntext\n");
        assert_eq!(doc[0]["t"], "html");
        assert!(doc[0]["text"].as_str().unwrap().contains("align"));
        for src in ["", "\n\n", "* \n", "> > > deep\n> > > x", "[ref]: http://x\n\n[ref]", "- a\n\n  b\n- c", "Term\n: def", "[^1]: note\n\ntext[^1]"] {
            let _ = parse(src);
        }
    }
}
