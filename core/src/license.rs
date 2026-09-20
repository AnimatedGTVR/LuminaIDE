//! License detection. A license definition carries "fingerprints": distinctive phrases that
//! must all appear (and, optionally, phrases that must not). Text is normalised first (lower-case,
//! punctuation and line breaks collapsed to single spaces), so wrapping, quote styles, comment
//! markers and copyright symbols don't matter. The most specific matching license wins.

use std::ffi::c_char;
use std::ptr;
use std::sync::RwLock;

use serde::Deserialize;

use crate::{arg, out};

/// Only the start of a file is scanned; real licenses are recognisable within the first ~200 KB.
const MAX_SCAN_BYTES: usize = 200 * 1024;

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct Raw {
    id: String,
    match_all: Vec<String>,
    match_none: Vec<String>,
}

#[derive(Clone, Debug)]
pub struct Def {
    pub id: String,
    all: Vec<String>,
    none: Vec<String>,
}

/// Lower-case, keep letters and digits, collapse everything else to single spaces.
pub fn normalize(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut pending_space = false;
    for c in text.chars() {
        if c.is_alphanumeric() {
            if pending_space && !out.is_empty() {
                out.push(' ');
            }
            pending_space = false;
            out.extend(c.to_lowercase());
        } else {
            pending_space = true;
        }
    }
    out
}

impl Def {
    pub fn from_json(json: &str) -> Option<Def> {
        let raw: Raw = serde_json::from_str(json).ok()?;
        let all: Vec<String> = raw.match_all.iter().map(|s| normalize(s)).filter(|s| !s.is_empty()).collect();
        if raw.id.is_empty() || all.is_empty() {
            return None; // a license with no positive fingerprint could match anything
        }
        Some(Def { id: raw.id, all, none: raw.match_none.iter().map(|s| normalize(s)).filter(|s| !s.is_empty()).collect() })
    }

    /// How well this license matches the (normalised) text, or None if it doesn't match at all.
    /// Ordered by: more fingerprints matched, then a match nearer the top of the file (a license
    /// names itself first; other licenses only get mentioned later, e.g. MPL-2.0 lists the GPL family
    /// as allowed "secondary licenses"), then longer phrases.
    fn score(&self, norm: &str) -> Option<(usize, usize, usize)> {
        if self.none.iter().any(|f| norm.contains(f.as_str())) {
            return None;
        }
        let mut earliest = usize::MAX;
        for f in &self.all {
            earliest = earliest.min(norm.find(f.as_str())?);
        }
        Some((self.all.len(), usize::MAX - earliest, self.all.iter().map(String::len).sum()))
    }
}

/// The id of the best-matching license, or None (see [`Def::score`] for how candidates are ranked).
pub fn detect<'a>(defs: &'a [Def], text: &str) -> Option<&'a str> {
    let mut end = text.len().min(MAX_SCAN_BYTES);
    while !text.is_char_boundary(end) {
        end -= 1;
    }
    let norm = normalize(&text[..end]);
    defs.iter().filter_map(|d| d.score(&norm).map(|s| (s, d))).max_by_key(|(s, _)| *s).map(|(_, d)| d.id.as_str())
}

static REGISTRY: RwLock<Vec<Def>> = RwLock::new(Vec::new());

/// Registers (or replaces) a license definition from JSON. 0 = ok, -1 = invalid.
#[no_mangle]
pub extern "C" fn lumina_register_license(json: *const c_char) -> i32 {
    let Some(def) = arg(json).and_then(Def::from_json) else { return -1 };
    let Ok(mut reg) = REGISTRY.write() else { return -1 };
    match reg.iter().position(|d| d.id == def.id) {
        Some(i) => reg[i] = def,
        None => reg.push(def),
    }
    0
}

#[no_mangle]
pub extern "C" fn lumina_clear_licenses() {
    if let Ok(mut reg) = REGISTRY.write() {
        reg.clear();
    }
}

/// The id of the license the text is, or null when it isn't a known one.
#[no_mangle]
pub extern "C" fn lumina_detect_license(text: *const c_char) -> *mut c_char {
    let Some(t) = arg(text) else { return ptr::null_mut() };
    let Ok(reg) = REGISTRY.read() else { return ptr::null_mut() };
    match detect(&reg, t) {
        Some(id) => out(id.to_string()),
        None => ptr::null_mut(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalisation_ignores_case_punctuation_and_wrapping() {
        assert_eq!(normalize("  “Permission”  is\n hereby-granted,\tFREE of charge… "), "permission is hereby granted free of charge");
        assert_eq!(normalize("/* (c) 2024 */ // The Software"), "c 2024 the software");
        assert_eq!(normalize(""), "");
    }

    #[test]
    fn a_definition_without_a_positive_fingerprint_is_rejected() {
        assert!(Def::from_json(r#"{"id":"X"}"#).is_none());
        assert!(Def::from_json(r#"{"id":"","matchAll":["a"]}"#).is_none());
        assert!(Def::from_json("not json").is_none());
        assert!(Def::from_json(r#"{"id":"X","matchAll":["hello world"]}"#).is_some());
    }

    #[test]
    fn specific_beats_general_and_none_phrases_exclude() {
        let general = Def::from_json(r#"{"id":"general","matchAll":["free software"]}"#).unwrap();
        let specific = Def::from_json(r#"{"id":"specific","matchAll":["free software","for everyone"]}"#).unwrap();
        let excluded = Def::from_json(r#"{"id":"excl","matchAll":["free software"],"matchNone":["for everyone"]}"#).unwrap();
        let defs = [general, specific, excluded];
        assert_eq!(detect(&defs, "This is FREE software, for everyone."), Some("specific"));
        // Both "general" and "excl" match; they are equally specific, so either is acceptable but it must be one of them.
        assert!(matches!(detect(&defs, "This is free software."), Some("general" | "excl")));
        assert_eq!(detect(&defs, "nothing relevant"), None);
    }

    #[test]
    fn when_two_licenses_tie_the_one_named_first_wins() {
        let a = Def::from_json(r#"{"id":"mozilla","matchAll":["mozilla public license"]}"#).unwrap();
        let b = Def::from_json(r#"{"id":"gnu","matchAll":["gnu lesser general public license"]}"#).unwrap();
        let text = "Mozilla Public License\n... secondary licenses: GNU Lesser General Public License ...";
        assert_eq!(detect(&[b.clone(), a.clone()], text), Some("mozilla"));
        assert_eq!(detect(&[a, b], "GNU Lesser General Public License, see also the Mozilla Public License"), Some("gnu"));
    }

    #[test]
    fn multibyte_text_near_the_scan_limit_does_not_panic() {
        let d = Def::from_json(r#"{"id":"X","matchAll":["needle"]}"#).unwrap();
        let text = format!("needle {}", "é".repeat(MAX_SCAN_BYTES));
        assert_eq!(detect(&[d], &text), Some("X"));
    }
}
