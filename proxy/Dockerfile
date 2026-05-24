# proxy/Dockerfile
#
# Multi-stage build of the Net7Proxy server-side TCP listener.
#
# Phase J: this image's job is to bind TCP 3801 (MASTER_SERVER_PORT) and
# 3805 (GLOBAL_SERVER_PORT) and accept connections. The legacy 9.5K LOC
# of client-side launcher code under proxy/ is wrapped in `#ifdef WIN32`
# at source level and does not compile on Linux.
#
# Modelled on server/Dockerfile.

# ---------- build stage ----------
FROM ubuntu:24.04 AS build

ENV DEBIAN_FRONTEND=noninteractive
ENV OPENSSL_API_COMPAT=0x30000000L

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        g++ \
        cmake \
        ninja-build \
        pkg-config \
        libssl-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
# Build context is the repo root (docker-compose context: .); copy only
# what the proxy build needs: proxy/ itself and common/ (Phase R shared
# headers + Phase M net7ipc::PosixIpc).
COPY proxy/  /src/proxy/
COPY common/ /src/common/

RUN cmake -S /src/proxy -B /build -G Ninja \
    && cmake --build /build -j"$(nproc)" -- -k 0

# ---------- runtime stage ----------
FROM ubuntu:24.04 AS runtime

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        libssl3 \
        iproute2 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system net7 \
    && useradd  --system --gid net7 --home /app net7

WORKDIR /app
COPY --from=build /build/net7proxy /app/net7proxy
RUN mkdir -p /app/logs && chown -R net7:net7 /app

# Net7Proxy listener footprint (proxy/Net7.h ports):
#   3801  TCP  MASTER_SERVER_PORT (Westwood RSA+RC4 handshake)
#   3805  TCP  GLOBAL_SERVER_PORT
#   443   TCP  SSL_PORT (Net7SSL auth — currently stubbed Linux-side)
EXPOSE 3801/tcp
EXPOSE 3805/tcp

USER net7
CMD ["/app/net7proxy"]
