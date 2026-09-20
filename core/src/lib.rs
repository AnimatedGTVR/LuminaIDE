//! LuminaIDE core. Everything crossing the C ABI is either a UTF-8 C string
//! or an int. Structured results are returned as JSON strings that the caller
//! must release with `lumina_free_string`.

use std::ffi::{c_char, CStr, CString};
use std::fs;
use std::path::Path;
use std::ptr;

pub mod agent;
pub mod license;
pub mod markdown;
pub mod syntax;

use ignore::WalkBuilder;
use serde::Serialize;

const MAX_HITS: usize = 500;
const MAX_SEARCH_FILE_BYTES: u64 = 2 * 1024 * 1024;

#[derive(Serialize)]
pub struct Entry {
    pub name: String,
    pub path: String,
    pub is_dir: bool,
}

#[derive(Serialize)]
pub struct Hit {
    pub path: String,
    pub line: usize,
    pub text: String,
}

/// Immediate children of `dir`, folders first, respecting .gitignore.
pub fn list_dir(dir: &Path) -> Vec<Entry> {
    let walk = WalkBuilder::new(dir)
        .max_depth(Some(1))
        .hidden(false)
        .require_git(false)
        .filter_entry(|e| e.file_name() != ".git")
        .build();

    let mut entries: Vec<Entry> = walk
        .filter_map(Result::ok)
        .filter(|e| e.depth() == 1)
        .map(|e| Entry {
            name: e.file_name().to_string_lossy().into_owned(),
            path: e.path().to_string_lossy().into_owned(),
            is_dir: e.file_type().is_some_and(|t| t.is_dir()),
        })
        .collect();

    entries.sort_by(|a, b| {
        b.is_dir
            .cmp(&a.is_dir)
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
    });
    entries
}

/// Case-insensitive substring search over every text file under `root`.
pub fn search(root: &Path, query: &str) -> Vec<Hit> {
    let needle = query.to_lowercase();
    let mut hits = Vec::new();
    if needle.is_empty() {
        return hits;
    }

    let walk = WalkBuilder::new(root).require_git(false).build();
    for entry in walk.filter_map(Result::ok) {
        if !entry.file_type().is_some_and(|t| t.is_file()) {
            continue;
        }
        if entry.metadata().map_or(true, |m| m.len() > MAX_SEARCH_FILE_BYTES) {
            continue;
        }
        // Non-UTF-8 (binary) files fail here and are skipped.
        let Ok(text) = fs::read_to_string(entry.path()) else {
            continue;
        };
        for (i, line) in text.lines().enumerate() {
            if line.to_lowercase().contains(&needle) {
                hits.push(Hit {
                    path: entry.path().to_string_lossy().into_owned(),
                    line: i + 1,
                    text: line.trim().chars().take(200).collect(),
                });
                if hits.len() >= MAX_HITS {
                    return hits;
                }
            }
        }
    }
    hits
}

/// Every file under `root` (relative paths, `/` separators), honouring .gitignore, up to `limit`.
pub fn list_files(root: &Path, limit: usize) -> Vec<String> {
    let walk = WalkBuilder::new(root)
        .require_git(false)
        .hidden(false)
        .filter_entry(|e| e.file_name() != ".git" && e.file_name() != "node_modules")
        .build();
    let mut files = Vec::new();
    for e in walk.filter_map(Result::ok) {
        if e.file_type().is_some_and(|t| t.is_file()) {
            if let Ok(rel) = e.path().strip_prefix(root) {
                files.push(rel.to_string_lossy().replace('\\', "/"));
                if files.len() >= limit {
                    break;
                }
            }
        }
    }
    files
}

// ---------------------------------------------------------------- C ABI --

pub(crate) fn arg<'a>(p: *const c_char) -> Option<&'a str> {
    if p.is_null() {
        return None;
    }
    unsafe { CStr::from_ptr(p) }.to_str().ok()
}

pub(crate) fn out(s: String) -> *mut c_char {
    CString::new(s.replace('\0', ""))
        .map(CString::into_raw)
        .unwrap_or(ptr::null_mut())
}

fn json<T: Serialize>(v: &T) -> *mut c_char {
    out(serde_json::to_string(v).unwrap_or_else(|_| "[]".into()))
}

/// JSON array of `{name, path, is_dir}`. Null on bad input.
#[no_mangle]
pub extern "C" fn lumina_list_dir(dir: *const c_char) -> *mut c_char {
    match arg(dir) {
        Some(d) => json(&list_dir(Path::new(d))),
        None => ptr::null_mut(),
    }
}

/// File contents as UTF-8 (invalid bytes replaced). Null if unreadable.
#[no_mangle]
pub extern "C" fn lumina_read_file(path: *const c_char) -> *mut c_char {
    let Some(p) = arg(path) else {
        return ptr::null_mut();
    };
    match fs::read(p) {
        Ok(bytes) => out(String::from_utf8_lossy(&bytes).into_owned()),
        Err(_) => ptr::null_mut(),
    }
}

/// Returns 0 on success, -1 on failure.
#[no_mangle]
pub extern "C" fn lumina_write_file(path: *const c_char, content: *const c_char) -> i32 {
    match (arg(path), arg(content)) {
        (Some(p), Some(c)) => fs::write(p, c).map_or(-1, |_| 0),
        _ => -1,
    }
}

/// JSON array of `{path, line, text}`, capped at 500 hits.
#[no_mangle]
pub extern "C" fn lumina_search(root: *const c_char, query: *const c_char) -> *mut c_char {
    match (arg(root), arg(query)) {
        (Some(r), Some(q)) => json(&search(Path::new(r), q)),
        _ => ptr::null_mut(),
    }
}

/// JSON array of relative file paths under `root` (for quick open), at most `limit`.
#[no_mangle]
pub extern "C" fn lumina_list_files(root: *const c_char, limit: i32) -> *mut c_char {
    match arg(root) {
        Some(r) => json(&list_files(Path::new(r), limit.max(1) as usize)),
        None => ptr::null_mut(),
    }
}

/// Releases any string returned by this library.
#[no_mangle]
pub extern "C" fn lumina_free_string(s: *mut c_char) {
    if !s.is_null() {
        drop(unsafe { CString::from_raw(s) });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lists_dirs_first_and_skips_ignored() {
        let d = tempfile::tempdir().unwrap();
        fs::write(d.path().join(".gitignore"), "skip.txt\n").unwrap();
        fs::write(d.path().join("skip.txt"), "x").unwrap();
        fs::write(d.path().join("b.txt"), "x").unwrap();
        fs::create_dir(d.path().join("zdir")).unwrap();
        fs::create_dir(d.path().join(".git")).unwrap();

        let names: Vec<_> = list_dir(d.path()).into_iter().map(|e| e.name).collect();
        assert_eq!(names, ["zdir", ".gitignore", "b.txt"]);
    }

    #[test]
    fn lists_files_relative_skipping_git_and_node_modules() {
        let d = tempfile::tempdir().unwrap();
        fs::create_dir_all(d.path().join("src/deep")).unwrap();
        fs::create_dir_all(d.path().join("node_modules/x")).unwrap();
        fs::create_dir(d.path().join(".git")).unwrap();
        fs::write(d.path().join("a.txt"), "x").unwrap();
        fs::write(d.path().join("src/deep/b.rs"), "x").unwrap();
        fs::write(d.path().join("node_modules/x/i.js"), "x").unwrap();
        fs::write(d.path().join(".git/HEAD"), "x").unwrap();

        let mut files = list_files(d.path(), 100);
        files.sort();
        assert_eq!(files, ["a.txt", "src/deep/b.rs"]);
        assert_eq!(list_files(d.path(), 1).len(), 1);
    }

    #[test]
    fn search_is_case_insensitive_and_reports_lines() {
        let d = tempfile::tempdir().unwrap();
        fs::write(d.path().join("a.txt"), "one\nHello World\nthree\n").unwrap();
        fs::write(d.path().join("bin.dat"), [0xff, 0xfe, 0x00]).unwrap();

        let hits = search(d.path(), "hello");
        assert_eq!(hits.len(), 1);
        assert_eq!(hits[0].line, 2);
        assert_eq!(hits[0].text, "Hello World");
    }

    #[test]
    fn write_then_read_roundtrip_over_c_abi() {
        let d = tempfile::tempdir().unwrap();
        let p = CString::new(d.path().join("f.txt").to_str().unwrap()).unwrap();
        let c = CString::new("héllo").unwrap();
        assert_eq!(lumina_write_file(p.as_ptr(), c.as_ptr()), 0);
        let r = lumina_read_file(p.as_ptr());
        assert_eq!(unsafe { CStr::from_ptr(r) }.to_str().unwrap(), "héllo");
        lumina_free_string(r);
    }
}
