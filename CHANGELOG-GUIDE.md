# Maintain the changelog

Edit [CHANGELOG.md](CHANGELOG.md) by hand. Follow
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Write short changes
that matter to players; do not copy Git logs or list routine refactors.
Keep the mod's four-part version numbering.

## During development

Add bullets under `## [Unreleased]`. Group them under `### Added`, `### Changed`,
`### Deprecated`, `### Removed`, `### Fixed` or `### Security` as needed.
Omit empty categories. Describe the resulting behavior, not the code edits.

## Before a release

1. Choose the next version and finish local tests.
2. Move the pending bullets under `## [VERSION] - YYYY-MM-DD`, newest first.
   Leave an empty `## [Unreleased]` above the new release. Use the release date.
3. Update `GameData/Glissade3D/Glissade3D.version` and `src/AssemblyInfo.cs`
   to the same version. Finalize the dated changelog section before tagging.
   Branch builds preview Unreleased entries, which can be empty.
4. Preview the notes from the mod folder:

   ```bash
   python3 release_notes.py v0.3.4.0
   python3 -m unittest discover -s tests -p 'test_*.py' -v
   ```

   Replace the example version with your release version. Windows users can
   use `python` instead of `python3`.

5. Commit privately, synchronize, and run the release helper from the private
   KSP repository root:

   ```bash
   bash ./sync-glissade3d.sh --publish "Prepare Glissade3D 0.3.4.0"
   bash ./release-glissade3d.sh v0.3.4.0
   ```

The helper refuses missing, duplicate, undated or empty release sections.
CI repeats these checks and uploads `RELEASE_NOTES.md` beside the ZIP in the
Actions artifact. A pushed version tag publishes that exact section as the
GitHub release body. Unreleased and older entries are excluded. The workflow
reads the changelog from the built commit, not from a later checkout of `main`.
The notes file is not included inside the installable ZIP.

## Correct published wording

Correct the matching section in the source changelog and commit it. Update
the existing GitHub release body from that section. Do not move the release
tag or replace the binary ZIP to fix prose. This is a deliberate documentation
correction; normal future releases read notes from their own tagged commit.
