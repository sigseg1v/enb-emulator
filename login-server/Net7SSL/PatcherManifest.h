// PatcherManifest.h -- Phase AN launcher self-update support.
//
// Holds the authoritative SHA-512 hashes of the three published Windows
// artifacts (FreyaLauncher.exe, FreyaLauncher.cfg, bin/FreyaProxy.exe). The
// cache is populated ONCE at login-server startup by an HTTPS GET of a small
// manifest.json over CloudFront (NET7_PATCHER_MANIFEST_URL). libcurl also reads
// file:// URLs, so the local-stub test points the env at an on-disk manifest --
// no TLS, no cloud.
//
// No TTL: the cache is refreshed by restarting the login server, which
// `just update` does. Reads on the /updateCheck request path are thread-safe.
//
// Fail-closed: until a successful Load(), Empty() is true and /updateCheck
// reports the server DOWN (HTTP 503). The endpoint NEVER reads a local file
// chosen by the client and never reflects client input into a path -- it only
// ever returns these three fixed, server-controlled entries.

#pragma once

#include <string>
#include <mutex>

class PatcherManifest
{
public:
    static PatcherManifest &Instance();

    // Fetch + parse NET7_PATCHER_MANIFEST_URL. Returns true only when all three
    // known files are present with non-empty hashes. A missing env var, a
    // transport error, or a malformed manifest leaves the cache Empty() and
    // returns false (logged by the caller as a deploy error). Safe to call once
    // at startup; not intended for hot-path re-entry.
    bool Load();

    bool Empty() const;            // true until a successful Load()

    std::string LauncherExeHash() const;
    std::string LauncherCfgHash() const;
    std::string ProxyExeHash() const;

    // CloudFront base URL for the published files (NET7_PATCHER_DL_BASE),
    // trailing slash trimmed. Files are flat in the bucket; the launcher maps
    // them to its own relative layout (bin/FreyaProxy.exe) on download.
    std::string DlBase() const;

private:
    PatcherManifest() = default;

    mutable std::mutex m_mutex;
    bool m_loaded = false;
    std::string m_launcherExe;
    std::string m_launcherCfg;
    std::string m_proxyExe;
    std::string m_dlBase;
};
