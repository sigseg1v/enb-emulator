// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// autologin.cpp -- env-driven, in-client auto-login (no screen coordinates).
// See autologin.h for the env contract and the why.
//
// Mechanism. The injected DLL drives the client's OWN front-end code paths -- the
// same functions the EULA / Login button / character-enter actions call -- off the
// per-frame LoginTask captured read-only by hooks::enable_login_hook(). No pixels,
// no synthetic input. A per-process environment (ENB_ACC_NAME/PASS/CHARACTER) thus
// gets a different account into the world per client, which is what makes scripted
// multibox trivial.
//
// Safety model. A wrong *read* of a game offset is harmless: every read here goes
// through enb::mem, which is VEH-guarded and returns a default instead of faulting.
// A wrong *write* or *call* is NOT recoverable -- it crashes the Win32 client. So
// the driver never guesses: the credential view is located by a strong runtime
// SIGNATURE (its back-pointer equals the LoginTask AND both its edit-widget slots
// point at objects whose first dword is the edit-widget vtable), and a widget is
// re-checked for that vtable immediately before its text setter is called. If the
// layout does not match a future client build, the driver logs and stays its hand
// -- it degrades to "did not log in", never to a crash. Every client function it
// calls is non-blocking (the client's own run loop pumps the result), so it never
// wedges the tick thread.

#include "autologin.h"

#include <windows.h>

#include <queue>
#include <string>
#include <unordered_set>
#include <utility>

#include "actions.h"
#include "game.h"
#include "hooks.h"
#include "log.h"
#include "mem.h"

namespace enb {
namespace autologin {
namespace {

// ---- env-driven config, read once in init() ---------------------------------
bool g_want_eula = false;     // ENB_EULA / FREYA_EULA == ACCEPT
std::string g_acc;            // account name
std::string g_pass;           // account password
std::string g_char;           // character to select + enter
bool g_enabled = false;       // any account/character requested -> capture hook installed

// ---- driver latches (act once per phase) ------------------------------------
int g_last_state = -0x7fffffff;
bool g_submitted = false;      // credentials sent
bool g_entered = false;        // character enter requested
int g_view_throttle = 0;       // frames since last credential-view search
int g_view_attempts = 0;       // bounded credential-view searches (cost guard)
bool g_view_gaveup = false;
int g_char_attempts = 0;       // bounded character-name searches
bool g_char_gaveup = false;

// Bound the cost of the credential-view search so a future client build that moved
// an offset cannot turn tick() into a permanent per-frame memory crawl.
constexpr int kViewSearchPeriod = 15;  // run the search at most every N frames
constexpr int kViewSearchMax = 60;     // ~15s of retries, then give up (logged once)
constexpr int kCharSearchMax = 1800;   // ~30s for the avatar list to arrive + populate

// FREYA_* alias wins over ENB_* when both are set.
std::string env_either(const char* freya_name, const char* enb_name) {
    char buf[1024];
    DWORD n = GetEnvironmentVariableA(freya_name, buf, sizeof buf);
    if (n > 0 && n < sizeof buf)
        return std::string(buf, n);
    n = GetEnvironmentVariableA(enb_name, buf, sizeof buf);
    if (n > 0 && n < sizeof buf)
        return std::string(buf, n);
    return std::string();
}

bool ieq(const std::string& a, const char* b) {
    return _stricmp(a.c_str(), b) == 0;
}

// Pre-accept the EULA by writing the registry value the client's own EULA check
// reads (HKCU\Software\Westwood Studios\Earth & Beyond, "Trial" = DWORD 0). This
// is an ordinary registry write -- no game memory, no game call -- so it is safe
// to do unconditionally when requested.
void accept_eula_registry() {
    HKEY k = nullptr;
    LONG r = RegCreateKeyExA(HKEY_CURRENT_USER,
                             "Software\\Westwood Studios\\Earth & Beyond", 0, nullptr, 0,
                             KEY_SET_VALUE, nullptr, &k, nullptr);
    if (r != ERROR_SUCCESS) {
        logf("autologin: EULA registry open failed (%ld)", r);
        return;
    }
    DWORD zero = 0;
    r = RegSetValueExA(k, "Trial", 0, REG_DWORD, (const BYTE*)&zero, sizeof zero);
    RegCloseKey(k);
    if (r == ERROR_SUCCESS)
        logf("autologin: EULA pre-accepted (Trial=0)");
    else
        logf("autologin: EULA registry write failed (%ld)", r);
}

// A candidate pointer must be 4-aligned and land in the client's mapped range
// (image base .. user-space top). Cheap pre-filter for the BFS so we do not chase
// small ints / tag words as if they were object pointers.
inline bool plausible_ptr(uintptr_t p) {
    return p >= 0x00400000 && p < 0x7ff00000 && (p & 3) == 0;
}

// Does V look like the credential FORM view for this LoginTask? Strong signature:
// its owner back-pointer is the LoginTask, and BOTH its edit-widget slots point at
// objects whose first dword is the edit-widget vtable. Matching all three on a
// stale/unrelated object is implausible, so a hit is the real view.
bool is_credential_view(uintptr_t v, uintptr_t lt) {
    if (mem::ptr(v + game::login::view_owner) != lt)
        return false;
    uintptr_t uw = mem::ptr(v + game::login::view_user_widget);
    uintptr_t pw = mem::ptr(v + game::login::view_pass_widget);
    return uw && pw && mem::u32(uw) == game::login::edit_widget_vtable &&
           mem::u32(pw) == game::login::edit_widget_vtable;
}

// Locate the credential view by walking the object graph from the LoginTask. The
// view is a child of the active front-end container, so a bounded BFS reaches it;
// the search is capped (depth + node count) and the caller throttles how often it
// runs. Returns 0 when the view is not (yet) present.
uintptr_t find_credential_view(uintptr_t lt) {
    constexpr int kMaxDepth = 5;
    constexpr size_t kMaxNodes = 4000;
    constexpr unsigned kScanSpan = 0x400;  // bytes of each object scanned for child ptrs

    std::unordered_set<uintptr_t> seen;
    std::queue<std::pair<uintptr_t, int>> q;
    seen.insert(lt);
    q.push({lt, 0});
    size_t visited = 0;

    while (!q.empty() && visited < kMaxNodes) {
        auto [node, depth] = q.front();
        q.pop();
        ++visited;

        if (node != lt && is_credential_view(node, lt))
            return node;

        if (depth >= kMaxDepth)
            continue;
        for (unsigned off = 0; off <= kScanSpan; off += 4) {
            uintptr_t child = mem::ptr(node + off);
            if (!plausible_ptr(child))
                continue;
            if (seen.insert(child).second)
                q.push({child, depth + 1});
        }
    }
    return 0;
}

// Replace an edit widget's text via the client's own setter. Re-checks the
// edit-widget vtable immediately before calling so a mislocated widget degrades to
// a no-op rather than a bad call. The text pointer is ordinary DLL memory, which is
// valid client-process memory (we are injected), so no marshalling buffer is needed.
void set_widget_text(uintptr_t widget, const std::string& text) {
    if (!widget || mem::u32(widget) != game::login::edit_widget_vtable)
        return;
    uint32_t args[2] = {(uint32_t)(uintptr_t)text.c_str(), 0u};  // append=0 -> replace
    actions::call_thiscall(game::addr::EditSetText, widget, args, 2);
}

// Find the character slot whose name matches ENB_CHARACTER (case-insensitive).
// Names are inline buffers at LoginTask + char_name_base + i*char_name_stride.
// Returns -1 when no slot matches (list not yet populated, or no such character).
int find_char_index(uintptr_t lt, const std::string& name) {
    for (int i = 0; i < game::login::char_max; ++i) {
        uintptr_t slot = lt + game::login::char_name_base +
                         (unsigned)i * game::login::char_name_stride;
        std::string s = mem::cstr(slot, 64);
        if (!s.empty() && _stricmp(s.c_str(), name.c_str()) == 0)
            return i;
    }
    return -1;
}

// ---- phase drivers ----------------------------------------------------------

// state_login: fill the credential widgets and fire the (non-blocking) accept.
void drive_submit(uintptr_t lt) {
    if (g_view_gaveup)
        return;
    if (++g_view_throttle < kViewSearchPeriod)
        return;
    g_view_throttle = 0;
    if (g_view_attempts >= kViewSearchMax) {
        logf("autologin: credential view not found after %d tries -- auto-login inactive",
             g_view_attempts);
        g_view_gaveup = true;
        return;
    }
    ++g_view_attempts;

    uintptr_t view = find_credential_view(lt);
    if (!view)
        return;  // widgets not built yet -- retry on the next throttled tick

    set_widget_text(mem::ptr(view + game::login::view_user_widget), g_acc);
    if (!g_pass.empty())
        set_widget_text(mem::ptr(view + game::login::view_pass_widget), g_pass);
    actions::call_thiscall(game::addr::CredentialAccept, view, nullptr, 0);
    g_submitted = true;
    logf("autologin: credentials submitted (view=%08x)", (unsigned)view);
}

// Select ENB_CHARACTER and request world entry (non-blocking). Gated on the
// character's NAME having appeared in the slots -- which only happens after auth
// has succeeded and the server's avatar list has arrived -- NOT on the +0x24 state
// (this build keeps +0x24 at the login value through char entry; see game.h).
void drive_enter(uintptr_t lt) {
    if (g_char_gaveup)
        return;
    int idx = find_char_index(lt, g_char);
    if (idx < 0) {
        if (++g_char_attempts >= kCharSearchMax) {
            logf("autologin: character \"%s\" not found in list -- auto-login inactive",
                 g_char.c_str());
            g_char_gaveup = true;
        }
        return;  // list may still be populating -- retry next tick
    }
    mem::write<uint32_t>(lt + game::login::sel_index, (uint32_t)idx);
    actions::call_thiscall(game::addr::CharEnter, lt, nullptr, 0);
    g_entered = true;
    logf("autologin: entering world as %s (idx=%d)", g_char.c_str(), idx);
}

} // namespace

void init() {
    g_acc = env_either("FREYA_ACC_NAME", "ENB_ACC_NAME");
    g_pass = env_either("FREYA_ACC_PASS", "ENB_ACC_PASS");
    g_char = env_either("FREYA_CHARACTER", "ENB_CHARACTER");
    g_want_eula = ieq(env_either("FREYA_EULA", "ENB_EULA"), "ACCEPT");

    if (g_want_eula)
        accept_eula_registry();

    // Only arm the front-end capture when there is an account or character to
    // drive. With neither set, an ordinary launch installs no hook and tick() is
    // a no-op -- byte-for-byte unaffected. (Do NOT log the password.)
    g_enabled = !g_acc.empty() || !g_char.empty();
    if (!g_enabled) {
        if (g_want_eula)
            logf("autologin: EULA-only (no account/character) -- capture hook not installed");
        return;
    }
    logf("autologin: enabled acc=%s char=%s pass=%s", g_acc.empty() ? "(unset)" : g_acc.c_str(),
         g_char.empty() ? "(unset)" : g_char.c_str(), g_pass.empty() ? "(unset)" : "(set)");
    if (!hooks::enable_login_hook())
        logf("autologin: LoginTask capture hook failed -- auto-login inactive");
}

void tick() {
    // Once we have requested world entry the LoginTask is torn down and the
    // captured pointer is stale -- stop here so we never read freed memory (which
    // would log a stream of garbage "state" values as the heap is reused).
    if (!g_enabled || g_entered)
        return;
    uintptr_t lt = hooks::login_task();
    if (!lt)
        return;  // front-end run loop has not fired yet (no pre-game screens up)

    int st = mem::i32(lt + game::login::state);
    if (st != g_last_state) {
        logf("autologin: state %d -> %d", g_last_state, st);
        g_last_state = st;
    }

    // Phase 1: submit credentials at the login screen. Until that lands, do not
    // attempt character entry (the avatar list cannot arrive before auth).
    if (!g_acc.empty() && !g_submitted) {
        if (st == game::login::state_login)
            drive_submit(lt);
        return;
    }

    // Phase 2: enter the world as ENB_CHARACTER once its name appears in the slots.
    // The name only populates after the submit above authenticates, so this is the
    // reliable "char list is ready" signal -- the front-end state never advances.
    if (!g_char.empty())
        drive_enter(lt);
}

} // namespace autologin
} // namespace enb
