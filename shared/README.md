# Shared Glissades

Approach trajectories made by Glissade3D users.

| Trajectory | Body | Folder |
|---|---|---|
| bridge | Kerbin | [bridge](bridge/) |
| danger-on-island | Kerbin | [danger-on-island](danger-on-island/) |
| danger-on-island³ | Kerbin | [danger-on-island³](<danger-on-island^3/>) |

## Install a shared trajectory

## Recommended

Install via **Install shared trajectories** button under **Advanced**


# Manual

1. Copy the trajectory's `.cfg` file into `PluginData/Glissade3D/routes/`.
2. Start KSP, or press **Read trajectories from disk** under **Advanced** in the mod.
3. The trajectory appears in the list for its body.

## Share your own

1. Copy the trajectory file from `PluginData/Glissade3D/routes/`; its name follows
   the trajectory name.
2. Create `shared/<kebab-case-name>/` and save the file there as
   `<trajectory-name>.cfg`.
3. Optionally add a short `README.md` and a screenshot.
4. Add a row to the table above and open a pull request.

One trajectory per folder, no changes to other people's folders.

## Maintainers

Regenerate `index.cfg` after changing a trajectory file, then commit both:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-shared-index.ps1
```
