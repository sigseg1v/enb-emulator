# Client mod binaries (no source)

This directory contains the modded Earth & Beyond Windows client binary plus its authentication shim DLL. **We do not have source for these.** They are kept as-is because the Linux installer (`client/linux-installer/`) downloads the official client through Net-7's launcher rather than using these mods; these are the historical artefacts from the tada-o tree.

| File | Size | What it is |
|---|---|---|
| `release/client.exe` | ~8.3 MB | The patched Earth & Beyond client binary. Contains hooks/redirections needed by the emulator (originally produced by the Net-7 / tada-o team). |
| `release/authlogin.dll` | ~110 KB | Authentication shim loaded by the patched client; talks to the login server. |
| `release/launch.bat` | text | Launcher batch file. |
| `Data/client/mixfiles/EB_Sizzle.bik` | ~22 MB | Original Westwood game intro video (Bink Video). |

## Provenance

All of the above came from `/data/dev/tada-o-enb-fork/Source Code/Client Mods/` verbatim. The tada-o tree is itself an svn checkout of Net-7's repo at r2974 (2010-03-15) — these binaries were produced by the Net-7 team using internal tooling that was not part of the public source release.

## Use today

For Linux users we recommend `client/linux-installer/install-enb-linux.sh`, which installs the official Earth & Beyond client under WINE and pulls in Net-7's current launcher. The binaries here are mostly historical reference.
