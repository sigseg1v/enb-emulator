# Client mod binaries (no source)

This directory holds a few no-source artefacts from the tada-o tree. The Linux
installer (`client/linux-installer/`) downloads the official client through
Net-7's launcher rather than using anything here, so these are historical
reference only.

| File | Size | What it is |
|---|---|---|
| `release/authlogin.dll` | ~110 KB | Authentication shim loaded by the patched client; talks to the login server. Net-7 community artefact. |
| `release/launch.bat` | text | Launcher batch file. |

## Removed: original Westwood/EA copyrighted binaries

The following were **deleted from the repository** because they are original
Westwood/EA copyrighted game content that we have no right to redistribute, and
nothing in the build, deploy, launcher, or installer uses the committed copies:

| File | Size | What it was |
|---|---|---|
| `release/client.exe` | ~8.3 MB | The Earth & Beyond client executable (a patched build, but fundamentally the EA/Westwood client binary). |
| `Data/client/mixfiles/EB_Sizzle.bik` | ~22 MB | Original Westwood game intro video (Bink Video). |

Get the client the legitimate way instead: run
`client/linux-installer/install-enb-linux.sh`, which installs the official
Earth & Beyond client under WINE and pulls in Net-7's current launcher. (Note
the removal is from the working tree; the blobs still exist in git history. A
full history purge is a separate, history-rewriting step.)

## Provenance

The remaining files came from the tada-o fork source tree
(`Source Code/Client Mods/`) verbatim. The tada-o tree is itself an svn checkout
of Net-7's repo at r2974 (2010-03-15); these binaries were produced by the Net-7
team using internal tooling that was not part of the public source release.
