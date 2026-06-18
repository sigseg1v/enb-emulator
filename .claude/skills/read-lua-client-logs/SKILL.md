---
name: read-lua-client-logs
description: Read the logs of a running Earth & Beyond dev session -- the client-side enbmod Lua log (enbmod.log) and the server-side docker logs (server, proxy, login). Use when debugging the live game: checking enbmod startup/hook status, seeing `/run` or enbmod.cmd output, or correlating client behaviour with server/proxy errors after `just play-local`.
---

# Read the live session logs

A `just play-local` session has logs on two sides of the proxy. Know which side a
symptom lives on before chasing it.

## Client side: enbmod.log (the Lua / mod log)

Written by the injected enbmod DLL next to the staged client exe. Holds: enbmod
init, hook install results (`event hooks enabled`, `hook RpgLevels failed`, ...),
every `enb.log(...)` from a mod, and all `[run] ...` console/`enbmod.cmd` output.

Resolve the path from the launcher settings (there can be more than one WINE
prefix -- do not hardcode):

```bash
SETTINGS="/data/dev/enb-emulator/tools/LaunchFreya/bin/Debug/net10.0/FreyaLauncher.settings.json"
DIR=$(python3 -c "import json,os;print(os.path.dirname(json.load(open('$SETTINGS'))['ClientPath']))")

tail -n 50 "$DIR/enbmod.log"          # recent activity
# follow it live:
tail -f "$DIR/enbmod.log"
```

What to look for:
- `event hooks enabled` / `partially enabled` -- whether the skill/chat/RPG hooks
  installed. `partially enabled` + a `hook X failed` line means an address is wrong.
- `[run] ...` -- results of `/run` or commands sent via the run-lua-client-command
  skill. `[run] error: ...` / `[run] compile error: ...` are failures.
- `<mod> loaded` -- each HUD/mod script that initialised.
- Wine input-handler noise (Win32MouseHandlerClass / ShowCursor) is NOT progress;
  ignore it.

The launcher's **Advanced... -> Mods** tab shows this same file, and its
**Auto-refresh** checkbox re-reads it every 5s for live tailing.

## Server side: docker logs (server / proxy / login)

The server, proxy and login run in docker (only postgres is up until
`just play-local` brings the rest up). Tail them with the justfile helper or
docker compose directly:

```bash
just logs server          # or: proxy | net7go | postgres | freya-online
# raw, several at once, follow:
docker compose logs -f server proxy net7go
# last N without following:
docker compose logs --tail 100 server
```

Per the project rule, treat any `Error` / `WARNING` / `FATAL` / `failed` /
exception / SQL error in these as a real defect until proven harmless -- not boot
noise.

## Which side?

- Mod / HUD / Lua / `/run` / memory-read behaviour -> **enbmod.log** (client).
- Login, sector handoff, opcode handling, DB, "object didn't spawn" -> **docker
  logs** (server/proxy). Remember the proxy re-frames/consumes/fabricates packets,
  so a server log line is not necessarily what the client received.

Related: run-lua-client-command (to drive Lua into the client and see its output
land in enbmod.log).
