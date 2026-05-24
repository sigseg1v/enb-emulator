# server/Dockerfile
#
# Multi-stage build of the Net-7 server.
#
# NOTE: this is Phase B scaffolding. The build stage WILL currently fail
# because the server doesn't compile end-to-end yet. The image definition
# is in place so CI can wire it up and so Phase B iteration has somewhere
# to land.

# ---------- build stage ----------
FROM ubuntu:24.04 AS build

ENV DEBIAN_FRONTEND=noninteractive
# Phase E completed: server/src is clean against OpenSSL 3.0.
# CMakeLists.txt now bakes in OPENSSL_API_COMPAT=0x30000000L; this
# env var is kept as a safety net for out-of-tree consumers.
ENV OPENSSL_API_COMPAT=0x30000000L

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
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
# Build context is the repo root (docker-compose context: .); copy only
# what the server build needs: server/ itself and common/ (Phase R shared
# headers + Phase M net7ipc::PosixIpc).
COPY server/ /src/server/
COPY common/ /src/common/

RUN cmake -S /src/server -B /build -G Ninja \
    && cmake --build /build -j"$(nproc)" -- -k 0

# ---------- runtime stage ----------
FROM ubuntu:24.04 AS runtime

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        libssl3 \
        liblua5.4-0 \
        libtinyxml2.6.2v5 \
        libcrypto++8t64 \
        libpqxx-7.8t64 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system net7 \
    && useradd  --system --gid net7 --home /app net7

WORKDIR /app
COPY --from=build /build/net7 /app/net7
# Provide a logs/ subdir; Net7.cpp:235 calls _mkdir(SERVER_LOGS_PATH)
# on boot but we make it owned by the runtime user up-front to avoid
# any permission surprise under read-only volume mounts.
RUN mkdir -p /app/logs && chown -R net7:net7 /app

# Net-7 listener footprint (server/src/Net7.h):
#   3805  TCP  Global server entry
#   3801  TCP  Master server
#   3501+ TCP  Sector servers (dynamic allocation)
#   3808  UDP  Master <-> proxy
EXPOSE 3805/tcp
EXPOSE 3801/tcp
EXPOSE 3501-3800/tcp
EXPOSE 3808/udp

USER net7
# Net7Config.cfg, local.net-7.org.{cer,pem} are bind-mounted by
# docker-compose.yml into /app/ at runtime.
CMD ["/app/net7"]
