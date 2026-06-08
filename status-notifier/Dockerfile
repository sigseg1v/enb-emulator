# status-notifier sidecar (Phase AM). Relays the game server's
# external_status_events outbox to a webhook. Talks ONLY to Postgres (net7_user)
# and outbound HTTPS -- no EnB protocol, no game ports.
FROM golang:1.25-alpine AS build
WORKDIR /src

# Cache the module download layer: only re-fetch when go.mod/go.sum change.
COPY go.mod go.sum ./
RUN go mod download

COPY . .
# Static build so the final image can be distroless/scratch-thin.
RUN CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o /out/status-notifier .

# Minimal runtime. Needs CA certs for the outbound HTTPS POST to the webhook.
FROM alpine:3.20
RUN apk add --no-cache ca-certificates
COPY --from=build /out/status-notifier /usr/local/bin/status-notifier
# Runs as a non-root user -- it needs no privileges.
USER nobody
ENTRYPOINT ["/usr/local/bin/status-notifier"]
