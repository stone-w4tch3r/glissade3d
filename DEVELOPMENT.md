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

## Rules

- Do not commit `dist`.
- Do not commit `.tools`.
- Keep the two version values equal.
- Update `README.md` and `TESTING.md` after a behaviour change.
