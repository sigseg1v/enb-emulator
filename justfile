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

# Build the gtest harness (Phase G).
build-tests:
    cmake -S tests -B build/tests -G Ninja
    cmake --build build/tests -j"$(nproc)"

# ---- dev stack ----

# One-shot first-time setup: generate dev SSL certs, bring up mysql, wait
# for the schema-load init scripts to finish, smoke-check it's reachable.
init: gen-certs
    @echo ">>> bringing up mysql + loading dumps"
    docker compose up -d mysql
    @echo ">>> waiting for mysql to become healthy"
    @bash -c 'until [ "$$(docker inspect -f {{{{.State.Health.Status}}}} enb-emulator-mysql-1 2>/dev/null)" = "healthy" ]; do echo "  ...waiting"; sleep 3; done'
    @echo ">>> verifying net7 + net7_user databases"
    docker compose exec -T mysql mysql -unet7 -pnet7 -e "SHOW DATABASES; SELECT COUNT(*) AS account_rows FROM net7_user.accounts;"
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

# Bring up the full runtime stack (mysql + server + login). Server image
# is built on demand. Streams logs in the foreground; Ctrl-C to stop.
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

# Tear down (containers + network; named volume `mysqldata` survives).
down:
    docker compose down

# Tear down AND wipe the mysqldata volume (destructive — dumps reload next `just init`).
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
