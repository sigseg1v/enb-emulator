# LICENSES directory

The Earth & Beyond emulator preservation project combines code from multiple upstream projects with different licenses. This directory holds each license verbatim and documents which one applies where.

## Precedence (top wins)

1. **Per-file header** — e.g. every Net-7 `.cpp`/`.h` carries a CC BY-NC-SA 3.0 header. Never strip it.
2. **Per-directory `LICENSE`** — e.g. `client/linux-installer/LICENSE` (GPLv3) governs the files in that directory.
3. **Project default** — `enb-emulator` in this directory, which we have set to CC BY-NC-SA 3.0 because the bulk of the codebase is Net-7 under that license.

## License map (directory → governing license)

| Directory | License | Lives in |
|---|---|---|
| `server/`              | CC BY-NC-SA 3.0 (per-file headers + project default) | `Net7`, `enb-emulator` |
| `server/third_party/`  | Per-vendor (each subdir keeps its upstream LICENSE)  | vendor's own |
| `login-server/`        | CC BY-NC-SA 3.0                                       | `Net7` |
| `proxy/`               | CC BY-NC-SA 3.0                                       | `Net7` |
| `launcher/`            | CC BY-NC-SA 3.0                                       | `Net7` |
| `client/detours/`      | Microsoft Research Detours license (per-file)         | per-file |
| `client/mods/`         | CC BY-NC-SA 3.0                                       | `Net7` |
| `client/linux-installer/` | GPLv3                                              | `enb-linux-installer` + `client/linux-installer/LICENSE` |
| `tools/`               | CC BY-NC-SA 3.0 (per-file headers where present; no separate file from tada-o) | `Net7`, `Tada-O` |
| `db/mysql/`            | CC BY-NC-SA 3.0                                       | `Net7` |
| `db/postgres/`         | CC BY-NC-SA 3.0 (derived schema)                      | `enb-emulator` |
| `docs/`                | CC BY-NC-SA 3.0                                       | `enb-emulator` |
| `plans/`               | CC BY-NC-SA 3.0                                       | `enb-emulator` |
| `archive/kyp-snapshot/`| Per-file headers where present; otherwise CC BY-NC-SA 3.0 | `Net7` |
| New code we add        | CC BY-NC-SA 3.0                                       | `enb-emulator` |

## License files

| File | Covers |
|---|---|
| `enb-emulator` | Project default. CC BY-NC-SA 3.0 with precedence rules. |
| `Net7` | Original Net-7 Entertainment server code license — CC BY-NC-SA 3.0 header + deed URL. |
| `Tada-O` | Statement that tada-o fork added no separate license; modifications inherit Net-7 under ShareAlike. |
| `enb-linux-installer` | GPLv3 verbatim — governs `client/linux-installer/`. |

## Practical implications

- **Non-commercial only.** The NC clause means you cannot run a paid server, sell mods, charge for access, or otherwise commercialise this project or any derivative. This applies to the *combined* work regardless of which subset you redistribute.
- **ShareAlike.** Any derivative work must be released under CC BY-NC-SA 3.0 (or a later compatible CC license).
- **Don't relicense.** No one in this project is authorised to relicense Net-7's code; only Net-7 Entertainment can. Same with the GPLv3-licensed installer script — the upstream author retains that copyright.
- **Don't strip headers.** Per-file license headers are load-bearing; tools that auto-format must preserve them.
