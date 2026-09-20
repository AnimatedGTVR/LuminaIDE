//! Sanity checks over every shipped language definition in ../extensions.

use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

use lumina_core::syntax::{tokenize, Compiled, Language};

fn language_files() -> Vec<PathBuf> {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../extensions");
    let mut files = vec![];
    for ext in fs::read_dir(root).unwrap().filter_map(Result::ok) {
        if let Ok(langs) = fs::read_dir(ext.path().join("languages")) {
            files.extend(langs.filter_map(Result::ok).map(|e| e.path()));
        }
    }
    files.sort();
    files
}

fn load() -> Vec<(PathBuf, Language)> {
    language_files()
        .into_iter()
        .map(|p| {
            let text = fs::read_to_string(&p).unwrap();
            let lang: Language = serde_json::from_str(&text).unwrap_or_else(|e| panic!("{}: {e}", p.display()));
            (p, lang)
        })
        .collect()
}

#[test]
fn there_are_at_least_fifty_languages_with_unique_ids_and_names() {
    let langs = load();
    assert!(langs.len() >= 70, "only {} languages found", langs.len());

    let mut ids = HashMap::new();
    for (p, l) in &langs {
        assert!(!l.id.is_empty() && !l.name.is_empty(), "{}: id and name are required", p.display());
        assert_eq!(p.file_stem().unwrap().to_str().unwrap(), l.id, "file name should match id");
        assert!(ids.insert(l.id.clone(), p.clone()).is_none(), "duplicate id {}", l.id);
        assert!(!l.extensions.is_empty() || !l.filenames.is_empty(), "{}: matches no files", l.id);
    }
}

#[test]
fn no_two_languages_claim_the_same_extension_or_filename() {
    let mut seen: HashMap<String, String> = HashMap::new();
    for (_, l) in load() {
        for e in &l.extensions {
            assert!(e.starts_with('.'), "{}: extension {e:?} must start with a dot", l.id);
            let key = e.to_lowercase();
            if let Some(other) = seen.insert(format!("ext:{key}"), l.id.clone()) {
                assert_eq!(other, l.id, "extension {e} is claimed by both {other} and {}", l.id);
            }
        }
        for f in &l.filenames {
            if let Some(other) = seen.insert(format!("file:{f}"), l.id.clone()) {
                // A file name that is also listed as an extension of the same language is fine.
                assert_eq!(other, l.id, "file name {f} is claimed by both {other} and {}", l.id);
            }
        }
    }
}

#[test]
fn every_language_survives_nasty_input_with_valid_spans() {
    let corpus = [
        "",
        "\"unterminated string\n'and another",
        "/* unterminated block comment",
        "<div class=\"a\" <<< >>> & &amp <!-- x",
        "λ → 日本語 😀 \"😀\" // 😀\n#|😀|#",
        "a-b--c ---- ++ == := :: ::: {{ }} [[ ]] (( ))",
        "0x1F 1e-5 .5 5. 1_000 #fff #ffffffff",
        "\n\n\t\t  \r\n",
        "def f():\n    return \"\"\"doc\"\"\"",
    ];
    for (p, lang) in load() {
        let id = lang.id.clone();
        let c = Compiled::new(lang);
        for text in corpus {
            let total: u32 = text.encode_utf16().count() as u32;
            let spans = tokenize(&c, text);
            assert_eq!(spans.len() % 3, 0, "{id}");
            let mut end = 0;
            for t in spans.chunks(3) {
                assert!(t[0] >= end, "{} ({id}): overlapping or unsorted span at {}", p.display(), t[0]);
                assert!(t[1] > 0 && t[0] + t[1] <= total, "{id}: span out of range on {text:?}");
                assert!(t[2] <= 10, "{id}: unknown kind {}", t[2]);
                end = t[0] + t[1];
            }
        }
    }
}

#[test]
fn spot_checks_of_real_snippets() {
    let by_id: HashMap<String, Language> = load().into_iter().map(|(_, l)| (l.id.clone(), l)).collect();
    let kinds = |id: &str, text: &str| -> Vec<(String, u32)> {
        let c = Compiled::new(by_id[id].clone());
        let u16s: Vec<u16> = text.encode_utf16().collect();
        tokenize(&c, text)
            .chunks(3)
            .map(|t| (String::from_utf16(&u16s[t[0] as usize..(t[0] + t[1]) as usize]).unwrap(), t[2]))
            .collect()
    };
    use lumina_core::syntax::{COMMENT, CONTROL, KEYWORD, PROPERTY, STRING};

    let nix = kinds("nix", "let pkg = \"x\"; in # note\n''multi\nline''");
    assert!(nix.contains(&("let".into(), KEYWORD)) && nix.contains(&("pkg".into(), PROPERTY)));
    assert!(nix.contains(&("# note".into(), COMMENT)) && nix.iter().any(|(s, k)| *k == STRING && s.contains("line")));

    let html = kinds("html", "<a href=\"/x\">it's</a>");
    assert!(html.contains(&("a".into(), KEYWORD)) && html.contains(&("href".into(), PROPERTY)) && html.contains(&("\"/x\"".into(), STRING)));

    let pkg = kinds("json", "{\"name\": \"lumina\", \"private\": true}");
    assert!(pkg.contains(&("\"name\"".into(), PROPERTY)) && pkg.contains(&("\"lumina\"".into(), STRING)));

    let docker = kinds("dockerfile", "from node:20\nRUN npm ci");
    assert!(docker.contains(&("from".into(), KEYWORD)) && docker.contains(&("RUN".into(), KEYWORD)));

    let diff = kinds("diff", "@@ -1 +1 @@\n-old\n+new");
    assert!(diff.contains(&("-old".into(), CONTROL)) && diff.contains(&("+new".into(), STRING)));
}
