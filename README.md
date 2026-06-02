# Earth & Beyond emulator preservation project

> A consolidated, modernised home for the Earth & Beyond MMO server emulator. The goal is to keep the game playable on contemporary hardware; Linux server, Linux or Windows client; and bring the codebase forward enough that contributors can actually work on it again.

## Quick Start

```
just play-local
just seed-account testclient testpw
```

Then click **Play** in LaunchNet7 and log in with username `testclient` / password `testpw`.

## What this is

Westwood's *Earth & Beyond* (2002) was shut down by EA in 2004. A community team at Net-7 Entertainment reverse-engineered the server protocol and built an open emulator in C++. That code split into multiple forks and drifted; the C# content editors lived in one repo, the server fork with the latest gameplay code lived in another, and a Linux client installer lived in a third.

This project is **one repo** that consolidates:

| Upstream | Lives in | What it brought |
|---|---|---|
| **tada-o fork** of Net-7 server (svn r2974, 2010-03-15) | `server/`, `login-server/`, `proxy/`, `launcher/`, `client/detours/`, `client/mods/`, `db/mysql/` | Newer/more complete C++ server (~162K LOC), the MySQL schema + seed data, ~20 ability implementations that other forks only had stubs for |
| **kyp snapshot** (older Net-7 snapshot, 2014 GitHub dump) | `tools/`, `archive/kyp-snapshot/` | Full C# editor suite (Sector, Mob, Mission, Faction, Item, Effect, TalkTree editors plus Station Tools, EnBPatcher, LaunchNet7, W3D Parser, etc.), the original Net-7 architecture documentation, packet captures, the historical Linux-port attempt |
| **enb-linux-installer** | `client/linux-installer/` | A GPLv3 bash script that automates installing and configuring the Windows client under WINE on Linux distros |

These projects it's based on are super old code but the Net-7 current codebase is private and otherwise inaccessible for extending, so this is the best I can do for now. If more modern code for that was released I'd be happy to build on it.

## Project status

Tracked in `plans/*.md`. 

## Quickstart

### Linux client (works today)

```
client/linux-installer/install-enb-linux.sh
```

Installs WINE + the Earth & Beyond client + the Net-7 launcher. See `client/linux-installer/README.md` for prerequisites and supported distros.

### Server (runs natively on Linux today)

```
just init         # first-run: bring up mysql:8.0 on :3307 and load both dumps
just dev          # docker-compose up server + proxy + login (in the background)
just build        # cmake build server + proxy + login, dotnet build tools
just test         # ctest + dotnet test
just package      # build OCI image of the server
```

`server`, `proxy`, and `login-server` all build clean against system OpenSSL 3.x and libpqxx 7.x, and pass the gtest suite plus the CLI-driven integration tests (33/33). See `docs/09-running-locally.md` for the walkthrough.

### C# content tools

```
dotnet build tools/Net7Tools.slnx
just launch                   # central Avalonia tool launcher (recommended)
just launch-sector-editor     # or jump straight to a specific editor
```

The Phase L Avalonia ports (`tools/<name>-avalonia/`) run natively on Linux — no WINE. The legacy WinForms ports (`tools/<name>/`) still cross-compile but only run on Windows / WINE. `tools/itemeditor/` is the only un-ported editor (no upstream csproj). See `tools/README.md` for the per-tool table.

### CLI packet inspector

`tools/cli-client/` is a headless C# REPL that drives the game protocol and decodes every S2C packet into structured fields with per-byte annotation (green background = decoded, orange = unknown).

**Live packet tail against a running stack:**

```bash
just dev                # bring up server + proxy + login in the background
just launch-cli         # open the REPL prompt

# inside the REPL:
dump-on                 # start printing every packet as it arrives, in colour
connect                 # connect to the server
login <user> <pass>     # authenticate
list                    # list characters on this account
enter <firstname>       # enter sector -- dumps the full handshake live
dump-off                # stop the tail
```

Each packet prints like:

```
<-- 0x0025 ItemBase  len=304
  [0000] ItemTemplateID    = 0x000021D1  (8657)  (BE)
  [0004] Category          = 10  (Weapon)
  [0010] FieldCount        = 12
  [0011]   Field[0].ID     = 11  Requires Level
  [0015]   Field[0].Value  = "Beam Weapon"
  ...
  [00AE] Flags             = 0x00000080  (128)  (NO_MANUFACTURE)
  [00B2] Name              = "Prototype B 8"
  [0094] ActEffects.Count  = 0  (BE)
  0000  00 00 21 D1 0A ...   <- green bytes decoded, orange bytes unknown
```

**Offline capture dump (no live server needed):**

```bash
just cli-replay                   # replay archive/replay/capture_1-sector-s2c.bin
just cli-replay capture_2         # replay a different capture file

# plain-text mode for piping:
NO_COLOR=1 just cli-replay | less

# filter to one opcode:
NO_COLOR=1 just cli-replay | grep -A 20 "ItemBase"

# count frames per opcode:
NO_COLOR=1 just cli-replay | grep "^#" | sed 's/ len=.*//' | sort | uniq -c | sort -rn
```

The `capture_1` file is the 101-frame retail S2C trace from `archive/replay/` (avatar "Ace", ship "Revenge of the Jenquai", sector 45151 Friendship 7). Every structured record class was verified byte-by-byte against it.

**Capturing and comparing a live session:**

```bash
# record a live session to a text file
NO_COLOR=1 just launch-cli | tee my-session.txt
# inside: dump-on, connect, login, enter, quit

# diff decoded fields from retail capture vs live server:
NO_COLOR=1 just cli-replay | grep "^\s*\[" > retail.txt
grep "^\s*\[" my-session.txt             > live.txt
diff retail.txt live.txt
```

See `docs/15-cli-client.md` for the full REPL command reference.

Or if you want to manually dump network traffic to compare:

```bash
ps aux | grep whatever-you-want-to-capture
sudo nsenter -t <PID> -n tcpdump -i any -nn -s0 -w network-capture.pcap
```

Dumping the proxy is a good idea as it's unencrypted. You can convert to hex with hexdump -C

**Replaying a raw pcap capture (server->proxy UDP traffic):**

The Net-7 server sends sector S2C data as 0x2016/0x201A PACKET_SEQUENCE UDP frames.
`pcap_to_replay.py` extracts the inner game opcodes from those frames and writes an
ENBREPLAY binary that the CLI replay tool can parse:

```bash
# Convert a pcap to ENBREPLAY and replay it in one step:
just pcap-replay proxy/local-debug/foo.pcap

# With custom IPs (default: 216.219.87.147 -> 192.168.0.150):
just pcap-replay proxy/local-debug/foo.pcap 10.0.0.1 10.0.0.2

# Or run the converter directly to produce a persistent .bin:
python3 tools/pcap-to-replay/pcap_to_replay.py \
    --pcap proxy/local-debug/foo.pcap \
    --out  /tmp/foo.bin \
    --server 216.219.87.147 --client 192.168.0.150 --verbose

# Then replay the .bin at any time:
printf 'replay /tmp/foo.bin\nquit\n' | \
    dotnet run --project tools/cli-client/src/CliClient.App -- start
```

The pcap must be a standard LE pcap (hexdump -C or wireshark export).
Only the UDP flows from server to client are extracted; RC4-encrypted
auth traffic and launcher opcodes are automatically skipped.

### Testing with multiple players

The `just launch-cli` REPL above dials the host-published proxy on `127.0.0.1`,
so it shares that proxy with the WINE `client.exe` started by `just play-local`.
The Net7Proxy is a **single-client bridge** (one connection set, one logged-in
state), so two clients through the same proxy clobber each other -- one
disconnects the other. To run several clients at once, give each its **own
proxy** in a container.

```bash
just run-stack-bg        # shared stack: postgres + server + login + proxy

# each `play-cli` brings up a CLI + its own dedicated proxy on a private
# network, against the running stack. Run as many as you like; the UNIT arg
# names the container set so they don't collide.
just play-cli cli1       # first player
just play-cli cli2       # second player (in another terminal)

# inside each REPL:
connect cliproxy         # dial THIS unit's own proxy (not 127.0.0.1)
login <user> <pass>      # authenticate
enter <firstname>        # enter sector
```

This composes freely: `just play-local` (the WINE client on the shared proxy)
can run at the same time as any number of `just play-cli` units, because each
has a proxy to itself.

Tear a unit's proxy down when finished:

```bash
just stop-cli cli1
```

One catch: the server force-kicks a duplicate login **per account** (this is
correct retail behaviour and is deliberately not bypassed). Give each
concurrent client a **distinct account**, not the same login twice.

See `docs/15-cli-client.md` for the REPL command reference and
`docker-compose.cli.yml` for the per-unit topology.

## Repo layout

See `CLAUDE.md` for the full directory map and rules. Short version:

- `server/`, `login-server/`, `proxy/`, `launcher/` - C++ server-side
- `client/` - Linux installer + client mods + Detours
- `tools/` - C# content editors
- `db/` - MySQL dumps (original) + Postgres schema (converted)
- `docs/` - architecture, protocol, modules, schema, abilities, tools, build, running, roadmap
- `plans/` - multi-phase plan files (source of truth for what's done/next)
- `archive/` - historical material from upstream repos that didn't make it into the active tree
- `LICENSES/` - license texts and the directory-by-directory license map

## License

**Non-commercial only.**

The project default license is **Creative Commons Attribution-NonCommercial-ShareAlike 3.0 United States** because the bulk of the inherited code (the Net-7 server) is under that license and we can't relicense it.

- `LICENSES/enb-emulator` - project default (CC BY-NC-SA 3.0)
- `LICENSES/Net7` - original Net-7 license header + deed URL
- `LICENSES/Tada-O` - note that tada-o adds no separate license; modifications inherit CC BY-NC-SA 3.0 under ShareAlike
- `LICENSES/enb-linux-installer` - GPLv3 verbatim (governs only `client/linux-installer/`)

Precedence: per-file header > per-folder `LICENSE` > project default. See `LICENSES/README.md` for the full directory-by-directory map.

## Credits

- **Net-7 Entertainment** (2005–2009) - the original team that built the emulator from reverse-engineered protocol work. None of this exists without them.
- **The tada-o contributors** - the post-Net-7 fork that landed the abilities, guild, and combat work consolidated here.
- **kyp / therealkyp** - the 2014 GitHub snapshot that preserved the C# editor suite, packet captures, and architecture docs.
- **Nimsy** - the original WINE-on-Linux guide whose steps became the basis of the installer.
- **ciphersimian** - author of the `enb-linux-installer` script.
- Westwood Studios - *Earth & Beyond* (2002, o7).

## Contributing

Read `CLAUDE.md` first. It explains the plans workflow, repo layout, coding rules, and license precedence. Then look at `plans/00-master.md` to see what's in progress.
