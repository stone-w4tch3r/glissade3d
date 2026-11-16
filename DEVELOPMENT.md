# Development

## Scope

This document explains how to build, test and install Glissade3D.
This document is for developers only.

## Requirements

- Windows with PowerShell 5.1 or later.
- Kerbal Space Program 1.12.5.
- Internet access for the first build.

## Folder layout

| Path | Content |
|---|---|
| `src` | C# sources |
| `tests` | Test programs |
| `GameData\Glissade3D` | Files to ship |
| `dist` | Build result. Do not commit. |

## Set the game folder

Set the environment variable `KSP_DIR`:

```
setx KSP_DIR "C:\Program Files\Kerbal Space Program"
```

Open a new terminal after this command.
You can also pass `-Ksp <path>` to each script.

## Get the compiler

The build needs a Roslyn compiler.
Run this command one time:

```
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Fetch
```

The script downloads Roslyn to `.tools`.
The script does not change the system.
If a compiler is already available, set `ROSLYN_CSC` to the full path of `csc.exe`.

## Build

1. Open a terminal in this folder.
2. Run:

```
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

The script checks the game files, the sources and the version.
The script writes the result to `dist\GameData\Glissade3D`.
The script deletes `dist` before each build. Do not edit files in `dist`.

## Install for development

1. Close KSP.
2. Run:

```
powershell -NoProfile -ExecutionPolicy Bypass -File install-dev.ps1
```

The script replaces `GameData\Glissade3D` in the game folder.
The script stops if KSP is running.

## Test

Run:

```
powershell -NoProfile -ExecutionPolicy Bypass -File test.ps1
```

The script builds and runs the math tests and the config tests.
The config tests read `GameData\Glissade3D\DefaultRunways.cfg`.

## Change the version

Change these two files:

- `GameData\Glissade3D\Glissade3D.version`
- `src\AssemblyInfo.cs`

The build stops if the versions differ.

## Make a release package

Run:

```
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Package
```

The script writes `dist\Glissade3D-<version>.zip`.
The ZIP contains the `GameData` folder.

## Automated builds and releases

Each push to public `main` builds a ZIP and uploads it as the `Glissade3D`
Actions artifact. You can also select **Run workflow** on the Actions page.
These runs do not publish a release.

CI uses Windows Server 2022, Roslyn 4.14.0 and pinned, checksum-verified
[KSP 1.12.5 reference assemblies](https://github.com/KSPModdingLibs/KSPLibs).
It compiles the mod, runs pure math tests against real .NET Framework, checks
the assembly version and verifies the ZIP file list and non-empty files.
Reference assemblies contain no game method bodies: run the full `test.ps1`
suite and flight checks locally with your installed KSP.

To publish a release, sync the tested source and push an annotated `v<version>`
tag on the public commit. The tag must match both version files. The tagged
commit must belong to public `main`. Successful tag builds publish the ZIP
directly as a GitHub release, with the matching section from `CHANGELOG.md`.
Manual runs never publish, even when you select a tag.

Use a new version for each release; do not move published tags or replace
published ZIPs.

Check the first CI-built ZIP in KSP once. Repeat this check when compiler,
references or packaging change. Normal gameplay checks remain local.

## Maintain the changelog

Edit [CHANGELOG.md](CHANGELOG.md) by hand using
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Describe player-visible
changes. Keep four-part versions.

Add bullets under `## [Unreleased]`. Use Added, Changed,
Deprecated, Removed, Fixed or Security subheadings; omit empty categories.

Release:

1. Test locally. Move pending bullets under `## [VERSION] - YYYY-MM-DD`,
   newest release first. Leave an empty Unreleased section above it.
2. Match the version in `Glissade3D.version` and `AssemblyInfo.cs`.
3. From the mod folder, substitute your version:

   ```powershell
   .\release-notes.ps1 -Version v0.3.4.0
   .\tests\ReleaseNotesTests.ps1
   ```

   Use Windows PowerShell 5.1 or PowerShell 7. On macOS, run scripts with
   `pwsh -NoProfile -File ./release-notes.ps1 -Version v0.3.4.0`.

The helper and CI reject missing, duplicate, undated or empty release sections.
Branch builds preview Unreleased entries. CI uploads `RELEASE_NOTES.md` beside
the ZIP; tag releases publish only their matching section from the built commit.

For wording corrections, commit the corrected changelog and update the existing
GitHub release body. Keep its tag and ZIP unchanged.

## Rules

- Do not commit `dist`.
- Do not commit `.tools`.
- Keep the two version values equal.
- Update `README.md` and `TESTING.md` after a behaviour change.
