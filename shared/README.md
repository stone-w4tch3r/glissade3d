# Shared Glissades

Approach routes made by Glissade3D users.

| Route | Body | Folder |
|---|---|---|
| bridge | Kerbin | [bridge](bridge/) |
| danger-on-island | Kerbin | [danger-on-island](danger-on-island/) |
| danger-on-island³ | Kerbin | [danger-on-island³](<danger-on-island^3/>) |

## Install a shared route

## Recommended

Install via **Install shared routes** button under **Advanced**


# Manual

1. Copy the route's `.cfg` file into `PluginData/Glissade3D/routes/`.
2. Start KSP, or press **Read routes from disk** under **Advanced** in the mod.
3. The route appears in the list for its body.

## Share your own

1. Copy the route file from `PluginData/Glissade3D/routes/`; its name follows
   the route name.
2. Create `shared/<kebab-case-name>/` and save the file there as
   `<route-name>.cfg`.
3. Optionally add a short `README.md` and a screenshot.
4. Add a row to the table above and open a pull request.

One route per folder, no changes to other people's folders.

## Maintainers

Regenerate `index.cfg` after changing a route file, then commit both:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-shared-index.ps1
```
