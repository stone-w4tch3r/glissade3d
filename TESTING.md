# Testing Glissade3D

## Before the first launch

1. Copy `GameData/Glissade3D` into the game's `GameData`.
2. No dependencies to install.
3. Launch the game and enter flight.

## Log checks

Search `KSP.log` for `Glissade3D`:

| Line | Meaning |
|---|---|
| `settings loaded: angle=...` | settings file read |
| `user routes loaded: N` | our `routes.cfg` parsed |
| `routes: N (M import, K user)` | merged route list |
| `body Kerbin: N routes` | routes for the current body |
| `shader=KSP/Unlit colorProperty=True` | unlit shader resolved |
| `launcher button added` | toolbar button registered |
| `gates rebuilt: N` | gate chain created |
| `geometry: dme=...` | first geometry sample in flight |
| `SMOKE] DONE failures=0` | headless self-test passed (only with `GLISSADE_SMOKE=1`) |

Any exception whose stack names `Glissade3D` means the mod is not working.
KSP-internal exceptions in `-nographics` are normal.

## In-game checks

| Check | Expected |
|---|---|
| Window on scene load | closed; opens only from the toolbar icon |
| `Close` button | closes the window; the icon state follows |
| Language | Russian UI is readable Cyrillic, no `Ð...` mojibake |
| Route list | built-ins plus your routes; `*` marks read-only |
| Built-in row | Edit and Delete disabled; Copy works |
| Double-click a row | opens the editor for that route |
| New route | opens in point + angle mode with the defaults |
| `+` / `-` | one click = one step; holding repeats after 0.45 s |
| Numeric fields | typing is kept while focused; values apply on Enter/blur |
| Two points | A and B markers, `Here` / `Click`, live recompute |
| Drag A or B | marker follows; fixed plane; altitude clamps to terrain + 0.5 m |
| `Ctrl+Z` in two points | route and point A both return to the previous state |
| Marker | arrow tip on the point, plumb line, cross with four arrowheads |
| Marker size | readable at 100 m and still visible at 30 km |
| Delete | press `Del` on one row, then select another; the question names the first route |
| Validation | a bad profile blocks Apply and lists the reason |
| `> 201 gates` | `too many gates` error; Apply is disabled |
| Apply / Undo | Apply disabled on errors; Undo disabled on an empty stack |
| Close with changes | asks before discarding |
| Gates key | typing `y` in a name field does not toggle gates |
| Rebind key | modifiers (Alt/Ctrl/Shift) are ignored |
| Pause | time stops while the editor is open and restores on exit |
| Settings | angle, window position and key survive a scene change and restart |
| Window position | both windows stay reachable after a resolution change |
| Save round-trip | Apply, leave to the menu, return: the route is still listed |
| Gate orientation | frames perpendicular to the descent path |
| IVA | gates visible through cockpit windows, not over the interior |
| Terrain | gates behind a hill are hidden |

## Failure signs

| Symptom | Likely cause |
|---|---|
| No gates, log lists routes | `Show gates` off, or vessel on another body |
| Gates on the wrong route | manual selection pinned; press `Auto (nearest)` |
| Apply does nothing | validation errors; read the list under the buttons |
| `too many gates` | spacing too small or far distance too large |
| Empty route list after restart | `routes.cfg` unreadable; check the log, keep `routes.cfg.bak` |
| `shader=none` | unlit shader missing; report the log line |
| Window does not open | toolbar icon missing; check the launcher line |

## Report

Send the `Glissade3D` lines from `KSP.log` and a screenshot of the approach.

## Rollback

Delete `GameData/Glissade3D` and `PluginData/Glissade3D`.
