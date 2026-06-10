# SPDX-License-Identifier: MIT
# Freya Online -- login server + website image.
#
# Three stages: build the React SPA (web/dist), build the static Go binary
# (server/), then assemble a minimal runtime that serves both. Self-contained
# under freya/online -- no Net-7 source is needed to build this (MIT).

# ---- web ----
FROM node:20-alpine AS web
WORKDIR /web
COPY web/package.json ./
RUN npm install --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- go ----
FROM golang:1.25-alpine AS gobuild
WORKDIR /src
COPY server/go.mod server/go.sum ./
RUN go mod download
COPY server/ ./
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /freya-online .

# ---- runtime ----
FROM alpine:3.20
WORKDIR /app
COPY --from=gobuild /freya-online /app/freya-online
COPY --from=web /web/dist /app/web
# Runs as container-root, exactly like the sibling Net7SSL `login` service: the
# TLS private key (deploy/certs/*.pem) is mounted 0600 and is only readable by
# container-root. Under the rootless Docker daemon this stack runs on,
# container-root maps to the unprivileged host user -- it is NOT host root -- so
# this is not a privilege escalation, just the only uid that can read the key.
EXPOSE 443 8080
ENTRYPOINT ["/app/freya-online"]
