# justfile — build / dev / test / package targets.
#
# Requires https://github.com/casey/just (`apt install just` on recent
# Debian/Ubuntu, or `cargo install just`).
#
# Most targets call docker/cmake/dotnet; none of them require root.

IMAGE_REGISTRY := env_var_or_default("IMAGE_REGISTRY", "ghcr.io/anthropics/enb-emulator")
IMAGE_TAG      := env_var_or_default("IMAGE_TAG", "dev")

# Connection to the local content DB (`net7`), exported so the Avalonia editors
# launched via `just launch-*` prefill their Login dialog with zero typing --
# the user can just click Login (CommonTools.Gui.LoginData.LoadFromEnvironment).
# These mirror the docker-compose stack's host-side binding (localhost:5434 ->
# container 5432, creds net7/net7). Override any with the matching env var.
export ENB_DB_HOST := env_var_or_default("ENB_DB_HOST", "localhost")
export ENB_DB_PORT := env_var_or_default("ENB_DB_PORT", "5434")
export ENB_DB_USER := env_var_or_default("ENB_DB_USER", "net7")
export ENB_DB_PASS := env_var_or_default("ENB_DB_PASS", "net7")

# Per-worktree docker compose project name, derived from the current git
# branch so parallel worktrees don't fight over the same container set.
# main/master/detached-HEAD collapse to plain `freya`. Already-prefixed
# branches (freya-foo) are used as-is. Override with the env var.
#
# Note: only the container/network/volume *names* are namespaced. Host
# port bindings in docker-compose.yml are still fixed at the conventional
# defaults, so only one worktree at a time can run its stack.
export COMPOSE_PROJECT_NAME := env_var_or_default("COMPOSE_PROJECT_NAME", `b=$(git branch --show-current 2>/dev/null); if [ -z "$b" ] || [ "$b" = main ] || [ "$b" = master ]; then echo freya; else s=$(printf '%s' "$b" | tr 'A-Z' 'a-z' | tr -c 'a-z0-9_-' '-' | tr -s '-' | sed 's/^-//;s/-$//'); case "$s" in freya-*) echo "$s";; *) echo "freya-$s";; esac; fi`)

# Default: list targets.
default:
    @just --list

_default: default

# ---- build ----

# Build the C++ server (Phase B: best-effort, may fail mid-build).
build:
    cmake -S server -B build/server -G Ninja
    cmake --build build/server -j"$(nproc)"

# Build the C# tool suite (FreyaTools.slnx, .NET 10).
build-tools:
    dotnet build tools/FreyaTools.slnx

# Decode a proxy<->server sector capture into a nav/mob/resource inventory.
# Output lands next to the input as <input>.inventory.txt (gitignored).
#   just pcap-inventory proxy/local-debug/<capture>.pcapng
pcap-inventory FILE:
    dotnet run --project tools/pcap-inventory -c Release -- "{{FILE}}"

# Publish pcap-inventory as a self-contained, single-file Windows .exe with no
# .NET install required, so a non-technical user can just drag a .pcapng file
# onto it to get a <name>.inventory.txt next to it. Output:
#   bin/pcap-inventory.exe
# (Trimming is left OFF: the tool reflects over CliClient.Core record decoders,
# and an over-eager trimmer would strip types it can't see are used.)
package-pcap-inventory:
    @echo ">>> publishing self-contained win-x64 single-file pcap-inventory.exe"
    dotnet publish tools/pcap-inventory -c Release -r win-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o tools/pcap-inventory/bin/win-x64-publish
    @mkdir -p bin
    @cp tools/pcap-inventory/bin/win-x64-publish/pcap-inventory.exe bin/pcap-inventory.exe
    @echo ">>> done. bin/pcap-inventory.exe -- drag a .pcapng onto it on Windows."

# Cross-compile FreyaProxy as a Win32 PE binary (MinGW-w64). The launcher
# spawns this under WINE next to the EnB client — see plans/23-phase-w-proxy-win32-crossbuild.md.
# Builds OpenSSL 3 statically into proxy/third_party/openssl-mingw64 the
# first time (idempotent), then cmake-configures + builds, then stages
# FreyaProxy.exe to ./bin/ where LaunchFreya looks for it.
build-proxy-win64:
    @echo ">>> building static OpenSSL 3 for MinGW (idempotent — skip if already built)"
    ./proxy/scripts/build-openssl-mingw.sh
    @echo ">>> cmake configure (Win32 cross)"
    # CMAKE_EXE_LINKER_FLAGS_INIT (in the toolchain) only seeds a FRESH cache.
    # A build-win64/ left over from before the --no-insert-timestamp flag would
    # keep its old cached linker flags and resume stamping a live PE timestamp,
    # making every rebuild a different hash. Drop a pre-flag cache once so the
    # reproducible-build flag actually takes (openssl is built separately, so
    # this only recompiles the proxy objects).
    @if [ -f proxy/build-win64/CMakeCache.txt ] && ! grep -q 'no-insert-timestamp' proxy/build-win64/CMakeCache.txt; then \
        echo ">>> stale build-win64 cache predates the reproducible-build flag — reconfiguring clean"; \
        rm -rf proxy/build-win64; \
    fi
    cmake -S proxy -B proxy/build-win64 \
        -DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64-x86_64.toolchain.cmake \
        -DCMAKE_BUILD_TYPE=Release
    @echo ">>> cmake build"
    cmake --build proxy/build-win64 -j"$(nproc)"
    @echo ">>> staging FreyaProxy.exe → bin/"
    @mkdir -p bin
    @cp proxy/build-win64/FreyaProxy.exe bin/FreyaProxy.exe
    @echo ">>> done. bin/FreyaProxy.exe is what 'just launch-net7' will spawn under WINE."

# Cross-compile the standalone MVAS position-feed DLL (PB-2) as a 32-bit Win32
# PE. It MUST be 32-bit: client.exe is a PE32/i386 process and a DLL loaded into
# it (via FreyaInject.exe remote-thread LoadLibrary -- see tools/LaunchFreya) has
# to match its bitness. This is the ONLY 32-bit artifact in the tree, hence the separate i686
# toolchain (the proxy/launcher are 64-bit). It is a minimal DLL: just
# PosFeedDllMain.cpp + ClientPositionFeed.cpp, no Detours, no client offsets.
#
# Requires the i686 MinGW toolchain, which is NOT the same package as the x86-64
# one used for the proxy:
#   sudo apt install gcc-mingw-w64-i686-posix g++-mingw-w64-i686-posix
build-posfeed-dll:
    @if ! command -v i686-w64-mingw32-g++-posix >/dev/null 2>&1 && ! command -v i686-w64-mingw32-g++ >/dev/null 2>&1; then \
        echo "ERROR: 32-bit MinGW not found. client.exe is PE32/i386, so the feed DLL must be 32-bit." >&2; \
        echo "  install it:  sudo apt install gcc-mingw-w64-i686-posix g++-mingw-w64-i686-posix" >&2; \
        exit 1; \
    fi
    @cxx="$(command -v i686-w64-mingw32-g++-posix || command -v i686-w64-mingw32-g++)"; \
    echo ">>> building 32-bit FreyaPosFeed.dll with $cxx"; \
    mkdir -p bin; \
    "$cxx" -shared -O2 -static -static-libgcc -static-libstdc++ \
        -Wall -Wextra \
        -o bin/FreyaPosFeed.dll \
        freya/client-injection/PosFeedDllMain.cpp freya/client-injection/ClientPositionFeed.cpp \
        -Ifreya/client-injection \
        -lws2_32 \
        -Wl,--no-insert-timestamp; \
    echo ">>> building 32-bit FreyaInject.exe with $cxx"; \
    "$cxx" -O2 -static -static-libgcc -static-libstdc++ \
        -Wall -Wextra \
        -o bin/FreyaInject.exe \
        freya/client-injection/FreyaInject.cpp \
        -Wl,--no-insert-timestamp
    @echo ">>> done. bin/FreyaPosFeed.dll + bin/FreyaInject.exe (32-bit). The launcher injects the DLL into client.exe at launch via FreyaInject.exe (WINE has no AppInit_DLLs)."

# Standalone Windows client package. Produces dist/enb-client-windows/ holding a
# self-contained launcher (FreyaLauncher.exe -- no .NET runtime needed) + the Win32
# proxy (bin/FreyaProxy.exe) + the 32-bit MVAS position-feed injection pair
# (bin/FreyaPosFeed.dll + bin/FreyaInject.exe) + a package-only FreyaLauncher.cfg
# that defaults to the public server. The end user extracts the folder on Windows and runs
# FreyaLauncher.exe: no docker, no dev environment, nothing to install beyond Earth
# & Beyond itself. The launcher connects to a remote upstream (default
# enb.sigsegv.land; the Server box is editable so they can point it anywhere).
# Note: this packaging-only cfg is what flips the defaults to Multi-Player +
# enb.sigsegv.land -- `just launch-net7` / `just play-*` keep the localhost dev
# cfg untouched.
package-client-windows: build-proxy-win64 build-posfeed-dll
    @echo ">>> publishing self-contained win-x64 launcher (single-file)"
    dotnet publish tools/LaunchFreya/LaunchFreya.csproj -c Release -r win-x64 \
        --self-contained true \
        -p:CheckForUpdates=true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none \
        -o tools/LaunchFreya/bin/win-x64-publish
    @echo ">>> assembling dist/enb-client-windows/"
    @rm -rf dist/enb-client-windows
    @mkdir -p dist/enb-client-windows/bin
    @cp tools/LaunchFreya/bin/win-x64-publish/FreyaLauncher.exe dist/enb-client-windows/FreyaLauncher.exe
    @cp bin/FreyaProxy.exe dist/enb-client-windows/bin/FreyaProxy.exe
    @cp bin/FreyaPosFeed.dll dist/enb-client-windows/bin/FreyaPosFeed.dll
    @cp bin/FreyaInject.exe dist/enb-client-windows/bin/FreyaInject.exe
    @cp tools/LaunchFreya/FreyaLauncher.windows-package.cfg dist/enb-client-windows/FreyaLauncher.cfg
    @echo ">>> zipping dist/enb-client-windows.zip"
    @rm -f dist/enb-client-windows.zip
    @cd dist && zip -qr enb-client-windows.zip enb-client-windows
    @echo ">>> done. dist/enb-client-windows/ (+ enb-client-windows.zip)"
    @echo "    Contents: FreyaLauncher.exe + bin/FreyaProxy.exe + bin/FreyaPosFeed.dll + bin/FreyaInject.exe + FreyaLauncher.cfg"
    @echo "    User extracts the zip on Windows and runs FreyaLauncher.exe;"
    @echo "    FreyaLauncher self-updates itself + FreyaProxy + the MVAS feed pair from the server thereafter."

# Smoke-run FreyaProxy.exe under WINE (no game client, just the proxy).
# Confirms WSAStartup + binds TCP 3801/3805 + opens both UDP planes.
# Set NET7_UPSTREAM_HOST=<host> in the env to point the proxy at a non-local
# game server. Ctrl-C to stop.
run-proxy-wine:
    @if [ ! -x bin/FreyaProxy.exe ]; then echo "bin/FreyaProxy.exe missing — run 'just build-proxy-win64' first" >&2; exit 1; fi
    wine bin/FreyaProxy.exe

# Stop the docker proxy container if it's running. The WINE proxy spawned
# by `just launch-net7` binds the same host port 3801 — they can't both
# run. `just run-stack-bg` doesn't start the docker proxy in the first
# place, but `docker compose up` (no-arg) or `just dev-fg` does.
stop-docker-proxy:
    -docker compose stop proxy

# ---- launch C# editors (Avalonia ports — Linux native) ----
#
# Each recipe runs the Avalonia port of a tools/* editor. Use `just launch`
# for the central toolslauncher GUI (button per editor); use `just launch-X`
# to skip straight to a specific editor.
#
# All editors that talk to the DB connect via the Login dialog on
# startup. Launched through `just`, the dialog is PREFILLED from the
# exported ENB_DB_* vars (localhost:5434 / net7 / net7 -- the dev stack;
# `just init` first), so you just click Login: no typing. Phase N moved
# the content DB to Postgres `net7` (was MySQL on 3307). Tools that don't
# talk to the DB (toolslauncher, launchnet7, enbpatcher, toolspatcher,
# w3d-parser, talktreeeditor) skip the login dialog.

# Central launcher GUI — button per editor; spawns Avalonia projects.
launch:
    dotnet run --project tools/toolslauncher-avalonia

# Game client launcher (Freya).
launch-net7:
    dotnet run --project tools/LaunchFreya

# Effect / particle / stat editor (DB).
launch-effect-editor:
    dotnet run --project tools/effect-editor-avalonia

# NPC faction relationships editor (DB).
launch-faction-editor:
    dotnet run --project tools/faction-editor-avalonia

# item_base data editor (DB).
launch-item-editor:
    dotnet run --project tools/item-editor-avalonia

# Headless GUI end-to-end smoke for the item editor: launches MainWindow under
# Avalonia's headless platform, selects a row, edits the detail panel, clicks
# Save, and asserts the edit persisted to the net7 container (then restores it).
# Regression guard for the parameter-binding write-path fix. Needs the stack up
# (`just dev`); skips cleanly if net7 is unreachable.
verify-item-editor:
    dotnet run --project tools/item-editor-avalonia/test

# Mission / quest authoring (DB).
launch-mission-editor:
    dotnet run --project tools/missioneditor-avalonia

# Mob (NPC) data editor (DB).
launch-mob-editor:
    dotnet run --project tools/mob-editor-avalonia

# Sector / map authoring (DB, Piccolo-on-Avalonia canvas).
launch-sector-editor:
    dotnet run --project tools/sector-editor-avalonia

# Station / vendor / NPC editor (DB).
launch-station-tools:
    dotnet run --project tools/station-tools-avalonia

# NPC dialog tree editor (XML in/out, no DB).
launch-talktree-editor:
    dotnet run --project tools/talktreeeditor-avalonia

# Bulk import of game data into the DB.
launch-dataimport:
    dotnet run --project tools/dataimport-avalonia

# Client patcher.
launch-enbpatcher:
    dotnet run --project tools/enbpatcher-avalonia

# Patches the tools themselves.
launch-toolspatcher:
    dotnet run --project tools/toolspatcher-avalonia

# Build the gtest harness (Phase G).
build-tests:
    cmake -S freya/tests/server -B build/tests -G Ninja
    cmake --build build/tests -j"$(nproc)"

# ---- dev stack ----

# One-shot first-time setup: generate dev SSL certs, bring up postgres,
# wait for it + the one-shot `schema-init` service to finish loading the
# converted schema + seed data, smoke-check it's reachable.
init: gen-certs
    @echo ">>> bringing up postgres + applying schema"
    docker compose up -d postgres schema-init
    @echo ">>> waiting for postgres to become healthy"
    @bash -c 'until [ "$(docker inspect -f {{{{.State.Health.Status}} ${COMPOSE_PROJECT_NAME}-postgres-1 2>/dev/null)" = "healthy" ]; do echo "  ...waiting"; sleep 3; done'
    @echo ">>> waiting for schema-init to finish"
    @bash -c 'until docker inspect -f "{{{{.State.Status}}" ${COMPOSE_PROJECT_NAME}-schema-init-1 2>/dev/null | grep -q exited; do echo "  ...waiting"; sleep 2; done'
    @echo ">>> verifying net7 + net7_user databases"
    docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -l
    docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -c "SELECT COUNT(*) AS account_rows FROM accounts;"
    @echo ">>> init complete. Next: 'just run-stack'"

# Generate the self-signed dev cert pair the server expects to find at
# CWD as <g_DomainName>.cer / .pem (SSL_Listener.cpp:56-57). Phase V
# switched g_DomainName from local.net-7.org to localhost.
gen-certs:
    @mkdir -p deploy/certs
    @if [ ! -f deploy/certs/localhost.cer ]; then \
        echo ">>> generating self-signed cert for localhost"; \
        openssl req -x509 -newkey rsa:2048 -days 3650 -nodes \
            -subj "/CN=localhost/O=Earth-and-Beyond Emulator Dev/C=US" \
            -addext "subjectAltName=DNS:localhost,DNS:local.net-7.org,IP:127.0.0.1" \
            -keyout deploy/certs/localhost.pem \
            -out    deploy/certs/localhost.cer; \
    else \
        echo "deploy/certs/localhost.cer exists, skipping"; \
    fi

# Bring up the full runtime stack (postgres + schema-init + server +
# login + proxy). Server image is built on demand. Streams logs in the
# foreground; Ctrl-C to stop.
#
# Includes `proxy` (and not just server + login) so the docker proxy is
# what the WINE client TCP-connects to on localhost:3801 / :3805 / :3500.
# Keeping the proxy in-network means proxy <-> server UDP never crosses
# the docker NAT, which dodges the rootless-docker slirp4netns conntrack
# pitfall on the sector reverse-push from MVASauth:3806. The xUnit
# integration suite (ServerFixture `compose up -d --wait`) also brings
# the proxy up; this keeps `just dev` parity with the test stack.
run-stack: init
    docker compose up server login proxy

# Same but detached.
run-stack-bg: init
    docker compose up -d server login proxy

# Convenience: legacy name. Same as run-stack-bg.
dev: run-stack-bg

# Rebuild + restart ONLY what actually changed. `docker compose build`
# recompiles just the images whose build context changed (Docker's layer cache
# makes an unchanged service a near-instant no-op -- no recompile); then plain
# `up -d` (deliberately NOT --force-recreate) recreates only the containers
# whose image ID actually changed, so an unchanged server/login keeps running
# and does NOT bounce or lose in-flight state. Scope it tighter with an arg:
# `just rebuild proxy` touches only the proxy. Run this after changing C++ in
# server/, proxy/, or login-server/ -- `just play-local` / `run-stack-bg`
# deliberately REUSE running containers, so without a rebuild a relaunch keeps
# serving the OLD binary. postgres + its pgdata volume are never touched.
rebuild SERVICES='proxy server login':
    docker compose build {{SERVICES}}
    docker compose up -d {{SERVICES}}

# Internal helper: print each image's build timestamp and, if the source on
# disk is NEWER than the built image, print a big STALE-IMAGE banner naming the
# rebuild command. Used by play-local / play-cli so a relaunch tells you whether
# you are testing the current binary or an old one. Args:
#   COMPOSE_ARGS  extra `docker compose` flags (e.g. "-f docker-compose.cli.yml
#                 -p cli1"); pass "" for the default stack.
#   SERVICES      space-separated compose service names to inspect.
#   REBUILD_CMD   the exact command to print in the banner to rebuild them.
_image-status COMPOSE_ARGS SERVICES REBUILD_CMD:
    #!/usr/bin/env bash
    set -uo pipefail
    read -ra CA <<< "{{COMPOSE_ARGS}}"
    read -ra SVCS <<< "{{SERVICES}}"
    declare -A SRC=(
      [proxy]="proxy common/include/net7"
      [server]="server/src common/include/net7"
      [login]="login-server common/include/net7"
      [cli]="freya/cli-client"
    )
    STALE=()
    echo ">>> image build times (an image OLDER than its source is flagged):"
    for svc in "${SVCS[@]}"; do
      img=$(docker compose "${CA[@]}" images -q "$svc" 2>/dev/null | head -1)
      if [ -z "$img" ]; then
        printf '      %-8s last-image-build: (not built yet -- builds on first run)\n' "$svc:"
        continue
      fi
      created=$(docker image inspect "$img" -f '{{{{.Created}}' 2>/dev/null)
      ce=$(date -d "$created" +%s 2>/dev/null || echo 0)
      human=$(date -d "$created" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo "$created")
      # Staleness via git, NOT file mtime: a checkout/rebase rewrites every
      # source mtime to "now" and would falsely flag everything. The newest
      # COMMIT touching the source survives checkouts; uncommitted edits are
      # caught separately by `git status`. Either one means the image predates
      # the current source.
      paths="${SRC[$svc]:-}"
      flag=""
      commit_ct=$(git log -1 --format=%ct -- $paths 2>/dev/null)
      if [ -n "$commit_ct" ] && [ "$commit_ct" -gt "$ce" ]; then
        flag="   <-- MAY be out of date (newer commit not built into this image)"; STALE+=("$svc")
      elif [ -n "$(git status --porcelain -- $paths 2>/dev/null)" ]; then
        flag="   <-- MAY be out of date (uncommitted source edits not built)"; STALE+=("$svc")
      fi
      printf '      %-8s last-image-build: %s%s\n' "$svc:" "$human" "$flag"
    done
    if [ "${#STALE[@]}" -gt 0 ]; then
      echo
      echo "  ##########################################################################"
      echo "  ##  image(s) MAY be out of date: ${STALE[*]}"
      echo "  ##  The source on disk is newer than the built image, so these containers"
      echo "  ##  MAY be running an older binary than your current source."
      echo "  ##"
      echo "  ##  (The default launch already builds-if-stale; you will mainly see this"
      echo "  ##   when ENB_NOREBUILD=1 is set, or for the shared server under play-cli.)"
      echo "  ##  To rebuild from current source (postgres + pgdata untouched), run:"
      echo "  ##"
      echo "  ##      {{REBUILD_CMD}}"
      echo "  ##"
      echo "  ##########################################################################"
      echo
    fi

# Bring up an interactive CLI client in its own container, paired with its own
# dedicated proxy, against the running shared stack.
#
# Why a dedicated proxy: the FreyaProxy is a SINGLE-client bridge (one global
# ServerManager + g_LoggedIn + singular m_{Sector,Global,Master}Connection in
# proxy/ServerManager.h). A second client through the same proxy clobbers those
# pointers -- that's the "cli client and client.exe steal each other's ports"
# symptom. So each CLI gets its OWN proxy. client.exe keeps the host-published
# proxy from the main stack; this recipe never touches it. See
# docker-compose.cli.yml for the full topology rationale.
#
# UNIT names the compose project (container/network namespace) so you can run
# several at once: `just play-cli cli1`, `just play-cli cli2`, ... Each unit
# reuses the default proxy ports inside its own private network; nothing is
# host-published, so they never collide. NOTE: the server force-kicks a
# duplicate login PER ACCOUNT (retail behaviour, not bypassed) -- give each
# concurrent client a DISTINCT account.
#
# Inside the REPL:  connect cliproxy   then   login <user> <pass>
# (auth/MVAS hosts + auth port 443 come from env in docker-compose.cli.yml.)
play-cli UNIT='cli1':
    #!/usr/bin/env bash
    set -euo pipefail
    # Ensure the shared stack is up WITHOUT ever bouncing it. Launching a CLI
    # must never restart the shared server/login/proxy or wipe another player's
    # in-flight session, so `--no-recreate` starts only missing containers and
    # leaves running ones (and their state) untouched. play-cli NEVER rebuilds
    # the shared server -- use `just play-local` or `just rebuild` for that.
    just init
    docker compose up -d --no-recreate server login proxy
    # The CLI unit attaches to the shared stack network by name. It's derived
    # from COMPOSE_PROJECT_NAME (per-worktree), so pass it through rather than
    # hardcoding `freya_default`.
    export STACK_NETWORK="${COMPOSE_PROJECT_NAME}_default"
    echo ">>> CLI unit '{{UNIT}}' -> stack network '$STACK_NETWORK'"
    echo ">>> inside the REPL:  connect cliproxy   then   login <user> <pass>"
    # BUILD-IF-STALE for THIS cli unit (its cli + dedicated proxy). The layer
    # cache makes an unchanged unit a near-instant no-op, so a relaunch with no
    # code change does not rebuild. If you changed CliClient or the proxy, the
    # new image is built here. ENB_NOREBUILD=1 skips the build and reuses the
    # existing images as-is.
    if [ -n "${ENB_NOREBUILD:-}" ]; then
        echo ">>> ENB_NOREBUILD=1 -- reusing existing cli/proxy images as-is (no build)"
    else
        echo ">>> build-if-stale for cli unit '{{UNIT}}' (layer cache skips unchanged); set ENB_NOREBUILD=1 to skip"
        docker compose -f docker-compose.cli.yml -p {{UNIT}} build
    fi
    just _image-status "-f docker-compose.cli.yml -p {{UNIT}}" "cli proxy" "just rebuild-cli {{UNIT}}"
    # `run` (not `up`) gives an interactive TTY so the line editor + colour
    # turn on; --rm cleans up the ephemeral CLI container on exit. The
    # dedicated proxy stays up (unless-stopped) for re-runs of the same unit.
    docker compose -f docker-compose.cli.yml -p {{UNIT}} run --rm cli

# Rebuild a CLI unit's cli + dedicated-proxy images from current source. Run
# this after changing CliClient code (e.g. the REPL) or the proxy; then
# `just play-cli <UNIT>` reuses the freshly-built image. Defaults to `cli1`.
rebuild-cli UNIT='cli1':
    docker compose -f docker-compose.cli.yml -p {{UNIT}} build

# Tear down a CLI unit's dedicated proxy (and any leftover CLI container).
# Pair with `just play-cli <UNIT>`; defaults to the same `cli1` unit name.
stop-cli UNIT='cli1':
    docker compose -f docker-compose.cli.yml -p {{UNIT}} down

# Bring up the local stack + launch the launcher pre-configured to connect.
#
# With no args, defaults to the linux-installer's default install location:
#   $HOME/.wine-enb/drive_c/Program Files/EA GAMES/Earth & Beyond/release/client.exe
# Override either as a recipe arg or via the ENB_CLIENT_PATH env var:
#   just play-local /home/me/.wine/drive_c/.../release/client.exe
#   ENB_CLIENT_PATH=... just play-local
#
# Architecture (2026-05-29 rewrite):
#   The recipe brings up the FULL docker-compose stack (postgres + server +
#   login + PROXY) and then launches the launcher, which only spawns
#   client.exe under WINE. There is NO WINE-side FreyaProxy.exe.
#
#   The client connects TCP to the docker proxy on localhost:3801 / :3805 /
#   :3500 (port-published). The docker proxy speaks UDP to the docker
#   server entirely INSIDE the docker network -- no host-side UDP, no
#   docker NAT on the in-game plane.
#
#   Why this matters: on rootless docker (slirp4netns / pasta) the sector
#   reverse-push from MVASauth:3806 to (proxy_ip, proxy_global_src_port)
#   crosses a UDP conntrack mapping that wasn't established by an inbound
#   flow on that exact 5-tuple. slirp drops it. The visible symptom is
#   "Re-send Ack request 3" + "Player ... timed out during login stage 3"
#   in the server log, with the client hung on Enter Galaxy. Keeping the
#   proxy in-network sidesteps the whole rootless-docker NAT class of bugs.
#
# Steps the recipe performs:
#   1. `just run-stack-bg`           -- postgres + server + login + proxy.
#   2. Pre-writes FreyaLauncher.settings.json so the launcher opens with
#      Emulator=Net7Local, Host=localhost, port 4443 (the dev stack's
#      host-side mapping of the login container's 443). The launcher's
#      in-process LocalAuthRelay terminates the client's plaintext-HTTP
#      auth call on 127.0.0.1 and re-wraps it as TLS to the upstream --
#      so we don't need the WINE prefix to trust the dev cert (verify
#      is skipped only because upstream is loopback).
#   3. Runs the launcher. Net7Local in FreyaLauncher.cfg deliberately has
#      no launchName attribute, so Launcher.cs's switch falls to its
#      default case = LaunchClient() only (skip LaunchFreyaProxy).
#
# Click Play in the GUI; the client should connect to the local server.
play-local CLIENT_PATH='':
    #!/usr/bin/env bash
    set -euo pipefail
    cp="{{CLIENT_PATH}}"
    if [ -z "$cp" ]; then cp="${ENB_CLIENT_PATH:-}"; fi
    if [ -z "$cp" ]; then cp="$HOME/.wine-enb/drive_c/Program Files/EA GAMES/Earth & Beyond/release/client.exe"; fi
    if [ ! -f "$cp" ]; then
        echo "play-local: client.exe not found at: $cp" >&2
        echo "  pass the path as the recipe arg or set ENB_CLIENT_PATH." >&2
        exit 1
    fi

    echo ">>> bringing up local stack (postgres + server + login + proxy)"
    just init
    # BUILD-IF-STALE (default). `docker compose build` recompiles ONLY the
    # services whose build context actually changed -- Docker's layer cache
    # makes an unchanged service a near-instant no-op -- then `up -d` recreates
    # ONLY the containers whose image ID changed. So if you changed nothing,
    # nothing rebuilds and nothing restarts: your in-flight session is left
    # alone. If you changed server/ proxy/ login-server/ C++, the new binary is
    # built and only that one container bounces. This is what kills the
    # stale-image trap (testing an OLD binary because the container was never
    # rebuilt -- see CLAUDE.md "Wire format & byte order").
    #
    # ENB_NOREBUILD=1 forces a PURE ATTACH: skip the build entirely and start
    # only containers that are missing. `--no-recreate` leaves every running
    # container -- and its in-flight player/session state -- exactly as-is.
    # Use it when you KNOW the running binaries are current and must not bounce.
    if [ -n "${ENB_NOREBUILD:-}" ]; then
        echo ">>> ENB_NOREBUILD=1 -- skipping build; starting only missing containers, running state untouched"
        docker compose up -d --no-recreate server login proxy
    else
        echo ">>> build-if-stale (layer cache skips unchanged services); set ENB_NOREBUILD=1 to skip"
        docker compose build server login proxy
        docker compose up -d server login proxy
    fi
    just _image-status "" "proxy server login" "just rebuild"

    echo ">>> building launcher (so its output dir exists for settings.json)"
    dotnet build tools/LaunchFreya >/dev/null

    # PB-2: build the in-client position-feed DLL so the launcher can inject it
    # (UsePositionFeed=true below). REQUIRED -- if the 32-bit MinGW toolchain is
    # missing this FAILS the launch (with the apt install line) rather than
    # silently running a client with no feed. The feed is still inert until the
    # owner fills ClientEngineOffsets.local.h, but the injection wiring itself is
    # exercised every play-local.
    echo ">>> building position-feed DLL (required)"
    just build-posfeed-dll

    SETTINGS_DIR=tools/LaunchFreya/bin/Debug/net10.0
    mkdir -p "$SETTINGS_DIR"
    cp_json=$(printf '%s' "$cp" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')
    cat > "$SETTINGS_DIR/FreyaLauncher.settings.json" <<JSON
    {
      "ClientPath": $cp_json,
      "LastEmulatorName": "Net7Local",
      "LastServerName": "localhost",
      "UsePositionFeed": true,
      "UseLocalCert": false,
      "UseSecureAuthentication": true,
      "AuthenticationPort": "4443",
      "FormMainPositionX": -1,
      "FormMainPositionY": -1
    }
    JSON
    echo ">>> wrote $SETTINGS_DIR/FreyaLauncher.settings.json"

    : "${WINEPREFIX:=$HOME/.wine-enb}"
    export WINEPREFIX

    # Default play-local is quiet -- no WINEDEBUG overrides, so the console
    # only carries the launcher + proxy + server logs. For SEH / module
    # tracing on a crash, use `just debug-local` instead (it sets
    # WINEDEBUG=+seh,+module,err+module and then calls this recipe).
    echo ">>> launching (WINEPREFIX=$WINEPREFIX WINEDEBUG=${WINEDEBUG:-<unset>}) -- click Play in the GUI"
    dotnet run --no-build --project tools/LaunchFreya

# Connect to the REMOTE cloud server -- no local docker stack at all.
#
# Unlike `play-local` (which boots postgres + server + login + proxy in docker
# and points the client at localhost), this brings up NOTHING locally except the
# two per-client Win32 processes the launcher itself spawns under WINE:
#   1. FreyaProxy.exe -- the single-client bridge, dialed at the cloud's resolved
#      IP (/ADDRESS:<ip>). It speaks the cleartext game UDP planes + login to the
#      remote box. The FreyaProxy IS the local half of the topology; there is no
#      such thing as "connect with nothing running locally" -- the proxy must run
#      on the player's machine (see CLAUDE.md "proxy is single-client"). The
#      launcher just spawns it for you.
#   2. client.exe -- talks only to the local proxy.
# The launcher's in-process LocalAuthRelay terminates the client's plaintext-HTTP
# auth on loopback and re-wraps it as TLS to the cloud login on :443 (a real
# Let's Encrypt cert, so no UseLocalCert).
#
# Selecting the "Net7MP" emulator is what makes the launcher's switch take the
# NET7MP case (spawn proxy + client) instead of client-only. The Server box is
# prefilled to the cloud host; override with ENB_ONLINE_HOST=<host> or arg 2.
#
# Client path: same resolution as play-local (arg 1 / ENB_CLIENT_PATH / default
# linux-installer location).
#
#   just play-online
#   just play-online /path/to/client.exe
#   ENB_ONLINE_HOST=myserver.example just play-online
play-online CLIENT_PATH='' HOST='':
    #!/usr/bin/env bash
    set -euo pipefail
    cp="{{CLIENT_PATH}}"
    if [ -z "$cp" ]; then cp="${ENB_CLIENT_PATH:-}"; fi
    if [ -z "$cp" ]; then cp="$HOME/.wine-enb/drive_c/Program Files/EA GAMES/Earth & Beyond/release/client.exe"; fi
    if [ ! -f "$cp" ]; then
        echo "play-online: client.exe not found at: $cp" >&2
        echo "  pass the path as the recipe arg or set ENB_CLIENT_PATH." >&2
        exit 1
    fi

    host="{{HOST}}"
    if [ -z "$host" ]; then host="${ENB_ONLINE_HOST:-enb.sigsegv.land}"; fi
    echo ">>> online target: $host:443 (no local docker stack)"

    # A local docker stack (from `just play-local`) binds the same client-facing
    # TCP ports this online proxy needs -- 3500 (PROXY_LOCAL_TCP_PORT), 3801
    # (MASTER_SERVER_PORT), 3805 (GLOBAL_SERVER_PORT). If it's still up, the WINE
    # FreyaProxy.exe can't bind them (EADDRINUSE) and the client silently talks to
    # the LOCAL stack instead of $host -- which surfaces as a ~30s hang then
    # "EA.com temporarily unavailable (INV-300)" at login. Tear it down first.
    echo ">>> taking down any local docker stack (frees 3500/3801/3805)"
    docker compose down --remove-orphans >/dev/null 2>&1 || true
    # Kill stale WINE proxies from prior runs that may still hold those ports.
    # Pattern excludes the .exe suffix match against this recipe's own argv.
    pkill -f 'FreyaProxy\.exe' >/dev/null 2>&1 || true

    # The launcher spawns <launcher-dir>/bin/FreyaProxy.exe under WINE. Build the
    # Win32 proxy and stage it where LaunchFreyaProxy() looks (AppContext.BaseDirectory/bin).
    echo ">>> building Win32 FreyaProxy.exe (idempotent; layer-cached)"
    just build-proxy-win64

    # PB-2: build the in-client position-feed DLL + injector so MVAS movement
    # works online too (same as play-local). The launcher resolves them from
    # bin/ under the repo root (CWD). Inert until ClientEngineOffsets.local.h is
    # filled, but the injection wiring is exercised here.
    echo ">>> building position-feed DLL + injector (required for online MVAS)"
    just build-posfeed-dll

    echo ">>> building launcher"
    dotnet build tools/LaunchFreya >/dev/null

    SETTINGS_DIR=tools/LaunchFreya/bin/Debug/net10.0
    mkdir -p "$SETTINGS_DIR/bin"
    cp bin/FreyaProxy.exe "$SETTINGS_DIR/bin/FreyaProxy.exe"
    echo ">>> staged $SETTINGS_DIR/bin/FreyaProxy.exe"

    cp_json=$(printf '%s' "$cp"   | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')
    host_json=$(printf '%s' "$host" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')
    cat > "$SETTINGS_DIR/FreyaLauncher.settings.json" <<JSON
    {
      "ClientPath": $cp_json,
      "LastEmulatorName": "Net7MP",
      "LastServerName": $host_json,
      "UsePositionFeed": true,
      "UseLocalCert": false,
      "UseSecureAuthentication": true,
      "AuthenticationPort": "443",
      "FormMainPositionX": -1,
      "FormMainPositionY": -1
    }
    JSON
    echo ">>> wrote $SETTINGS_DIR/FreyaLauncher.settings.json (Net7MP -> $host:443)"

    : "${WINEPREFIX:=$HOME/.wine-enb}"
    export WINEPREFIX
    echo ">>> launching (WINEPREFIX=$WINEPREFIX) -- pick Multi-Player if not preselected, then click Play"
    dotnet run --no-build --project tools/LaunchFreya

# Drive the C# CLI client against the REMOTE (cloud) server -- the online twin
# of `just play-cli`. No local docker stack: the CLI's own dedicated proxy
# bridges TCP->UDP straight to the cloud host, and auth/MVAS go direct to the
# cloud. Each invocation is one client + one proxy in an isolated compose
# project (UNIT), so several can run at once against different accounts.
#
# Host defaults to the cloud deployment; override with ENB_ONLINE_HOST or arg 2:
#   just play-online-cli                       # UNIT=online1, default host
#   just play-online-cli online2               # a second concurrent unit
#   ENB_ONLINE_HOST=my.host just play-online-cli
#
# Inside the REPL:  connect cliproxy   then   login <user> <pass>
# (auth host + port 443 and the MVAS host come from env in the compose file.)
# The cloud has NO seeded account -- create one first with
# `just create-account` from deploy/do (the local seed ships none).
play-online-cli UNIT='online1' HOST='':
    #!/usr/bin/env bash
    set -euo pipefail
    host="{{HOST}}"
    if [ -z "$host" ]; then host="${ENB_ONLINE_HOST:-enb.sigsegv.land}"; fi
    export ENB_ONLINE_HOST="$host"
    echo ">>> online CLI target: $host (proxy upstream + auth:443 + MVAS:3806); no local stack"
    echo ">>> CLI unit '{{UNIT}}' (isolated compose project)"
    echo ">>> inside the REPL:  connect cliproxy   then   login <user> <pass>"
    # BUILD-IF-STALE for this unit's cli + dedicated proxy (layer cache skips an
    # unchanged unit). ENB_NOREBUILD=1 reuses the existing images as-is.
    if [ -n "${ENB_NOREBUILD:-}" ]; then
        echo ">>> ENB_NOREBUILD=1 -- reusing existing cli/proxy images as-is (no build)"
    else
        echo ">>> build-if-stale for cli unit '{{UNIT}}'; set ENB_NOREBUILD=1 to skip"
        docker compose -f docker-compose.cli-online.yml -p {{UNIT}} build
    fi
    just _image-status "-f docker-compose.cli-online.yml -p {{UNIT}}" "cli proxy" "just rebuild-cli-online {{UNIT}}"
    # `run` (not `up`) gives an interactive TTY; --rm cleans up the ephemeral CLI
    # container on exit. The dedicated proxy stays up for re-runs of this unit.
    docker compose -f docker-compose.cli-online.yml -p {{UNIT}} run --rm cli

# Rebuild a remote CLI unit's cli + dedicated-proxy images from current source.
# Pair with `just play-online-cli <UNIT>`; defaults to the `online1` unit.
rebuild-cli-online UNIT='online1':
    docker compose -f docker-compose.cli-online.yml -p {{UNIT}} build

# Tear down a remote CLI unit's dedicated proxy (and any leftover CLI
# container). Pair with `just play-online-cli <UNIT>`; defaults to `online1`.
stop-cli-online UNIT='online1':
    docker compose -f docker-compose.cli-online.yml -p {{UNIT}} down

# Same as `play-local`, but with three diagnostic knobs flipped on:
#   1. WINEDEBUG=+seh,+module,err+module so wine prints the structured-
#      exception backtrace (module + function names) to stderr on
#      unhandled page faults. Override WINEDEBUG=... in the env to pick
#      different channels.
#   2. PROXY_EXTRA_ARGS=/OPCODES so net7proxy (in docker) emits its
#      LogVMessage verbose stream -- every inbound/outbound UDP packet,
#      every split-packet reassembly step, every inner opcode. Compose
#      re-resolves the proxy `command:` from this env var, so the
#      proxy container is recreated automatically on `up -d`.
#   3. PROXY_S2C_HEXDUMP=1 so net7proxy dumps the full payload bytes of
#      every server->client opcode it forwards, in the same row format as
#      archive/kyp-snapshot/capturedPackets/*.txt. Pre-RC4-encryption, so
#      it diffs directly against the retail captures. Only set here, not
#      in play-local or the bisect-drop* recipes -- the volume is high
#      (a sector zone-in is ~10k+ hex rows) and would bury the bisection
#      DROP/keep summaries.
#
# Use this when chasing a client crash; `just play-local` stays quiet.
debug-local CLIENT_PATH='':
    #!/usr/bin/env bash
    set -euo pipefail
    : "${WINEDEBUG:=+seh,+module,err+module}"
    export WINEDEBUG
    export PROXY_EXTRA_ARGS=/OPCODES
    export PROXY_S2C_HEXDUMP=1
    just play-local {{CLIENT_PATH}}

# Bisection diagnostic: drop EVERY server->client opcode forwarded by the
# proxy (the proxy still ACKs 0x2020 login-stage confirms back to the
# server so the login state machine advances, but the client receives
# nothing past the TCP redirect handshake). If the Win32 client survives
# past the loading screen in this mode, the crash is being driven by
# server-side payload content; if it still crashes, the crash is in the
# client's post-connect self-test path and the data plane is innocent.
# Inherits debug-local's WINEDEBUG + /OPCODES tracing.
bisect-drop-all CLIENT_PATH='':
    #!/usr/bin/env bash
    set -euo pipefail
    : "${WINEDEBUG:=+seh,+module,err+module}"
    export WINEDEBUG
    export PROXY_EXTRA_ARGS=/OPCODES
    export PROXY_S2C_DROP_ALL=1
    just play-local {{CLIENT_PATH}}

# Bisection diagnostic: drop a specific set of server->client opcodes (CSV
# hex, e.g. `just bisect-drop 0x001b,0x0025,0x00b4`). Use after
# `bisect-drop-all` narrows the failure to "data plane fault" -- start by
# dropping the largest / most structurally novel opcodes (0x1B AUX_DATA,
# 0x25 ITEM_BASE), then widen or narrow based on which iteration
# survives. Inherits debug-local's WINEDEBUG + /OPCODES tracing.
bisect-drop OPCODES CLIENT_PATH='':
    #!/usr/bin/env bash
    set -euo pipefail
    : "${WINEDEBUG:=+seh,+module,err+module}"
    export WINEDEBUG
    export PROXY_EXTRA_ARGS=/OPCODES
    export PROXY_S2C_DROP={{OPCODES}}
    just play-local {{CLIENT_PATH}}

# Build a binary capture-replay file from a retail packet capture
# (archive/kyp-snapshot/capturedPackets/capture_NNN.rar) into
# archive/replay/. The result feeds `just packet-replay`. CAPTURE is
# the basename without extension, e.g. `capture_1`.
capture-extract CAPTURE='capture_1':
    #!/usr/bin/env bash
    set -euo pipefail
    rar=archive/kyp-snapshot/capturedPackets/{{CAPTURE}}.rar
    txt=archive/replay/{{CAPTURE}}.txt
    out=archive/replay/{{CAPTURE}}-sector-s2c.bin
    if [ ! -f "$rar" ]; then echo "$rar not found" >&2; exit 1; fi
    mkdir -p archive/replay
    if [ ! -f "$txt" ]; then
        echo ">>> extracting $rar -> $txt"
        unrar p -inul "$rar" > "$txt"
    fi
    dotnet run --project tools/capture-extract/CaptureExtract.csproj -c Release \
        -- --input "$txt" --output "$out"

# Run the local stack with PROXY_S2C_REPLAY pointing at a capture file.
# The proxy substitutes each server->client opcode payload with the next
# retail payload for that opcode from the file, and rewrites the retail
# avatar_id to your live session's avatar_id on the fly (Wave 325). No DB
# seeding required -- log in with any existing account and enter any
# sector; the proxy learns your live avatar_id from the first s2c emit
# whose retail payload starts with the capture's retail avatar_id and
# rewrites every subsequent substitution.
#
# Outcomes:
#
#   (a) client survives the dock zone-in -> bug is in our payload content,
#       and the per-substitution SUB log lines show byte-prefix-match
#       lengths so you know which opcodes diverge from retail and where.
#   (b) client crashes IDENTICALLY (same AV address) -> the trigger is in
#       our transport (RC4, framing, length encoding, byte order in our
#       headers) NOT the payload, because retail's exact payload bytes
#       still crashed.
#   (c) client crashes DIFFERENTLY -> something the rewrite missed:
#       other-player avatar IDs (NPCs / other players from retail's
#       session), object IDs, or a session-specific ID at an unexpected
#       byte offset. Inspect the SUB / LEARN / REWRITE log lines near
#       the crash to narrow down which opcode caused it.
#
# Optional: if you already know your live avatar_id (e.g. via psql -- see
# `just psql-user` and SELECT avatar_id FROM avatar_info ... ), skip the
# lazy learn:
#   PROXY_LIVE_AVATAR_ID=0xNNNNNNNN just packet-replay capture_1
#
# Volume: archive/replay/ is mounted into the proxy at /app/replay/.
packet-replay CAPTURE='capture_1' CLIENT_PATH='':
    #!/usr/bin/env bash
    set -euo pipefail
    out=archive/replay/{{CAPTURE}}-sector-s2c.bin
    if [ ! -f "$out" ]; then
        echo ">>> $out not found, building"
        just capture-extract {{CAPTURE}}
    fi
    : "${WINEDEBUG:=+seh,+module,err+module}"
    export WINEDEBUG
    export PROXY_EXTRA_ARGS=/OPCODES
    export PROXY_S2C_REPLAY=/app/replay/{{CAPTURE}}-sector-s2c.bin
    just play-local {{CLIENT_PATH}}

# Drop into the enb-cli REPL pointed at the running local stack.
# Assumes the stack is already up (`just dev` / `just run-stack-bg`); does
# not start anything. Useful for reproducing client-side crashes from the
# launcher path -- the CLI walks the same connect/login/create/enter
# sequence but is more permissive than the Win32 client at decode time, so
# CLI-completes != WINE-completes (it's a triage tool, not a proof of
# correctness).
#
# Once in the prompt:
#     connect 127.0.0.1
#     login <user> <pass>
#     create JE Aevin       (firstname must contain a vowel)
#     enter Aevin
#     quit
launch-cli:
    dotnet run --project freya/cli-client/src/CliClient.App -- start

# Run the enb-cli REPL `replay` command against a captured ENBREPLAY S2C
# stream and exit. Decodes every frame through the record classes (known
# opcodes -> structured fields + hex tail; unknown opcodes -> ASCII-string
# heuristic + hex tail) so you can byte-skim retail captures without
# touching the docker stack. Default CAPTURE matches the on-disk file in
# archive/replay/. NO_COLOR strips ANSI when output is piped.
#
#   just cli-replay                    # capture_1-sector-s2c.bin
#   just cli-replay capture_2          # capture_2-sector-s2c.bin
#   NO_COLOR=1 just cli-replay | less  # plain-text scroll
#
# To replay a raw pcap from the proxy (converts pcap -> ENBREPLAY on the fly):
#   just pcap-replay proxy/local-debug/foo.pcap [server_ip] [client_ip]
cli-replay CAPTURE='capture_1':
    printf 'replay archive/replay/{{CAPTURE}}-sector-s2c.bin\nquit\n' | \
        dotnet run --project freya/cli-client/src/CliClient.App -- start

# Convert a raw pcap of server->proxy UDP traffic to ENBREPLAY and replay it.
# The pcap must contain 0x2016/0x201A PACKET_SEQUENCE frames (what the Net-7
# server sends to the proxy over UDP). Requires Python 3.
#
# Usage: just pcap-replay proxy/local-debug/foo.pcap SERVER_IP CLIENT_IP
#   (pass the reference server IP and the client/proxy IP -- no defaults)
pcap-replay PCAP SERVER CLIENT:
    #!/usr/bin/env bash
    set -euo pipefail
    tmp=$(mktemp /tmp/enbreplay-XXXXXX.bin)
    python3 tools/pcap-to-replay/pcap_to_replay.py \
        --pcap "{{PCAP}}" --out "$tmp" \
        --server "{{SERVER}}" --client "{{CLIENT}}" --verbose
    printf 'replay %s\nquit\n' "$tmp" | \
        dotnet run --project freya/cli-client/src/CliClient.App -- start
    rm -f "$tmp"

# Stream all logs in the foreground.
dev-fg:
    docker compose up

# Tear down (containers + network; named volume `pgdata` survives).
down:
    docker compose down

# Tear down AND wipe the pgdata volume (destructive — schema-init reloads next `just init`).
nuke:
    docker compose down -v

# Tail a service's logs.    e.g. `just logs server`
logs SERVICE='server':
    docker compose logs -f {{SERVICE}}

# Shell into a running service. e.g. `just shell mysql`
shell SERVICE='server':
    docker compose exec {{SERVICE}} bash

# Open a psql client against the dev net7_user DB (the one with accounts).
psql-user:
    docker compose exec -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user

# Seed a known-good test account into net7_user.accounts. Idempotent
# (DELETE-by-username then INSERT -- the schema has no UNIQUE on username,
# so a plain UPSERT isn't available). Default user/pass: testuser/testpass.
# Password is stored as an Argon2id PHC string in password_phc (Phase X
# replaced raw MD5 with libsodium-verified Argon2id, commit 12acf26).
# The PHC is generated on the host via PyNaCl (libsodium binding) using
# the INTERACTIVE profile, then handed to psql via :'phc' for safe
# SQL-literal interpolation. Requires python3-nacl on the host
# (`sudo apt install python3-nacl`).
seed-account USER='testuser' PASS='testpass':
    @phc=$(printf '%s' {{ quote(PASS) }} | python3 -c 'import nacl.pwhash,sys; sys.stdout.write(nacl.pwhash.argon2id.str(sys.stdin.buffer.read()).decode())'); \
        if [ -z "$phc" ]; then \
            echo "seed-account: failed to generate Argon2id PHC -- install python3-nacl (sudo apt install python3-nacl)" >&2; \
            exit 1; \
        fi; \
        printf '%s\n' \
            "-- Resync the identity sequence to the highest REAL account id, EXCLUDING" \
            "-- the reserved bot band (id >= 9000001). Syncing to a plain MAX(id) would" \
            "-- pull the sequence up to the AhBot sentinel, so the next signup overflows" \
            "-- the 32-bit GameID (account*5+1). See db/postgres/freya_online_bots.sql." \
            "SELECT setval('accounts_id_seq', GREATEST((SELECT COALESCE(MAX(id),0) FROM accounts WHERE id < 9000001), 1));" \
            "DELETE FROM accounts WHERE username = :'username';" \
            "INSERT INTO accounts (username, password_phc, status, formname, email)" \
            "VALUES (:'username', :'phc', 100, :'username' || '_form', :'username' || '@local');" \
        | docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -v ON_ERROR_STOP=1 \
            -v username={{ quote(USER) }} -v phc="$phc"
    @echo ">>> seeded {{USER}} / {{PASS}} (status=100)"

# Seed a dev account with a full roster: TW JE JW(jd) TT TE(ts), each at total
# level 75 (combat/explore/trade = 25 each), every class skill at max rank, and
# 10,000,000 credits. Characters are named <username><suffix> (Grievertw,
# Grieverje, Grieverjd, Grievertt, Grieverts). Unlike seed-account this is not
# pure SQL: it drives the CLI to create+enter each character so the SERVER lays
# down the starting gear / position / faction state correctly, then bumps the
# stored levels/credits/slots/skills. Honours ENB_NOREBUILD=1. Re-runnable: an
# existing roster for the account is wiped first. See tools/seed-dev-account/.
seed-dev-account USER='devuser' PASS='devpass':
    @tools/seed-dev-account/seed-dev-account.sh {{ quote(USER) }} {{ quote(PASS) }}

# Grant SKILL_PROSPECT (id 41) and a high Explore level to an existing
# character, so a Jenquai Explorer can mine without first completing the
# retail mission that normally grants Prospect (Skills.xml marks it
# Quest="1"). Prospect's per-class MaxLevel is 7 for the Explorer classes
# (Sentinel/Explorer/Scout) and 0 for everyone else, so granting it to a
# non-explorer is pointless -- the server clamps the loaded level to the
# class MaxLevel at login (server/src/PlayerSaves.cpp:668-675). This recipe
# warns if the target is not prof=2 (Explorer).
#
# Order of operations:
#   1. just seed-account <user> <pass>
#   2. just play-cli <unit>  ->  `create JE <name>`  (name must contain a vowel)
#   3. just grant-prospect <user>     <-- you are here
#   4. log out + back in (skills load from avatar_skill_levels at login)
#
# The skill row is loaded at login by PlayerSaves.cpp:653 and prospecting
# is gated only on Skill[SKILL_PROSPECT].GetLevel() != 0 (PlayerSkills.cpp:912),
# so level 1 is enough to enable mining; 7 is the JE cap.
#
# Args: USER (account username), SLOT (char slot, default 0),
#       LEVEL (prospect skill level 1..7, default 7),
#       EXPLORE (explore level, default 150 -- retail discipline cap).
grant-prospect USER SLOT='0' LEVEL='7' EXPLORE='150':
    @aid=$(printf '%s\n' \
            "SELECT i.avatar_id FROM avatar_info i JOIN accounts a ON a.id = i.account_id WHERE a.username = :'username' AND i.slot = :slot;" \
        | docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -tA \
            -v username={{ quote(USER) }} -v slot={{ quote(SLOT) }} 2>/dev/null); \
        if [ -z "$aid" ]; then \
            echo "grant-prospect: no character for account '{{USER}}' slot {{SLOT}} -- create one first (play-cli: create JE <name>)" >&2; \
            exit 1; \
        fi; \
        printf '%s\n' \
            "INSERT INTO avatar_skill_levels (avatar_id, skill_id, skill_level) VALUES (:aid, 41, :level) ON CONFLICT (avatar_id, skill_id) DO UPDATE SET skill_level = EXCLUDED.skill_level;" \
            "UPDATE avatar_info SET explore = :explore WHERE avatar_id = :aid;" \
            "SELECT i.avatar_id, d.first_name, d.race, d.prof, i.explore, s.skill_level AS prospect_level, CASE WHEN d.prof = 2 THEN 'OK (Explorer)' ELSE 'WARNING: not Explorer -- Prospect will clamp to 0 at login' END AS status FROM avatar_info i JOIN avatar_data d USING (avatar_id) LEFT JOIN avatar_skill_levels s ON s.avatar_id = i.avatar_id AND s.skill_id = 41 WHERE i.avatar_id = :aid;" \
        | docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -v ON_ERROR_STOP=1 \
            -v aid="$aid" -v level={{ quote(LEVEL) }} -v explore={{ quote(EXPLORE) }}
    @echo ">>> granted Prospect(41) + explore={{EXPLORE}} to {{USER}} slot {{SLOT}} -- log out and back in to load it"

# ---- Phase C continuation (Postgres) ----

# Bring up the postgres profile and apply the converted schema.
postgres-dev:
    docker compose --profile postgres up -d postgres
    docker compose --profile postgres up schema-init

psql:
    docker compose --profile postgres exec postgres psql -U net7 -d net7

apply-schema:
    docker compose --profile postgres run --rm schema-init

convert-schema:
    bash db/postgres/convert.sh

pgadmin:
    docker compose --profile dev-tools-postgres up -d pgadmin

# ---- test ----

# Run the gtest harness + (best-effort) dotnet test.
test:
    ctest --test-dir build/tests --output-on-failure
    -dotnet test tools/FreyaTools.slnx --nologo

# Freya Online backend integration tests against the live two-DB Postgres.
# Brings the docker postgres up (host localhost:5434) and runs the Go suite
# with FREYA_TEST_DB set so the DB-gated tests actually execute. Each test
# seeds + wipes its own reserved id band, so it is safe against a running stack.
test-online-it:
    docker compose up -d postgres schema-init
    cd freya/online/server && FREYA_TEST_DB=1 FREYA_TEST_DB_HOST=localhost:{{ENB_DB_PORT}} go test ./...

# Freya Online web SPA unit tests (rarity/format math, real-vs-mock API dispatch).
test-online-web:
    cd freya/online/web && npm test

# Live handshake + replay over TCP against the FreyaProxy. Reuses a
# proxy already listening on 127.0.0.1:3801 if one exists (e.g. you ran
# `just dev`); otherwise spins up a standalone one. Skips mysql + server
# boot — the proxy handshake path doesn't touch the DB so this stays fast.
integration-test:
    #!/usr/bin/env bash
    set -euo pipefail
    spawned=0
    if ! timeout 1 bash -c '</dev/tcp/127.0.0.1/3801' 2>/dev/null; then
        echo ">>> no proxy on tcp/3801; building + starting net7proxy-local"
        # Context = repo root: proxy/Dockerfile COPYs proxy/ AND common/
        # (Phase R headers). Matches cbacf78 for CI workflow.
        docker build -t enb-proxy:local -f proxy/Dockerfile .
        docker rm -f net7proxy-local 2>/dev/null || true
        docker run -d --name net7proxy-local -p 3801:3801 -p 3805:3805 -p 3500:3500 enb-proxy:local
        spawned=1
        echo ">>> waiting for proxy on tcp/3801..."
        until timeout 1 bash -c '</dev/tcp/127.0.0.1/3801' 2>/dev/null; do sleep 1; done
    else
        echo ">>> reusing existing proxy on tcp/3801"
    fi
    cmake -S freya/tests/server -B build/tests -G Ninja
    cmake --build build/tests --target handshake_live_test replay_test master_join_test version_request_test sector_login_test -j"$(nproc)"
    NET7_TEST_PROXY_HOST=127.0.0.1 NET7_TEST_PROXY_PORT=3801 NET7_TEST_GLOBAL_PORT=3805 NET7_TEST_SECTOR_PORT=3500 \
        ctest --test-dir build/tests --output-on-failure \
              -R 'HandshakeDriver|Replay|MasterJoin|VersionRequest|SectorLogin'
    if [ "$spawned" = "1" ]; then
        docker rm -f net7proxy-local
    fi

# Phase T: xUnit integration suite that drives CliClient.Core
# (Phase S library) against the live docker-compose stack
# (mysql + login + proxy + server). Reuses an existing
# `just dev`/`just run-stack-bg` stack if one is up by exporting
# CLI_INTEGRATION_SKIP_COMPOSE=1; otherwise the test fixture brings
# its own stack up + tears it down.
cli-integration:
    #!/usr/bin/env bash
    set -euo pipefail
    if timeout 1 bash -c '</dev/tcp/127.0.0.1/4443' 2>/dev/null \
    && timeout 1 bash -c '</dev/tcp/127.0.0.1/3801' 2>/dev/null \
    && timeout 1 bash -c '</dev/tcp/127.0.0.1/3805' 2>/dev/null \
    && timeout 1 bash -c '</dev/tcp/127.0.0.1/3500' 2>/dev/null; then
        echo ">>> reusing existing stack (login/proxy/sector ports listening)"
        export CLI_INTEGRATION_SKIP_COMPOSE=1
    else
        echo ">>> ServerFixture will own the docker-compose lifecycle"
    fi
    dotnet test freya/tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
        --logger "trx;LogFileName=cli-integration.trx" \
        --logger "console;verbosity=normal"

# Run ONLY the xUnit tests that don't need docker (Robustness +
# CaptureReplay + Smoke). Fast path for laptop development.
cli-integration-fast:
    CLI_INTEGRATION_SKIP_COMPOSE=1 \
    dotnet test freya/tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
        --filter "FullyQualifiedName~Robustness|FullyQualifiedName~Verification|FullyQualifiedName~Smoke"

# Warm-stack iteration workflow. Bring the docker-compose stack up
# ONCE, then run dotnet test repeatedly against it with
# CLI_INTEGRATION_SKIP_COMPOSE=1 so the ServerFixture skips its
# bring-up/tear-down. Cuts a typical wave-loop iteration from
# ~2-3 minutes (cold `docker compose up --wait` + sector marker poll
# + `down -v`) to ~10 seconds (just the actual test run).
#
# Usage:
#     just cli-int-up                 # once per session
#     just cli-int-run "FILTER"       # per wave
#     just cli-int-run                # all integration tests
#     just cli-int-down               # cleanup
cli-int-up:
    #!/usr/bin/env bash
    set -euo pipefail
    echo ">>> bringing up docker compose stack (warm-iteration mode)"
    docker compose up -d --wait
    echo ">>> waiting for server sector 10151 (Luna) marker in logs..."
    deadline=$(( $(date +%s) + 180 ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        if docker compose logs --no-color --no-log-prefix server 2>/dev/null \
                | grep -q 'BeginSectorThread sector_id=10151'; then
            echo ">>> sector 10151 ready; building test assembly"
            dotnet build freya/tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj --nologo -v quiet
            echo ">>> stack warm. Use \`just cli-int-run [filter]\` to run waves."
            exit 0
        fi
        sleep 2
    done
    echo "ERROR: sector 10151 did not load within 180s." >&2
    docker compose logs --tail=60 server >&2
    exit 1

# Run a single wave (or all of them) against the warm stack. Pass a
# filter expression -- usually a test name -- as the first arg.
# Empty arg runs the full integration suite.
cli-int-run FILTER='':
    #!/usr/bin/env bash
    set -euo pipefail
    if ! docker compose ps --status running --services 2>/dev/null | grep -q server; then
        echo "ERROR: docker compose stack is not up. Run \`just cli-int-up\` first." >&2
        exit 1
    fi
    if [ -n "{{FILTER}}" ]; then
        CLI_INTEGRATION_SKIP_COMPOSE=1 \
        dotnet test freya/tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
            --no-build \
            --filter "{{FILTER}}"
    else
        CLI_INTEGRATION_SKIP_COMPOSE=1 \
        dotnet test freya/tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
            --no-build
    fi

# Tear down the warm stack and wipe named volumes.
cli-int-down:
    docker compose down -v

# ---- package / release ----

# Build OCI images for server + login locally.
package:
    docker compose build server login

# Build + push OCI images to {{IMAGE_REGISTRY}}:{{IMAGE_TAG}}.
push:
    docker build -t {{IMAGE_REGISTRY}}/server:{{IMAGE_TAG}} server/
    docker build -t {{IMAGE_REGISTRY}}/login:{{IMAGE_TAG}}  login-server/
    docker push  {{IMAGE_REGISTRY}}/server:{{IMAGE_TAG}}
    docker push  {{IMAGE_REGISTRY}}/login:{{IMAGE_TAG}}

# ---- lint ----

# Lint: clang-format (new code only) + shellcheck. dotnet format is not
# run here because it does not yet understand the .slnx solution format
# we adopted in Phase D — re-enable when that lands upstream.
lint:
    -clang-format --dry-run --Werror tests/**/*.cpp server/compat/*.h 2>/dev/null
    shellcheck client/linux-installer/install-enb-linux.sh

# Apply clang-format in place to new code we own.
format:
    -clang-format -i tests/**/*.cpp server/compat/*.h

# ---- housekeeping ----

clean:
    rm -rf build/ tools/**/bin tools/**/obj

# Sanity-check that plans/ exists and has a status table.
verify-plans:
    @test -d plans || (echo "plans/ missing" && exit 1)
    @ls plans/00-master.md plans/01-phase-a-merge.md plans/02-phase-b-linux-server.md > /dev/null
    @grep -q "## Status table" plans/00-master.md || (echo "00-master.md missing status table" && exit 1)
    @echo "plans look OK"
