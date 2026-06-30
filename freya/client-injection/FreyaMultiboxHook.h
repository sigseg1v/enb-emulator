// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//==============================================================================
// FreyaMultiboxHook.h
//
// Single-instance bypass for running more than one client.exe at once
// (multi-box). The retail client guards startup with a named mutex; this hook
// neutralises that guard in-process so a second (third, ...) client can run.
// See FreyaMultiboxHook.cpp for the mechanism. Opt-in: does nothing unless the
// FREYA_MULTIBOX environment variable is set (the launcher sets it only when it
// is deliberately launching a multi-box session).
//==============================================================================
#pragma once

// Install the bypass. Idempotent and safe to call once from DllMain
// (DLL_PROCESS_ATTACH). No-op unless FREYA_MULTIBOX is set in the environment.
void FreyaMultiboxHook_Install();
