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
        libmysqlclient-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . /src/server/

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
        libcrypto++8 \
        libpqxx-7.7 \
        libmysqlclient21 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system net7 \
    && useradd  --system --gid net7 --home /app net7

WORKDIR /app
COPY --from=build /build/net7 /app/net7

# Ports TBD — Phase B determines the actual server port plan. The legacy
# server uses a range starting around 7000. We expose a placeholder range
# here so docker-compose can publish them; tighten in Phase B continuation.
EXPOSE 7000-7100/udp
EXPOSE 7000-7100/tcp

USER net7
CMD ["/app/net7"]
