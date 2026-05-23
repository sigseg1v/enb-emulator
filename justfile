# justfile — build / dev / test / package targets.
#
# Requires https://github.com/casey/just (`apt install just` on recent
# Debian/Ubuntu, or `cargo install just`).
#
# Most targets call docker/cmake/dotnet; none of them require root.

# Default: list targets.
default:
    @just --list

_default: default

# Build the C++ server. EXPECTED to fail mid-build during Phase B —
# that's what the BUILD_ERRORS.md tracking is for.
build:
    cmake -S server -B build/server -G Ninja
    cmake --build build/server -j"$(nproc)"

# Build the C# tool suite. Requires Phase D upgrade (csproj -> SDK style)
# before this succeeds.
build-tools:
    dotnet build tools/Net7Tools.sln

# Dev stack: postgres, schema apply, server + login.
dev:
    docker compose up -d postgres
    docker compose up schema-init
    docker compose up -d server login

down:
    docker compose down

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

# Run such tests as exist. Both halves currently expected to fail or be
# empty; the `|| true` keeps `just test` from being a hard error during
# Phase B/D.
test:
    cmake --build build/server --target test 2>&1 || true
    dotnet test tools/Net7Tools.sln 2>&1 || true

# Build OCI images for server + login.
package:
    docker compose build server login

# Lint placeholder. Phase F wires up clang-format and `dotnet format`.
lint:
    @echo "TODO: clang-format + dotnet format"

clean:
    rm -rf build/ tools/**/bin tools/**/obj

# Sanity check that the plan files exist and the master file still has
# its status table. Useful in CI so a deleted/renamed plan is caught
# immediately.
verify-plans:
    @test -d plans || (echo "plans/ missing" && exit 1)
    @ls plans/00-master.md plans/01-phase-a-merge.md plans/02-phase-b-linux-server.md > /dev/null
    @grep -q "## Status table" plans/00-master.md || (echo "00-master.md missing status table" && exit 1)
    @echo "plans look OK"
