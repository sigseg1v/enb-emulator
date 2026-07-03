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
bool g_want_eula = false; // ENB_EULA / FREYA_EULA == ACCEPT
std::string g_acc;        // account name
std::string g_pass;       // account password
std::string g_char;       // character to select + enter
bool g_enabled = false;   // any account/character requested -> capture hook installed

// ---- driver latches (act once per phase) ------------------------------------
int g_last_state = -0x7fffffff;
bool g_submitted = false; // credentials sent
bool g_entered = false;   // character enter requested
int g_view_throttle = 0;  // frames since last credential-view search
int g_view_attempts = 0;  // bounded credential-view searches (cost guard)
bool g_view_gaveup = false;
DWORD g_view_found_tick = 0; // GetTickCount when the credential view first appeared (0 = not yet)
int g_char_attempts = 0;     // bounded character-name searches
bool g_char_gaveup = false;

// Bound the cost of the credential-view search so a future client build that moved
// an offset cannot turn tick() into a permanent per-frame memory crawl.
constexpr int kViewSearchPeriod = 15; // run the search at most every N frames
constexpr int kViewSearchMax = 60;    // ~15s of retries, then give up (logged once)
constexpr int kCharSearchMax = 1800;  // ~30s for the avatar list to arrive + populate
constexpr DWORD kViewSettleMs = 1000; // let the located form finish wiring before we submit

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

// Pre-accept the trial EULA by writing the registry value the client's own EULA
// check reads (HKCU\Software\Westwood Studios\Earth & Beyond, "Trial" = DWORD 0).
// This is an ordinary registry write -- no game memory, no game call -- so it is
// safe to do unconditionally when requested. NOTE: this suppresses only the trial
// "demoEULA" dialog. The license / Rules-of-Conduct dialog the retail client shows
// every launch has NO persistent accepted-flag, so it cannot be pre-set away; it is
// dismissed live by eula_dismiss_thread() below.
void accept_eula_registry() {
    HKEY k = nullptr;
    LONG r = RegCreateKeyExA(HKEY_CURRENT_USER, "Software\\Westwood Studios\\Earth & Beyond", 0,
                             nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr);
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

// ---- live license-dialog dismiss (no screen coordinates) --------------------
//
// The retail client shows its license / Rules-of-Conduct agreement as a MODAL
// Win32 dialog (DialogBoxParamA) before the game window appears. Because it is
// modal, the main thread is parked in the dialog's own message loop and the
// game's per-frame pump (which drives tick()) never runs while it is up -- so the
// dialog must be dismissed from a SEPARATE thread, not from tick(). We accept it
// by posting the dialog's own "Accept" command, identifying the dialog purely by
// its license-text control (no pixels, no synthetic mouse): the Accept button is
// control id 1 (IDOK) and the license-text control is id 0x40D, which the normal
// DirectDraw game window does not have. We post WM_COMMAND/IDOK rather than a
// VK_RETURN keypress because the dialog's default-button mapping is not knowable
// from here -- ENTER could land on Decline, IDOK cannot.
constexpr WORD kLicenseAcceptId = 1;      // IDOK -> the dialog's Accept path
constexpr int kLicenseTextCtrlId = 0x40D; // the license-text control unique to this dialog

struct EulaScan {
    DWORD pid;
    HWND found;
};

BOOL CALLBACK eula_enum_proc(HWND hwnd, LPARAM lp) {
    auto* s = reinterpret_cast<EulaScan*>(lp);
    DWORD wpid = 0;
    GetWindowThreadProcessId(hwnd, &wpid);
    if (wpid != s->pid || !IsWindowVisible(hwnd))
        return TRUE;
    // The license dialog is the only top-level window in this process that owns the
    // license-text control; the game window (and everything else) does not.
    if (GetDlgItem(hwnd, kLicenseTextCtrlId) != nullptr) {
        s->found = hwnd;
        return FALSE; // stop enumerating
    }
    return TRUE;
}

// Poll for the modal license dialog and post its Accept command. Runs on its own
// thread for a bounded window after launch; exits once it has accepted a dialog and
// no dialog remains, or when the deadline passes. Posting to a window that has
// already closed is a harmless no-op, so a second (demoEULA) dialog that appears in
// sequence is handled by the same loop.
DWORD WINAPI eula_dismiss_thread(LPVOID) {
    const DWORD pid = GetCurrentProcessId();
    const DWORD deadline = GetTickCount() + 120000; // give the dialog up to ~2 min to appear
    HWND last_accepted = nullptr;
    int gone_streak = 0;
    bool accepted_any = false;

    while (GetTickCount() < deadline) {
        EulaScan s{pid, nullptr};
        EnumWindows(eula_enum_proc, reinterpret_cast<LPARAM>(&s));
        if (s.found) {
            PostMessageA(s.found, WM_COMMAND, MAKEWPARAM(kLicenseAcceptId, 0), 0);
            if (s.found != last_accepted) {
                logf("autologin: license dialog accepted (hwnd=%p)", (void*)s.found);
                last_accepted = s.found;
                accepted_any = true;
            }
            gone_streak = 0;
        } else if (accepted_any) {
            // Past the dialog(s): once we have accepted at least one and none has
            // been present for ~1.5s, we are done.
            if (++gone_streak >= 8)
                break;
        }
        Sleep(200);
    }
    return 0;
}

void start_eula_dismiss_thread() {
    HANDLE h = CreateThread(nullptr, 0, eula_dismiss_thread, nullptr, 0, nullptr);
    if (h)
        CloseHandle(h);
    else
        logf("autologin: failed to start license-dialog dismiss thread");
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
    constexpr unsigned kScanSpan = 0x400; // bytes of each object scanned for child ptrs

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
    uint32_t args[2] = {(uint32_t)(uintptr_t)text.c_str(), 0u}; // append=0 -> replace
    actions::call_thiscall(game::addr::EditSetText, widget, args, 2);
}

// Find the character slot whose name matches ENB_CHARACTER (case-insensitive).
// Names are inline buffers at LoginTask + char_name_base + i*char_name_stride.
// Returns -1 when no slot matches (list not yet populated, or no such character).
int find_char_index(uintptr_t lt, const std::string& name) {
    for (int i = 0; i < game::login::char_max; ++i) {
        uintptr_t slot =
            lt + game::login::char_name_base + (unsigned)i * game::login::char_name_stride;
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
    if (!view) {
        g_view_found_tick = 0; // view not present -- restart the settle timer
        return;                // widgets not built yet -- retry on the next throttled tick
    }

    // Settle before submitting. The credential form can be located a beat before the
    // front-end has finished wiring it; firing CredentialAccept in that window drives
    // a half-initialized form and the login wedges (the avatar list still arrives but
    // the client never advances to character select). Wait for the view to remain
    // present for ~1s, THEN fill + submit.
    DWORD now = GetTickCount();
    if (g_view_found_tick == 0) {
        g_view_found_tick = now;
        return;
    }
    if (now - g_view_found_tick < kViewSettleMs)
        return;

    set_widget_text(mem::ptr(view + game::login::view_user_widget), g_acc);
    // Never fire the login with an EMPTY password: the client accepts the submit and
    // the avatar list arrives, but the front-end wedges before character select
    // (observed as a hang with autologin logging pass=(unset)). Fill the username and
    // stop -- the user types the password and submits by hand. The launcher already
    // refuses to arm hands-free login without a password; this is the DLL-side guard
    // for a raw ENB_ACC_NAME-without-PASS env.
    if (g_pass.empty()) {
        logf("autologin: no password supplied -- filled username, leaving submit to the user");
        g_view_gaveup = true;
        return;
    }
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
        return; // list may still be populating -- retry next tick
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

    if (g_want_eula) {
        accept_eula_registry();
        // Also dismiss the live (modal) license / Rules-of-Conduct dialog, which has
        // no persistent accepted-flag to pre-set. Runs on its own thread because the
        // modal dialog parks the game thread (and thus tick()).
        start_eula_dismiss_thread();
    }

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
        return; // front-end run loop has not fired yet (no pre-game screens up)

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
