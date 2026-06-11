// PatcherManifest.cpp -- Phase AN. See PatcherManifest.h.

#include "PatcherManifest.h"

#include <cstdlib>
#include <cstring>
#include <ctime>
#include <functional>
#include <mutex>

#include <curl/curl.h>

// LogMessage lives in Net7SSL.cpp (global, varargs). Declared here rather than
// pulling the whole header.
extern void LogMessage(const char *format, ...);

// The three published files, keyed by the relativePath the manifest publishes
// and the /updateCheck response echoes. The proxy lives under bin/ in the
// launcher's install layout but is flat in the bucket; the launcher maps the
// relativePath to its own tree on download.
static const char *kLauncherExeRel = "FreyaLauncher.exe";
static const char *kLauncherCfgRel = "FreyaLauncher.cfg";
static const char *kProxyExeRel    = "bin/FreyaProxy.exe";
// Optional MVAS position-feed injection pair; ride with the launcher set.
static const char *kPosFeedDllRel  = "bin/FreyaPosFeed.dll";
static const char *kInjectExeRel   = "bin/FreyaInject.exe";
// Optional Lua mod runtime (enbmod). Shipped independently like the MVAS pair.
static const char *kEnbmodDllRel   = "bin/enbmod.dll";

PatcherManifest &PatcherManifest::Instance()
{
    static PatcherManifest s_instance;
    return s_instance;
}

// ------------------------------------------------------------------ helpers

namespace {

size_t WriteToString(char *ptr, size_t size, size_t nmemb, void *userdata)
{
    std::string *out = static_cast<std::string *>(userdata);
    size_t n = size * nmemb;
    out->append(ptr, n);
    return n;
}

bool Fetch(const std::string &url, std::string &out)
{
    static std::once_flag s_curlInit;
    std::call_once(s_curlInit, []() { curl_global_init(CURL_GLOBAL_DEFAULT); });

    CURL *curl = curl_easy_init();
    if (!curl) return false;

    out.clear();
    curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteToString);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &out);
    curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
    curl_easy_setopt(curl, CURLOPT_TIMEOUT, 30L);
    curl_easy_setopt(curl, CURLOPT_CONNECTTIMEOUT, 10L);
    curl_easy_setopt(curl, CURLOPT_FAILONERROR, 1L);     // HTTP >=400 -> error
    curl_easy_setopt(curl, CURLOPT_USERAGENT, "FreyaLogin-PatcherManifest/1.0");
    // Cap the body we will accept (a manifest is tiny; refuse a runaway).
    curl_easy_setopt(curl, CURLOPT_MAXFILESIZE, 1L * 1024L * 1024L);

    CURLcode rc = curl_easy_perform(curl);
    curl_easy_cleanup(curl);

    if (rc != CURLE_OK)
    {
        LogMessage("PatcherManifest: fetch failed (%s): %s\n",
                   url.c_str(), curl_easy_strerror(rc));
        return false;
    }
    return !out.empty();
}

// Minimal extractor for a JSON string field within `obj`: finds "field"
// followed by a colon and a quoted value. The manifest is self-generated with a
// fixed, flat schema (no escaped quotes inside hashes or paths), so a full JSON
// parser is unwarranted -- but the bounds are checked so a malformed manifest
// yields "" rather than a crash.
std::string JsonString(const std::string &obj, const char *field)
{
    std::string needle = std::string("\"") + field + "\"";
    size_t p = obj.find(needle);
    if (p == std::string::npos) return "";
    p = obj.find(':', p + needle.size());
    if (p == std::string::npos) return "";
    ++p;
    while (p < obj.size() && (obj[p] == ' ' || obj[p] == '\t' ||
                              obj[p] == '\n' || obj[p] == '\r'))
        ++p;
    if (p >= obj.size() || obj[p] != '"') return "";
    ++p;
    size_t end = obj.find('"', p);
    if (end == std::string::npos) return "";
    return obj.substr(p, end - p);
}

// Walk the "files":[ ... ] array, yielding each top-level { ... } object as a
// substring. Brace-depth aware so nested objects (none expected, but safe)
// don't split early.
void ForEachFileObject(const std::string &json,
                       const std::function<void(const std::string &)> &fn)
{
    size_t arr = json.find("\"files\"");
    if (arr == std::string::npos) return;
    size_t lb = json.find('[', arr);
    if (lb == std::string::npos) return;

    size_t i = lb + 1;
    while (i < json.size())
    {
        if (json[i] == ']') break;
        if (json[i] == '{')
        {
            int depth = 1;
            size_t start = i;
            ++i;
            while (i < json.size() && depth > 0)
            {
                if (json[i] == '{') ++depth;
                else if (json[i] == '}') --depth;
                ++i;
            }
            if (depth != 0) break;               // malformed; stop
            fn(json.substr(start, i - start));    // includes the braces
            continue;
        }
        ++i;
    }
}

std::string TrimTrailingSlashes(std::string s)
{
    while (!s.empty() && (s.back() == '/' || s.back() == ' '))
        s.pop_back();
    return s;
}

} // namespace

// ------------------------------------------------------------------ Load

bool PatcherManifest::Load()
{
    // Record the attempt up front so RefreshIfStale() backs off for a full TTL
    // whether this load succeeds OR fails -- a CloudFront outage must not turn
    // every /updateCheck into an outbound fetch.
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        m_lastAttempt = time(nullptr);
    }

    const char *url = getenv("NET7_PATCHER_MANIFEST_URL");
    if (!url || !*url)
    {
        LogMessage("PatcherManifest: NET7_PATCHER_MANIFEST_URL not set; "
                   "/updateCheck disabled (server reports DOWN to the launcher)\n");
        return false;
    }

    std::string body;
    if (!Fetch(url, body))
        return false;   // Fetch already logged

    std::string launcherExe, launcherCfg, proxyExe, posFeedDll, injectExe, enbmodDll;
    ForEachFileObject(body, [&](const std::string &obj) {
        std::string rel  = JsonString(obj, "relativePath");
        std::string hash = JsonString(obj, "sha512");
        if (rel.empty() || hash.empty()) return;
        if (rel == kLauncherExeRel)      launcherExe = hash;
        else if (rel == kLauncherCfgRel) launcherCfg = hash;
        else if (rel == kProxyExeRel)    proxyExe = hash;
        else if (rel == kPosFeedDllRel)  posFeedDll = hash;
        else if (rel == kInjectExeRel)   injectExe = hash;
        else if (rel == kEnbmodDllRel)   enbmodDll = hash;
    });

    if (launcherExe.empty() || launcherCfg.empty() || proxyExe.empty())
    {
        LogMessage("PatcherManifest: manifest at %s is missing one of "
                   "FreyaLauncher.exe / FreyaLauncher.cfg / bin/FreyaProxy.exe; "
                   "refusing to serve a partial manifest\n", url);
        return false;
    }
    // The MVAS injection pair is optional: an older manifest that predates it
    // still loads (it just won't ship those two files). They ship the moment a
    // manifest carrying them is published -- no login redeploy required.

    std::string dlBase;
    if (const char *b = getenv("NET7_PATCHER_DL_BASE"))
        dlBase = TrimTrailingSlashes(b);

    {
        std::lock_guard<std::mutex> lock(m_mutex);
        m_launcherExe = launcherExe;
        m_launcherCfg = launcherCfg;
        m_proxyExe    = proxyExe;
        m_posFeedDll  = posFeedDll;
        m_injectExe   = injectExe;
        m_enbmodDll   = enbmodDll;
        m_dlBase      = dlBase;
        m_loaded      = true;
    }

    int total = 3 + (!posFeedDll.empty() ? 1 : 0) + (!injectExe.empty() ? 1 : 0)
                  + (!enbmodDll.empty() ? 1 : 0);
    LogMessage("PatcherManifest: loaded %d file hashes from %s (dl base '%s')\n",
               total, url, dlBase.c_str());
    return true;
}

// ---------------------------------------------------------------- refresh

void PatcherManifest::RefreshIfStale()
{
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        // Attempted within the TTL -> serve whatever we have. This gates BOTH
        // the warm-refresh case and the never-loaded retry case (a startup
        // fetch failure must not turn every /updateCheck into a fetch).
        if ((time(nullptr) - m_lastAttempt) < kRefreshTtlSeconds)
            return;
    }
    // Stale (or never loaded). Load() re-stamps m_lastAttempt and only
    // overwrites the cache on a fully successful fetch, so a failed refresh is
    // transparent: the last good manifest keeps being served.
    Load();
}

// ------------------------------------------------------------------ reads

bool PatcherManifest::Empty() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return !m_loaded;
}

std::string PatcherManifest::LauncherExeHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_launcherExe;
}

std::string PatcherManifest::LauncherCfgHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_launcherCfg;
}

std::string PatcherManifest::ProxyExeHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_proxyExe;
}

std::string PatcherManifest::PosFeedDllHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_posFeedDll;
}

std::string PatcherManifest::InjectExeHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_injectExe;
}

std::string PatcherManifest::EnbmodDllHash() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_enbmodDll;
}

std::string PatcherManifest::DlBase() const
{
    std::lock_guard<std::mutex> lock(m_mutex);
    return m_dlBase;
}
