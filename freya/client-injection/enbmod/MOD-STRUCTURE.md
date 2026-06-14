<!-- SPDX-License-Identifier: MIT
     Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
     License: LICENSES/Freya -->

# Freya mod structure + distribution

This is the contract for how enbmod Lua mods are stored, packaged, shipped,
and updated. Adhere to it. The launcher (`tools/LaunchFreya`), the deploy
publisher (`deploy/do/scripts/Push-ClientPatch.ps1`), and the login server
(`login-server/Net7SSL` patcher manifest) all implement parts of it and MUST
stay in sync.

## 1. A mod is a folder with a `mod.json`

Repo source of our mods: `freya/client-injection/enbmod/scripts/mods/<id>/`.
Each mod folder contains a `mod.json` and its Lua files:

```json
{
  "id": "freya-hud",
  "name": "Freya HUD",
  "author": "Freya",
  "description": "One-line human description shown in Configure Mods.",
  "entrypoint": "init.lua",
  "dependencies": ["hide-ui"]
}
```

- `id` is the folder name and the stable identity of the mod. It is the only
  thing that decides "is this one of ours". It MUST match `[A-Za-z0-9._-]+`
  (no path separators) -- it is used as a directory name and a URL segment.
- `entrypoint` is the Lua file the loader `dofile`s. Shared library code lives
  in `scripts/lib/` (siblings on `package.path`), NOT under a mod folder.
- `dependencies` (optional) is an array of other mod `id`s this mod needs
  enabled to work fully. It is advisory, NOT enforced at load time: the loader
  still loads a mod whose deps are off, and each mod must tolerate a missing dep
  (e.g. `freya-hud` renders over the native bars when `hide-ui` is off rather
  than aborting). The launcher's Configure Mods window reads it and paints a
  mod's row red ("requires &lt;id&gt; (disabled/missing)") when the mod is
  enabled but a declared dependency is not. Omit or use `[]` for no deps.

## 2. Client-side layout: `./mods/<id>/` at the launcher location

On a player's machine, mods live in a persistent store next to
`FreyaLauncher.exe`: `<launcher-dir>/mods/<id>/`. Inside `<id>/` is the
unzipped mod (its `mod.json` + Lua files).

Two kinds of mod coexist here, distinguished ONLY by `id`:

- **Ours** -- an `id` that appears in the published manifest (see below). The
  launcher owns these: it will delete + re-extract the folder on update.
  Our folders additionally carry a `modhash` file (see 4).
- **The user's own** -- any `id` NOT in the manifest. The launcher never
  touches these. Users author mods directly in `./mods/<their-id>/`. They have
  no `modhash` and (by the no-collision rule) never share an id with ours, so
  the updater skips them entirely. We never break a user's mod.

At launch the launcher copies every ENABLED mod folder from `./mods/<id>/`
into the game's load location (`<client-dir>/scripts/mods/<id>/`), minus the
`modhash` bookkeeping file. Disabled mods (Configure Mods UI) are not copied
and are pruned from a previous launch's staging, so they are neither loaded
nor injected.

## 3. Deterministic folder hash (the version identity)

The version of a mod is a short, content-addressed hash of its folder,
computed at deploy time by `Push-ClientPatch.ps1`:

1. Enumerate every file under the mod folder recursively (excluding any
   `modhash` file).
2. For each file, take its path **relative to the mod folder**, normalized to
   forward slashes, and the lowercase-hex SHA-256 of its **contents**.
3. Sort the entries by relative path (ordinal / invariant, case-sensitive).
4. Concatenate `"<relpath>\n<contenthash>\n"` for every entry in order.
5. SHA-256 that string; the mod hash is the first **10** lowercase hex chars.

Properties this guarantees:

- **Deterministic**: same files with same contents -> same hash, on any
  machine, in any order on disk (step 3 sorts).
- **Timestamp-independent**: only contents are hashed, never mtimes.
- **Add/remove sensitive**: adding or removing a file changes the set of
  `relpath` lines, so the hash changes.

## 4. Packaging + upload (deploy)

For each mod folder in the repo, the deploy step:

- computes the hash (section 3),
- zips the folder CONTENTS into `<id>-<hash>.zip` (extract yields the files
  directly inside `<id>/`),
- uploads it to the patcher bucket under `mods/<id>-<hash>.zip`,
- records `{ "id": "<id>", "hash": "<hash>" }` in the `mods` array of
  `manifest.json`.

CloudFront invalidation invalidates the **exact** keys just uploaded
(`/mods/<id>-<hash>.zip`) plus `/manifest.json` -- never `/*` and never
`/mods/*`. A literal `*` can be re-expanded by a downstream shell into a local
glob and silently invalidate nothing; concrete keys cannot be mangled. New
zips carry the hash in their name so they were never cached under that key
anyway; superseded zips age out harmlessly.

## 5. Update check (client)

The launcher learns the authoritative mod set from the login server's
`/updateCheck` reply, which carries a `mods` array of
`{ id, hash, url }` (the login server synthesizes `url` from the manifest's
`{id,hash}` + the CloudFront base). For each entry:

- Read `<launcher-dir>/mods/<id>/modhash`.
- If it equals the manifest hash -> up to date, do nothing.
- Otherwise (mismatch or no `modhash`) the mod is an available update:
  download `<id>-<hash>.zip`, then **replace** the folder atomically --
  delete its contents, write `modhash` (= the hash, i.e. the `<hash>` suffix
  of the zip name), extract the zip over it.

Because the decision is purely `modhash` vs manifest hash keyed by `id`, a
user's own mod (unknown id, no `modhash`) is never selected for update. That
is the entire safety mechanism: **we only ever replace ids we own.**
