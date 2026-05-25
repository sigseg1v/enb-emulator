# justfile — build / dev / test / package targets.
#
# Requires https://github.com/casey/just (`apt install just` on recent
# Debian/Ubuntu, or `cargo install just`).
#
# Most targets call docker/cmake/dotnet; none of them require root.

IMAGE_REGISTRY := env_var_or_default("IMAGE_REGISTRY", "ghcr.io/anthropics/enb-emulator")
IMAGE_TAG      := env_var_or_default("IMAGE_TAG", "dev")

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
    @bash -c 'until [ "$$(docker inspect -f {{{{.State.Health.Status}}}} enb-emulator-postgres-1 2>/dev/null)" = "healthy" ]; do echo "  ...waiting"; sleep 3; done'
    @echo ">>> waiting for schema-init to finish"
    @bash -c 'until docker inspect -f "{{{{.State.Status}}}}" enb-emulator-schema-init-1 2>/dev/null | grep -q exited; do echo "  ...waiting"; sleep 2; done'
    @echo ">>> verifying net7 + net7_user databases"
    docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -l
    docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -c "SELECT COUNT(*) AS account_rows FROM accounts;"
    @echo ">>> init complete. Next: 'just run-stack'"

# Generate the self-signed dev cert pair the server expects to find at
# CWD as <g_DomainName>.cer / .pem (SSL_Listener.cpp:56-57).
gen-certs:
    @mkdir -p deploy/certs
    @if [ ! -f deploy/certs/local.net-7.org.cer ]; then \
        echo ">>> generating self-signed cert for local.net-7.org"; \
        openssl req -x509 -newkey rsa:2048 -days 3650 -nodes \
            -subj "/CN=local.net-7.org/O=Earth-and-Beyond Emulator Dev/C=US" \
            -addext "subjectAltName=DNS:local.net-7.org,DNS:localhost,IP:127.0.0.1" \
            -keyout deploy/certs/local.net-7.org.pem \
            -out    deploy/certs/local.net-7.org.cer; \
    else \
        echo "deploy/certs/local.net-7.org.cer exists, skipping"; \
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

# Open a mysql client against the dev DB.
mysql:
    docker compose exec mysql mysql -unet7 -pnet7 net7

# Seed a known-good test account into net7_user.accounts. Idempotent.
# Default user/pass: testuser/testpass. Password is stored as upper-case MD5
# (matches Net7SSL hash form). Schema is the 2010 dump's `accounts` table:
#   id (PK), username, password, status, formname, email, last_login,
#   last_logout, warn_level.
seed-account USER='testuser' PASS='testpass':
    #!/usr/bin/env bash
    set -euo pipefail
    MD5=$(printf "%s" "{{PASS}}" | md5sum | awk '{print toupper($1)}')
    docker compose exec -T mysql mysql -unet7 -pnet7 net7_user -e \
        "INSERT INTO accounts (username, password, status, formname, email) \
         VALUES ('{{USER}}', '$MD5', 100, 'forum_{{USER}}', '{{USER}}@local') \
         ON DUPLICATE KEY UPDATE password='$MD5';"
    echo ">>> seeded {{USER}} with MD5($MD5)"

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
