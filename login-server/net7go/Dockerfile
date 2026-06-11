# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Copyright (c) 2010 Net-7 Entertainment, Ltd.
# Modified by: Max Verigin, 2026 -- ported to go
#
# net7go -- standalone Go reimplementation of the Net7SSL game-auth server.
# This is a direct functional port of login-server/Net7SSL and inherits the
# upstream Net-7 CC BY-NC-SA 3.0 license (share-alike); it is NOT MIT and is
# NOT part of the freya/* tree. It links no Freya code and is independently
# distributable. See login-server/net7go/README.md.
#
# Two stages: build the static Go binary, then a minimal runtime. The build
# context is this directory (login-server/net7go); the module is self-contained.

# ---- build ----
FROM golang:1.25-alpine AS gobuild
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY *.go ./
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /net7go .

# ---- runtime ----
FROM alpine:3.20
WORKDIR /app
COPY --from=gobuild /net7go /app/net7go
# Plain HTTP only -- TLS is terminated by the Freya Online server, which
# raw-relays the legacy game-auth URIs here. net7go also binds the AF_UNIX
# keepalive socket under /run/net7-ipc (mounted shared with the game server).
EXPOSE 8085
ENTRYPOINT ["/app/net7go"]
