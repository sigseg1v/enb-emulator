# justfile — build / dev / test / package targets.
#
# Requires https://github.com/casey/just (`apt install just` on recent
# Debian/Ubuntu, or `cargo install just`).
#
# Most targets call docker/cmake/dotnet; none of them require root.

IMAGE_REGISTRY := env_var_or_default("IMAGE_REGISTRY", "ghcr.io/anthropics/enb-emulator")
IMAGE_TAG      := env_var_or_default("IMAGE_TAG", "dev")

# Per-worktree docker compose project name, derived from the current git
# branch so parallel worktrees don't fight over the same container set.
# main/master/detached-HEAD collapse to plain `enb-emulator`. Already-prefixed
# branches (enb-emulator-foo) are used as-is. Override with the env var.
#
# Note: only the container/network/volume *names* are namespaced. Host
# port bindings in docker-compose.yml are still fixed at the conventional
# defaults, so only one worktree at a time can run its stack.
export COMPOSE_PROJECT_NAME := env_var_or_default("COMPOSE_PROJECT_NAME", `b=$(git branch --show-current 2>/dev/null); if [ -z "$b" ] || [ "$b" = main ] || [ "$b" = master ]; then echo enb-emulator; else s=$(printf '%s' "$b" | tr 'A-Z' 'a-z' | tr -c 'a-z0-9_-' '-' | tr -s '-' | sed 's/^-//;s/-$//'); case "$s" in enb-emulator-*) echo "$s";; *) echo "enb-emulator-$s";; esac; fi`)

# Default: list targets.
default:
    @just --list

_default: default

# ---- build ----

# Build the C++ server (Phase B: best-effort, may fail mid-build).
build:
    cmake -S server -B build/server -G Ninja
    cmake --build build/server -j"$(nproc)"

# Build the C# tool suite (Net7Tools.slnx, .NET 10).
build-tools:
    dotnet build tools/Net7Tools.slnx

# Cross-compile Net7Proxy as a Win32 PE binary (MinGW-w64). The launcher
# spawns this under WINE next to the EnB client — see plans/23-phase-w-proxy-win32-crossbuild.md.
# Builds OpenSSL 3 statically into proxy/third_party/openssl-mingw64 the
# first time (idempotent), then cmake-configures + builds, then stages
# Net7Proxy.exe to ./bin/ where launchnet7-avalonia looks for it.
build-proxy-win64:
    @echo ">>> building static OpenSSL 3 for MinGW (idempotent — skip if already built)"
    ./proxy/scripts/build-openssl-mingw.sh
    @echo ">>> cmake configure (Win32 cross)"
    cmake -S proxy -B proxy/build-win64 \
        -DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64-x86_64.toolchain.cmake \
        -DCMAKE_BUILD_TYPE=Release
    @echo ">>> cmake build"
    cmake --build proxy/build-win64 -j"$(nproc)"
    @echo ">>> staging Net7Proxy.exe → bin/"
    @mkdir -p bin
    @cp proxy/build-win64/Net7Proxy.exe bin/Net7Proxy.exe
    @echo ">>> done. bin/Net7Proxy.exe is what 'just launch-net7' will spawn under WINE."

# Smoke-run Net7Proxy.exe under WINE (no game client, just the proxy).
# Confirms WSAStartup + binds TCP 3801/3805 + opens both UDP planes.
# Set NET7_UPSTREAM_HOST=<host> in the env to point the proxy at a non-local
# game server. Ctrl-C to stop.
run-proxy-wine:
    @if [ ! -x bin/Net7Proxy.exe ]; then echo "bin/Net7Proxy.exe missing — run 'just build-proxy-win64' first" >&2; exit 1; fi
    wine bin/Net7Proxy.exe

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
# startup — point it at the dev stack (`just init` first, then
# host=localhost port=5434 user=net7 pass=net7; Phase N: Postgres, was
# MySQL on 3307 before). Tools that don't talk to the DB (toolslauncher,
# launchnet7, enbpatcher, toolspatcher, w3d-parser, talktreeeditor) skip
# the login dialog.

# Central launcher GUI — button per editor; spawns Avalonia projects.
launch:
    dotnet run --project tools/toolslauncher-avalonia

# Game client launcher (LaunchNet7 port).
launch-net7:
    dotnet run --project tools/launchnet7-avalonia

# Effect / particle / stat editor (DB).
launch-effect-editor:
    dotnet run --project tools/effect-editor-avalonia

# NPC faction relationships editor (DB).
launch-faction-editor:
    dotnet run --project tools/faction-editor-avalonia

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
    cmake -S tests -B build/tests -G Ninja
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
run-stack: init
    docker compose up server login

# Same but detached.
run-stack-bg: init
    docker compose up -d server login

# Convenience: legacy name. Same as run-stack-bg.
dev: run-stack-bg

# Bring up the local stack + launch the launcher pre-configured to connect.
#
# With no args, defaults to the linux-installer's default install location:
#   $HOME/.wine-enb/drive_c/Program Files/EA GAMES/Earth & Beyond/release/client.exe
# Override either as a recipe arg or via the ENB_CLIENT_PATH env var:
#   just play-local /home/me/.wine/drive_c/.../release/client.exe
#   ENB_CLIENT_PATH=... just play-local
#
# Steps the recipe performs:
#   1. `just run-stack-bg`           — postgres + server + login (no proxy).
#   2. `just stop-docker-proxy`      — the WINE proxy binds the same host
#                                      ports as the docker proxy.
#   3. `just build-proxy-win64`      — idempotent; ensures bin/Net7Proxy.exe.
#   4. Pre-writes LaunchNet7.settings.json so the launcher opens with
#      Emulator=Net7Local, Host=localhost, port 4443 (the dev stack's
#      host-side mapping of the login container's 443). The launcher's
#      in-process LocalAuthRelay terminates the client's plaintext-HTTP
#      auth call on 127.0.0.1 and re-wraps it as TLS to the upstream —
#      so we don't need the WINE prefix to trust the dev cert (verify
#      is skipped only because upstream is loopback).
#   5. Runs the launcher so the spawned WINE proxy knows where to forward.
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

    echo ">>> bringing up local stack (postgres + server + login)"
    just run-stack-bg

    echo ">>> stopping docker proxy if running (WINE proxy will take its place)"
    just stop-docker-proxy >/dev/null 2>&1 || true

    echo ">>> ensuring bin/Net7Proxy.exe is built"
    just build-proxy-win64

    echo ">>> building launcher (so its output dir exists for settings.json)"
    dotnet build tools/launchnet7-avalonia >/dev/null

    SETTINGS_DIR=tools/launchnet7-avalonia/bin/Debug/net10.0
    mkdir -p "$SETTINGS_DIR"
    cp_json=$(printf '%s' "$cp" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')
    cat > "$SETTINGS_DIR/LaunchNet7.settings.json" <<JSON
    {
      "ClientPath": $cp_json,
      "LastEmulatorName": "Net7Local",
      "LastServerName": "localhost",
      "UseClientDetours": false,
      "UseLocalCert": false,
      "UseSecureAuthentication": true,
      "AuthenticationPort": "4443",
      "FormMainPositionX": -1,
      "FormMainPositionY": -1
    }
    JSON
    echo ">>> wrote $SETTINGS_DIR/LaunchNet7.settings.json"

    : "${WINEPREFIX:=$HOME/.wine-enb}"
    export WINEPREFIX

    # UDP routing for proxy<->server depends on which docker mode is active.
    #
    # ROOTFUL docker: the bridge for our compose network is a real host
    # interface (br-XXXX). The host has a direct route to 172.x.0.0/16, so
    # talking to the server container's bridge IP works and avoids the
    # MASQUERADE conntrack pitfall on the sector reverse-push (server's
    # MVASauth at 3806 -> proxy ephemeral, a 5-tuple the published-port
    # DNAT never saw on the outbound TICKET path, so reverse-NAT drops it).
    # Bridge IP -> server captures the proxy's real source port and the
    # reverse push back to 172.x.0.1:eph is normal local delivery to the
    # proxy's INADDR_ANY-bound socket.
    #
    # ROOTLESS docker: the compose network's bridge lives INSIDE the
    # rootless user namespace -- there is no host-side bridge interface,
    # and `ip route get 172.x.0.4` falls through to the default route
    # (off-host LAN gateway). Targeting the bridge IP from the host sends
    # packets out the LAN, where they are dropped. We must fall back to
    # localhost + the docker-userland-proxy port forwarder. Login
    # (TICKET -> AVATARLIST) works through the userland proxy because the
    # request creates a forwarder session. The sector reverse-push from
    # 3806 may still not survive (different source port than the session
    # the forwarder tracks). If sector zone-in hangs, run the dockerised
    # proxy instead via `just dev`.
    # Detect rootless docker. `docker info` lists "rootless" under
    # SecurityOptions in rootless mode; the daemon socket path also
    # encodes it as /run/user/$UID/docker.sock.
    ROOTLESS=""
    if docker info 2>/dev/null | grep -qi 'rootless'; then
        ROOTLESS=1
    fi

    if [ -n "$ROOTLESS" ]; then
        echo ">>> detected rootless docker -- using localhost for UDP (bridge IP unreachable from host)"
        echo "    sector zone-in may hang in rootless mode; if so, use \`just dev\` for the dockerised proxy."
        SERVER_IP=localhost
    else
        SERVER_IP=$(docker inspect "${COMPOSE_PROJECT_NAME:-enb-emulator}-server-1" 2>/dev/null \
            | grep -m1 '"IPAddress":' \
            | sed -E 's/.*"IPAddress": "([0-9.]+)".*/\1/')
        if [ -z "$SERVER_IP" ]; then
            echo "play-local: WARNING couldn't resolve server container bridge IP." >&2
            echo "  Falling back to localhost; in-game zone-in will likely time out." >&2
            echo "  (Check: docker compose ps server)" >&2
            SERVER_IP=localhost
        fi
        echo ">>> resolved server container bridge IP: $SERVER_IP"
    fi

    echo ">>> launching (WINEPREFIX=$WINEPREFIX, NET7_UPSTREAM_HOST=localhost, NET7_GAME_SERVER_HOST=$SERVER_IP) -- click Play in the GUI"
    NET7_UPSTREAM_HOST=localhost \
    NET7_GAME_SERVER_HOST="$SERVER_IP" \
        dotnet run --no-build --project tools/launchnet7-avalonia

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
# (DELETE-by-username then INSERT — the schema has no UNIQUE on username,
# so a plain UPSERT isn't available). Default user/pass: testuser/testpass.
# Password is stored as upper-case MD5, matching what Net7SSL's LinuxAuth
# compares against. pgcrypto's digest() does the hashing server-side.
seed-account USER='testuser' PASS='testpass':
    docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -v ON_ERROR_STOP=1 -c \
        "CREATE EXTENSION IF NOT EXISTS pgcrypto; \
         SELECT setval('accounts_id_seq', GREATEST((SELECT COALESCE(MAX(id),0) FROM accounts), 1)); \
         DELETE FROM accounts WHERE username = '{{USER}}'; \
         INSERT INTO accounts (username, password, status, formname, email) \
         VALUES ('{{USER}}', UPPER(encode(digest('{{PASS}}', 'md5'), 'hex')), 100, '{{USER}}_form', '{{USER}}@local');"
    @echo ">>> seeded {{USER}} / {{PASS}} (status=100)"

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

phpmyadmin:
    docker compose --profile dev-tools up -d phpmyadmin

# ---- test ----

# Run the gtest harness + (best-effort) dotnet test.
test:
    ctest --test-dir build/tests --output-on-failure
    -dotnet test tools/Net7Tools.slnx --nologo

# Live handshake + replay over TCP against the Net7Proxy. Reuses a
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
    cmake -S tests -B build/tests -G Ninja
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
    dotnet test tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
        --logger "trx;LogFileName=cli-integration.trx" \
        --logger "console;verbosity=normal"

# Run ONLY the xUnit tests that don't need docker (Robustness +
# CaptureReplay + Smoke). Fast path for laptop development.
cli-integration-fast:
    CLI_INTEGRATION_SKIP_COMPOSE=1 \
    dotnet test tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
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
            dotnet build tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj --nologo -v quiet
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
        dotnet test tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
            --no-build \
            --filter "{{FILTER}}"
    else
        CLI_INTEGRATION_SKIP_COMPOSE=1 \
        dotnet test tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj \
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
