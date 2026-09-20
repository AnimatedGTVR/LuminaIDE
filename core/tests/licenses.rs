//! Detects every shipped license against real license texts, and sanity-checks the definitions.

use std::collections::HashSet;
use std::fs;
use std::path::PathBuf;

use lumina_core::license::{detect, normalize, Def};
use serde_json::Value;

fn root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
}

fn raw_definitions() -> Vec<(PathBuf, String)> {
    let dir = root().join("../extensions/license-info/licenses");
    let mut files: Vec<_> = fs::read_dir(dir).unwrap().filter_map(Result::ok).map(|e| e.path()).collect();
    files.sort();
    files.into_iter().map(|p| { let t = fs::read_to_string(&p).unwrap(); (p, t) }).collect()
}

fn defs() -> Vec<Def> {
    raw_definitions().iter().map(|(p, t)| Def::from_json(t).unwrap_or_else(|| panic!("{} is not a valid license", p.display()))).collect()
}

fn fixture(name: &str) -> String {
    fs::read_to_string(root().join("tests/fixtures/licenses").join(name)).unwrap()
}

#[test]
fn the_definitions_are_well_formed() {
    let known_perm: HashSet<_> = ["commercial-use", "modifications", "distribution", "private-use", "patent-use"].into();
    let known_cond: HashSet<_> = ["include-copyright", "include-copyright--source", "document-changes", "disclose-source", "network-use-disclose", "same-license", "same-license--file", "same-license--library"].into();
    let known_lim: HashSet<_> = ["liability", "warranty", "trademark-use", "patent-use"].into();

    let mut ids = HashSet::new();
    for (p, text) in raw_definitions() {
        let v: Value = serde_json::from_str(&text).unwrap();
        let id = v["id"].as_str().unwrap();
        assert!(ids.insert(id.to_string()), "duplicate id {id}");
        assert_eq!(p.file_stem().unwrap().to_str().unwrap(), id.to_lowercase(), "file name should be the lower-case id");
        for key in ["name", "category", "summary", "url"] {
            assert!(v[key].as_str().is_some_and(|s| !s.is_empty()), "{id}: {key} is required");
        }
        assert!(v["url"].as_str().unwrap().starts_with("https://"), "{id}: url must be https");
        for (key, known) in [("permissions", &known_perm), ("conditions", &known_cond), ("limitations", &known_lim)] {
            for tag in v[key].as_array().unwrap() {
                assert!(known.contains(tag.as_str().unwrap()), "{id}: unknown {key} tag {tag}");
            }
        }
        // Fingerprints must survive normalisation unchanged (i.e. be findable in normalised text).
        for f in v["matchAll"].as_array().unwrap() {
            assert!(!normalize(f.as_str().unwrap()).is_empty(), "{id}: empty fingerprint");
        }
    }
    assert!(ids.len() >= 20, "expected 20+ licenses, found {}", ids.len());
}

#[test]
fn real_license_texts_are_identified() {
    let d = defs();
    let cases = [
        ("Apache-2.0.txt", "Apache-2.0"),
        ("BSD.txt", "BSD-3-Clause"),
        ("CC0-1.0.txt", "CC0-1.0"),
        ("GPL-2.txt", "GPL-2.0"),
        ("GPL-3.txt", "GPL-3.0"),
        ("LGPL-2.1.txt", "LGPL-2.1"),
        ("LGPL-3.txt", "LGPL-3.0"),
        ("MPL-2.0.txt", "MPL-2.0"),
    ];
    for (file, expected) in cases {
        assert_eq!(detect(&d, &fixture(file)), Some(expected), "{file}");
    }
}

#[test]
fn this_projects_own_license_and_its_bundled_fonts_are_identified() {
    let d = defs();
    let read = |p: &str| fs::read_to_string(root().join(p)).unwrap();
    assert_eq!(detect(&d, &read("../LICENSE")), Some("MIT"));
    assert_eq!(detect(&d, &read("../app/Assets/Fonts/OFL-Inter.txt")), Some("OFL-1.1"));
    assert_eq!(detect(&d, &read("../app/Assets/Fonts/OFL-JetBrainsMono.txt")), Some("OFL-1.1"));
}

#[test]
fn short_licenses_are_told_apart() {
    let d = defs();
    let mit = "MIT License\n\nCopyright (c) 2024 Someone\n\nPermission is hereby granted, free of charge, to any person obtaining a copy\nof this software and associated documentation files (the \"Software\"), to deal\nin the Software without restriction...\n\nThe above copyright notice and this permission notice shall be included in all\ncopies or substantial portions of the Software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND";
    assert_eq!(detect(&d, mit), Some("MIT"));
    // Same text without the notice requirement is MIT-0, not MIT.
    let mit0 = mit.replace("The above copyright notice and this permission notice shall be included in all\ncopies or substantial portions of the Software.\n\n", "");
    assert_eq!(detect(&d, &mit0), Some("MIT-0"));

    let isc = "ISC License\n\nCopyright (c) 2024, Someone\n\nPermission to use, copy, modify, and/or distribute this software for any\npurpose with or without fee is hereby granted, provided that the above\ncopyright notice and this permission notice appear in all copies.";
    assert_eq!(detect(&d, isc), Some("ISC"));
    let bsd0 = isc.replace(", provided that the above\ncopyright notice and this permission notice appear in all copies", "");
    assert_eq!(detect(&d, &bsd0), Some("0BSD"));

    let bsd2 = "Redistribution and use in source and binary forms, with or without\nmodification, are permitted provided that the following conditions are met:\n\n1. Redistributions of source code must retain the above copyright notice, this\n   list of conditions and the following disclaimer.\n\n2. Redistributions in binary form must reproduce the above copyright notice,\n   this list of conditions and the following disclaimer in the documentation.";
    assert_eq!(detect(&d, bsd2), Some("BSD-2-Clause"));

    assert_eq!(detect(&d, "This is free and unencumbered software released into the public domain."), Some("Unlicense"));
    assert_eq!(detect(&d, "            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE\n Version 2"), Some("WTFPL"));
    assert_eq!(detect(&d, "Boost Software License - Version 1.0 - August 17th, 2003"), Some("BSL-1.0"));
    assert_eq!(detect(&d, "GNU AFFERO GENERAL PUBLIC LICENSE\n Version 3, 19 November 2007"), Some("AGPL-3.0"));
    assert_eq!(detect(&d, "Eclipse Public License - v 2.0"), Some("EPL-2.0"));
    assert_eq!(detect(&d, "Creative Commons Attribution 4.0 International Public License"), Some("CC-BY-4.0"));
    assert_eq!(detect(&d, "Attribution-ShareAlike 4.0 International\n\nCreative Commons Attribution-ShareAlike 4.0 International Public License"), Some("CC-BY-SA-4.0"));
    assert_eq!(detect(&d, "This software is provided 'as-is', without any express or implied warranty. ... The origin of this software must not be misrepresented"), Some("Zlib"));
}

#[test]
fn ordinary_text_and_unknown_licenses_are_not_misidentified() {
    let d = defs();
    assert_eq!(detect(&d, "# My project\n\nThis project is licensed under the MIT License. See LICENSE."), None);
    assert_eq!(detect(&d, "All rights reserved. You may not copy this."), None);
    assert_eq!(detect(&d, ""), None);
    // A GPL text must not be reported as a lesser or affero variant, and vice versa.
    assert_ne!(detect(&d, &fixture("LGPL-3.txt")), Some("GPL-3.0"));
    assert_ne!(detect(&d, &fixture("GPL-3.txt")), Some("LGPL-3.0"));
}
