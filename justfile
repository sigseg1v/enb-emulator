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

# Bring up the dev stack (postgres, schema-init, server, login) detached.
dev:
    docker compose up -d postgres
    docker compose up schema-init
    docker compose up -d server login

# Same, but stream all logs in the foreground. Ctrl-C to stop.
dev-fg:
    docker compose up

# Tear down (containers + network; named volume `pgdata` survives).
down:
    docker compose down

# Tear down AND wipe the pgdata volume (destructive — schema is reapplied next `just dev`).
nuke:
    docker compose down -v

# Tail a service's logs.    e.g. `just logs server`
logs SERVICE='server':
    docker compose logs -f {{SERVICE}}

# Shell into a running service. e.g. `just shell postgres`
shell SERVICE='server':
    docker compose exec {{SERVICE}} bash

# psql into the dev postgres.
psql:
    docker compose exec postgres psql -U net7 -d net7

# Apply / re-apply the Postgres schema.
apply-schema:
    docker compose run --rm schema-init

# Re-run the MySQL -> Postgres conversion (produces db/postgres/schema.sql).
convert-schema:
    bash db/postgres/convert.sh

# Bring up pgadmin on http://localhost:8080 (opt-in profile).
pgadmin:
    docker compose --profile dev-tools up -d pgadmin

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

# Lint: clang-format (new code only), dotnet format, shellcheck.
lint:
    -clang-format --dry-run --Werror tests/**/*.cpp server/compat/*.h 2>/dev/null
    -dotnet format tools/Net7Tools.slnx --verify-no-changes --no-restore
    shellcheck client/linux-installer/install-enb-linux.sh

# Apply clang-format + dotnet format in place.
format:
    -clang-format -i tests/**/*.cpp server/compat/*.h
    -dotnet format tools/Net7Tools.slnx --no-restore

# ---- housekeeping ----

clean:
    rm -rf build/ tools/**/bin tools/**/obj

# Sanity-check that plans/ exists and has a status table.
verify-plans:
    @test -d plans || (echo "plans/ missing" && exit 1)
    @ls plans/00-master.md plans/01-phase-a-merge.md plans/02-phase-b-linux-server.md > /dev/null
    @grep -q "## Status table" plans/00-master.md || (echo "00-master.md missing status table" && exit 1)
    @echo "plans look OK"
