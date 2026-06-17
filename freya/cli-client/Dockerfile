# freya/cli-client/Dockerfile
#
# Containerised build of the headless C# CLI client (Phase S). The image
# exists so multiple clients can drive the dev stack at once: the Net7Proxy
# is a SINGLE-client bridge (one global ServerManager + g_LoggedIn + singular
# m_{Sector,Global,Master}Connection in proxy/ServerManager.h), so every
# client needs its OWN proxy. docker-compose.cli.yml pairs one of these CLI
# containers with one proxy on a private network, joined to the shared stack
# network; scale by project name (see `just play-cli`).
#
# Build context is the repo root (docker-compose context: .), matching the
# proxy/server images. We mirror the repo layout under /src so the relative
# ProjectReference from CliClient.Core to proxy/WestwoodCrypto/ resolves: the
# Westwood RSA/RC4 handshake lives OUTSIDE freya/ (own assembly, separate
# licensing bucket -- see proxy/WestwoodCrypto/), so the CLI build needs both
# trees, not just freya/cli-client/.

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY freya/cli-client/ /src/freya/cli-client/
COPY proxy/WestwoodCrypto/ /src/proxy/WestwoodCrypto/
# freya/cli-client/Directory.Build.props pulls the repo-root stylecop.json as an
# AdditionalFile, and the SA* severities live in the repo-root .editorconfig.
# Both sit above the copied trees, so copy them in or the StyleCop analyzers
# throw (missing settings file -> AD0001 / CS2001) and the publish fails.
COPY stylecop.json /src/stylecop.json
COPY .editorconfig /src/.editorconfig

# Framework-dependent publish: the runtime image carries the .NET runtime, so
# we ship only the app. UseAppHost gives the native `enb-cli` launcher
# (AssemblyName in CliClient.App.csproj).
RUN dotnet publish freya/cli-client/src/CliClient.App/CliClient.App.csproj \
        -c Release -o /app /p:UseAppHost=true

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# Invariant globalization: no ICU dependency, and it matches how the suite and
# the in-network runs drive the CLI (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1).
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

WORKDIR /app
COPY --from=build /app /app

# Default to the interactive REPL. Run with `-it` (compose `run`/`attach`) so
# the line editor sees a TTY and turns on colour; piped stdin falls back to a
# plain reader automatically.
ENTRYPOINT ["/app/enb-cli"]
CMD ["repl"]
