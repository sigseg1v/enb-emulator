# login-server/Dockerfile
#
# Multi-stage build of the Net7SSL authentication listener.
#
# Phase J: this image's job is to bind TCP 443 (SSL_PORT) and accept SSL
# handshakes. The 6.7K LOC under login-server/Net7SSL/ that depends on
# Connection_B, AccountManager (mysqlplus), MailslotManager, UDPClient,
# and the rest of the WIN32-only Connection-management tree is walled
# behind `#ifdef WIN32` at source level and compiles to nothing on Linux.
# The Linux build path stands up the SSL_Listener, drives SSL_accept()
# inline for each connection, and closes. Opcode dispatch and MySQL
# account lookup remain WIN32-only for Phase J.
#
# Modelled on proxy/Dockerfile.

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
        libsodium-dev \
        libpq-dev \
        libpqxx-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
# Build context is the repo root (docker-compose context: .); copy only
# what the login build needs: login-server/ itself and common/ (Phase R
# shared headers + Phase M net7ipc::PosixIpc).
COPY login-server/ /src/login-server/
COPY common/       /src/common/

RUN cmake -S /src/login-server/Net7SSL -B /build -G Ninja \
    && cmake --build /build -j"$(nproc)" -- -k 0

# ---------- runtime stage ----------
FROM ubuntu:24.04 AS runtime

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        libssl3 \
        libsodium23 \
        libpqxx-7.8t64 \
        iproute2 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system net7 \
    && useradd  --system --gid net7 --home /app net7

WORKDIR /app
COPY --from=build /build/net7ssl /app/net7ssl
RUN mkdir -p /app/logs && chown -R net7:net7 /app

# Net7SSL listener footprint (login-server/Net7SSL/Net7SSL.h):
#   443   TCP  SSL_PORT (Westwood SSL auth — primary)
# Other listed ports (3805 GLOBAL, 3807 LOCALCERT_LOGIN, 3808 UDP_MASTER,
# 3809 PROXY) are owned by other services; this container only binds 443.
EXPOSE 443/tcp

# Port 443 is privileged. The container's net7 user can't bind it by
# default; running as root is the simplest path inside the dev-stack
# container. Production would set CAP_NET_BIND_SERVICE on the binary.
# USER net7
CMD ["/app/net7ssl"]
