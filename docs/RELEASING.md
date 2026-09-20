# Releasing

1. Move the completed changes from `Unreleased` to a new version/date in `CHANGELOG.md`.
2. Update `<Version>` in `app/LuminaIDE.csproj`, the version in `core/Cargo.toml`, and regenerate `core/Cargo.lock` with Cargo.
3. Push a matching tag, for example `git tag v0.3.0` then `git push origin v0.3.0`.
4. **Downloads** builds and tests five native targets: Linux x64/ARM64, macOS Intel/Apple silicon and Windows x64. Every package includes .NET, native libraries, extensions and licenses. Each archive has a SHA-256 sidecar.
5. Review the draft GitHub Release and publish it. Public downloads are available on the repository's Releases page after publication.

The tag must match the app version. `packaging/release-notes.sh 0.2.0` previews the notes for an existing version.

To build downloadable artifacts without a release, choose **Actions → Downloads → Run workflow** from any browser. This never publishes a release and works on forks. Artifacts remain available for 14 days. Runner labels follow [GitHub's supported runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

## Local packages

Run `./build.sh package` on Linux/macOS or `.\build.ps1 package` on Windows, or use **Create download** in the graphical Build Studio. The result is in `dist/`. See [BUILDING.md](BUILDING.md) for prerequisites and supported architectures.

Linux archives include a binary installer for app-menu integration. Windows archives include a binary installer for a Start-menu shortcut. Mac archives contain `LuminaIDE.app`, ready to move into Applications.

## Signing

macOS packages are ad-hoc signed on the runner, not Developer ID signed or notarized. Windows executables are unsigned. OS trust prompts are expected; do not instruct users to disable their security settings.

For distribution without these prompts, provision an Apple Developer ID certificate and notarization credentials, and a Windows code-signing certificate. Add signing before archiving. The macOS bundle ID is `com.animated.luminaide`.
