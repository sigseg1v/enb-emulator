# login-server/Dockerfile
#
# Scaffolding only. The login-server tree (Net7Mysql + Net7SSL) currently
# ships .vcproj / .sln Visual Studio 2008 project files and a `.mak` file
# — no CMake yet. Phase B continuation will write a CMakeLists.txt at
# login-server/CMakeLists.txt that builds both subprojects.
#
# Once that exists, this Dockerfile will use it. Until then, the build
# stage below is intentionally a placeholder that exits successfully on
# a "no CMakeLists yet" sentinel so the rest of the docker-compose stack
# can come up without this image being a hard dependency.

# ---------- build stage ----------
FROM ubuntu:24.04 AS build

ENV DEBIAN_FRONTEND=noninteractive
ENV OPENSSL_API_COMPAT=0x10100000L

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        g++ \
        cmake \
        ninja-build \
        pkg-config \
        libssl-dev \
        liblua5.4-dev \
        liblua5.4-0 \
        libtinyxml-dev \
        libcrypto++-dev \
        libpq-dev \
        libpqxx-dev \
        libmariadb-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . /src/login-server/

# If a CMakeLists.txt exists, build it. Otherwise leave a stub binary
# and a marker file so the runtime image still assembles. Phase B
# continuation removes the conditional.
RUN if [ -f /src/login-server/CMakeLists.txt ]; then \
        cmake -S /src/login-server -B /build -G Ninja \
        && cmake --build /build -j"$(nproc)"; \
    else \
        mkdir -p /build \
        && printf '#!/bin/sh\necho "login-server: not yet built (Phase B continuation pending)" >&2\nexec sleep infinity\n' \
            > /build/login-server \
        && chmod +x /build/login-server \
        && echo "Phase B continuation: write login-server/CMakeLists.txt to enable a real build." \
            > /build/STUB.txt; \
    fi

# ---------- runtime stage ----------
FROM ubuntu:24.04 AS runtime

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        libssl3 \
        liblua5.4-0 \
        libtinyxml2.6.2v5 \
        libcrypto++8 \
        libpqxx-7.7 \
        libmariadb3 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system net7 \
    && useradd  --system --gid net7 --home /app net7

WORKDIR /app
COPY --from=build /build/login-server /app/login-server
# Best-effort: copy the stub marker if it exists.
COPY --from=build /build/STUB.txt /app/STUB.txt

# Placeholder port range until Phase B confirms login/SSL ports.
EXPOSE 7800-7810/tcp

USER net7
CMD ["/app/login-server"]
