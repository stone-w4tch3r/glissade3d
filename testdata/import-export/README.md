# Import/export test files

Copy one or more of these into `PluginData/Glissade3D/routes/`, then press
**Read routes from disk** under **Advanced** in the mod.

| File | Expected result |
|---|---|
| `single-route.cfg` | one route, `import-single` |
| `multi-route.cfg` | two routes, `import-multi-one` and `import-multi-two`; the file is renamed `.imported` |
| `zz-duplicate-id.cfg` | skipped: its id equals `single-route.cfg`; only `import-single` remains |
| `conflict-same-name.cfg` | second route named `import-single`; on first save its file becomes `import-single-2.cfg` |
| `navutilities-runway.cfg` | read-only route `TEST_18` near KSC, imported from the classic `Runway` format |

The multi-route file proves the single-file compatibility path: it is read,
split into one file per route, and archived so it cannot resurrect deleted
routes on the next start.
