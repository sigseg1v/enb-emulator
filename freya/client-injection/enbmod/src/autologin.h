// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
#pragma once
// autologin.h -- env-driven, in-client auto-login (no screen coordinates).
//
// Drives the client's OWN front-end code (EULA / login / character select) from a
// few environment variables, so a scripted multibox launch gets N different
// accounts into the game with zero pixel automation:
//
//   FREYA_EULA / ENB_EULA = ACCEPT  -- pre-accept the EULA.
//   FREYA_ACC_NAME / ENB_ACC_NAME   -- account name.
//   FREYA_ACC_PASS / ENB_ACC_PASS   -- account password (paired with the name).
//   FREYA_CHARACTER / ENB_CHARACTER -- character to select + enter once listed.
//
// (The ENB_* spellings are the user-facing names; the FREYA_* spellings are the
// project-branded aliases -- either works, FREYA_* wins if both are set.)
//
// Mechanism: we capture the LoginTask `this` read-only from the per-frame front-end
// run loop (game::addr::LoginRunLoop) -- the same capture pattern the in-space hooks
// use -- and, on the game thread, fill the login fields + invoke the client's own
// credential-submit / character-select functions when the matching env var is set.
// EULA accept is a registry write the client's own EULA check reads. Nothing here
// alters the wire protocol; it only calls existing client code paths.
//
// This is entirely OPT-IN: with none of the env vars set, init() installs no hook
// and tick() is a no-op, so an ordinary launch is byte-for-byte unaffected.

namespace enb {
namespace autologin {

// Read the env, optionally pre-accept the EULA (registry), and -- only when an
// account or character auto-login was requested -- install the read-only LoginTask
// capture hook. Call once from the worker thread AFTER hooks::mh_init() (it may
// create a MinHook hook). No-op when no auto-login env var is set.
void init();

// Per-frame driver, called on the game thread from the message-pump tick. Reads
// the captured LoginTask's state and performs the next pending step (submit
// credentials, then select+enter the character). Cheap no-op once login is
// complete or when auto-login was not requested.
void tick();

} // namespace autologin
} // namespace enb
