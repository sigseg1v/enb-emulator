# Third-party prebuilt binaries (server)

These are precompiled `.lib` (Windows static archive) files vendored from upstream so the original MSVC9 Windows build still works without external dependencies. They're not used by the Linux build — `server/CMakeLists.txt` finds the equivalents from system packages or vcpkg.

| File | What it is | Upstream | Why we keep it |
|---|---|---|---|
| `luabind.lib` | Luabind static lib (MSVC9, x86) | http://www.rasterbar.com/products/luabind.html | The tada-o checkout pinned a specific MSVC9-compiled copy. Linux builds use system luabind (or build from source). |
| `lua.lib` | Lua 5.x static lib (MSVC9, x86) | https://www.lua.org/ | Same reason. Linux builds use system `liblua5.x-dev`. |
| `tinyxmld.lib` | TinyXML (debug build, MSVC9, x86) | http://www.grinninglizard.com/tinyxml/ | Used by `TalkTreeParser`, `MissionDatabaseSQL`, etc. Linux uses system `libtinyxml-dev`. |
| `libeay32.lib` | OpenSSL libcrypto (old MSVC9 build) | https://www.openssl.org/ | OpenSSL 1.0.x era. Phase E handles migration to 3.x. |
| `libmySQL.lib` (in `login-server/Net7Mysql/`) | MySQL Connector/C static lib | https://dev.mysql.com/downloads/connector/c/ | Login server connects to MySQL. Phase C migrates to Postgres + libpqxx. |

In `server/src/LUA/` there are MSVC9 prebuilds of Lua + Luabind for **both** x86 and x64. Same status — kept for Windows builds, not used on Linux.

In `client/detours/Detours/lib/detours.lib` — Microsoft Research Detours prebuilt. Used by client mods to hook the game client.

## Source availability

For Lua, Luabind, TinyXML, OpenSSL — full source is publicly available, the prebuilt blobs are kept only for backward-compat with the historical MSVC9 build. The Linux build does NOT depend on them. The .NET 10 tools (`tools/`) ship their own DLL deps in `tools/<tool>/Libs/` — see `tools/THIRD_PARTY_BINARIES.md`.

## Things we don't have source for

- The Detours `.lib` is the only one in this list where the source isn't generally available (the open-source version of Detours postdates the .lib here; the legacy .lib here is from the closed-source release). Phase B will switch to the open-source Detours from GitHub.
