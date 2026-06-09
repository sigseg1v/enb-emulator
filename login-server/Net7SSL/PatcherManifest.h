// PatcherManifest.h -- Phase AN launcher self-update support.
//
// Holds the authoritative SHA-512 hashes of the three published Windows
// artifacts (FreyaLauncher.exe, FreyaLauncher.cfg, bin/FreyaProxy.exe). The
// cache is populated ONCE at login-server startup by an HTTPS GET of a small
// manifest.json over CloudFront (NET7_PATCHER_MANIFEST_URL). libcurl also reads
// file:// URLs, so the local-stub test points the env at an on-disk manifest --
// no TLS, no cloud.
//
// Bounded TTL: the cache is loaded once at startup, then RefreshIfStale()
// re-fetches it lazily on the /updateCheck path when it is older than
// kRefreshTtlSeconds. This is what lets a CLIENT-ONLY patch take effect
// without a login restart: a deploy only recreates login when its IMAGE
// changes, so a launcher/proxy patch (which never touches the login image)
// would otherwise leave login serving its stale startup cache forever. The
// refresh is rate-limited to at most one outbound fetch per TTL (an attempt
// timestamp gates it, success or failure), and a failed refresh keeps the
// last good cache (Load() only overwrites state on a fully successful fetch).
// Reads on the /updateCheck request path are thread-safe.
//
// Fail-closed: until a successful Load(), Empty() is true and /updateCheck
// reports the server DOWN (HTTP 503). The endpoint NEVER reads a local file
// chosen by the client and never reflects client input into a path -- it only
// ever returns these three fixed, server-controlled entries.

#pragma once

#include <ctime>
#include <string>
#include <mutex>

class PatcherManifest
{
public:
    static PatcherManifest &Instance();

    // Fetch + parse NET7_PATCHER_MANIFEST_URL. Returns true only when all three
    // known files are present with non-empty hashes. A missing env var, a
    // transport error, or a malformed manifest leaves the PRIOR cache intact
    // (state is overwritten only on a fully successful load) and returns false.
    // Called once at startup and again by RefreshIfStale() on the request path.
    bool Load();

    // Re-Load() the manifest if the cache is older than kRefreshTtlSeconds,
    // rate-limited to one fetch attempt per TTL. Cheap no-op when fresh. Call
    // this on the /updateCheck path before reading so a client-only patch is
    // picked up without a login restart.
    void RefreshIfStale();

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

    // Re-fetch the manifest at most this often on the request path.
    static constexpr long kRefreshTtlSeconds = 60;

    mutable std::mutex m_mutex;
    bool m_loaded = false;
    time_t m_lastAttempt = 0;   // last Load() attempt (success or fail); gates refresh
    std::string m_launcherExe;
    std::string m_launcherCfg;
    std::string m_proxyExe;
    std::string m_dlBase;
};
