# LuminaIDE: working notes

## Changelog: every change gets an entry

Any change to behaviour, looks, build, packaging or docs that a user or contributor could notice gets a line in
`CHANGELOG.md` under `## [Unreleased]`, in the same commit as the change. Small fixes and early or experimental work
count too. Skip only pure refactors with no visible effect and typo fixes.

- Group under `### Added`, `### Changed`, `### Fixed`, `### Removed` or `### Known limitations`.
- One bullet per change, plain language, starting with what the user sees. Bold the feature name for larger items.
- The welcome screen's "What's new" card shows the first five bullets of the latest release, so put the most important first.
- At release time, follow `docs/RELEASING.md`: move `Unreleased` into a dated version, bump the versions, tag.
- If a change adds to `docs/website/v0-changelog-prompt.txt` data, regenerate it from `CHANGELOG.md`.
