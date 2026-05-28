#!/usr/bin/env bash
# Build static OpenSSL 3 for MinGW-w64 (x86_64), installed to
# proxy/third_party/openssl-mingw64/. This prefix is what the proxy's
# Win32 cross-build links against; without it `cmake -B build-win64` fails
# at find_package(OpenSSL).
#
# Run from the repo root or from anywhere — paths are computed relative to
# this script. Idempotent: skips fetch + build if the prefix already has
# libssl.a + libcrypto.a + the headers.
#
# Why not commit the prefix: CLAUDE.md says "no binaries in git by
# default" — the exception is for binaries we can't rebuild from source.
# OpenSSL is public + buildable, so it lives outside the index.

set -euo pipefail

OPENSSL_VERSION="3.0.16"
OPENSSL_TARBALL_SHA256="57e03c50feab5d31b152af2b764f10379aecd8ee92f16c985983ce4a99f7ef86"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROXY_DIR="$(dirname "$SCRIPT_DIR")"
TP_DIR="$PROXY_DIR/third_party"
PREFIX="$TP_DIR/openssl-mingw64"
SRC_DIR="$TP_DIR/openssl-$OPENSSL_VERSION"
TARBALL="$TP_DIR/openssl-$OPENSSL_VERSION.tar.gz"
URL="https://www.openssl.org/source/openssl-$OPENSSL_VERSION.tar.gz"

# Already built?
if [[ -f "$PREFIX/lib64/libssl.a" && -f "$PREFIX/lib64/libcrypto.a" && -d "$PREFIX/include/openssl" ]]; then
    echo "openssl-mingw64 prefix already populated at $PREFIX — nothing to do."
    exit 0
fi

# MinGW toolchain present?
if ! command -v x86_64-w64-mingw32-gcc-posix >/dev/null && ! command -v x86_64-w64-mingw32-gcc >/dev/null; then
    cat >&2 <<EOF
error: MinGW-w64 toolchain not found.
On Debian/Ubuntu install with:
    sudo apt-get install -y mingw-w64 g++-mingw-w64-x86-64-posix
EOF
    exit 1
fi

mkdir -p "$TP_DIR"

# Fetch tarball if needed.
if [[ ! -f "$TARBALL" ]]; then
    echo "Fetching $URL"
    if command -v curl >/dev/null; then
        curl -fL "$URL" -o "$TARBALL"
    elif command -v wget >/dev/null; then
        wget -O "$TARBALL" "$URL"
    else
        echo "error: need curl or wget to fetch OpenSSL tarball" >&2
        exit 1
    fi
fi

# Verify checksum (optional but cheap).
if command -v sha256sum >/dev/null; then
    ACTUAL_SHA="$(sha256sum "$TARBALL" | awk '{print $1}')"
    if [[ "$ACTUAL_SHA" != "$OPENSSL_TARBALL_SHA256" ]]; then
        echo "error: $TARBALL sha256 mismatch" >&2
        echo "  expected: $OPENSSL_TARBALL_SHA256" >&2
        echo "  actual:   $ACTUAL_SHA" >&2
        exit 1
    fi
fi

# Extract.
if [[ ! -d "$SRC_DIR" ]]; then
    echo "Extracting $TARBALL"
    tar -xzf "$TARBALL" -C "$TP_DIR"
fi

# Configure + build + install.
cd "$SRC_DIR"
if [[ ! -f Makefile ]]; then
    echo "Configuring OpenSSL for mingw64"
    ./Configure mingw64 \
        --cross-compile-prefix=x86_64-w64-mingw32- \
        --prefix="$PREFIX" \
        no-shared no-asm no-tests
fi

echo "Building OpenSSL (this takes a few minutes)"
make -j"$(nproc)"
make install_dev

echo "OpenSSL static prefix ready at $PREFIX"
