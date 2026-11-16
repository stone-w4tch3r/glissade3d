# Shared routes test plan

## Setup

1. Install 0.3.4 in the main game.
2. Start the local catalog server from the mod folder:

   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/serve-shared.ps1 -Shared testdata/shared-site
   ```

3. In `PluginData/Glissade3D/settings.cfg` set:

   ```
   sharedBaseUrl = http%3A%2F%2F127.0.0.1%3A8765%2F
   ```

   The value is percent-encoded: `ConfigNode` treats `//` as the start of a
   comment, so a raw URL is truncated to `http:` when the file is re-read.
   Encode any URL with `[uri]::EscapeDataString("http://host:port/")`.

4. Start KSP and enter flight.

The test site has three entries: `import-single`, `import-second`, and
`import-bridge-conflict` (its id matches the real `bridge` route, so it must be
skipped).

## 1. Install

1. Open **Advanced**. The line shows `Shared routes: 0`.
2. Press **Install shared routes**.
3. Status: `2 installed, 0 updated, 1 skipped`.
4. `import-single` and `import-second` appear with `[S]` next to the name.
5. Your `bridge` is untouched; the conflicting catalog entry is skipped.

## 2. Reinstall

1. Press **Install shared routes** again.
2. Status: `0 installed, 0 updated, 3 skipped` (two up to date, one conflict).
3. No new files appear.

## 3. Edit and uninstall

1. Open `import-single`, change its name or heading, press **Apply**.
2. Press **Uninstall shared routes** and confirm.
3. Status: `Removed 1, kept 1 modified`.
4. `import-second` is gone; `import-single` remains as your own route and is no
   longer marked `[S]`; `bridge` and the skipped conflict are untouched.
5. `PluginData/Glissade3D/shared-installed.cfg` is empty.

## 4. Offline

1. Stop the server, press **Install shared routes**.
2. Status starts with `Shared routes: download failed`; no route changes.

## 5. Real catalog

After the public repository is published, set `sharedBaseUrl` to its
`shared/` raw URL and press **Install shared routes**. Routes whose ids already
exist in your installation are skipped; the rest are added.

## Expected log lines

| Line | Meaning |
|---|---|
| `shared install: installed=N updated=N skipped=N failed=N` | result |
| `shared uninstall: removed=N kept=N failed=N` | result |
| `shared: modified, kept: <name>` | edited route preserved |
| `shared: id conflict: <id>` | your route wins |

## Rollback

Remove the downloaded routes, restore `settings.cfg`, and stop the server.
