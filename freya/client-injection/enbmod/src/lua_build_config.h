// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// lua_build_config.h -- force-included (via the Lua build's -include) into every
// Lua translation unit before any other header.
//
// Why this exists: Lua's error handling (ldo.c LUAI_THROW / LUAI_TRY) defaults,
// on a non-C++ Windows build, to libc setjmp / longjmp. On this target --
// 32-bit mingw-w64 GCC running under Wine -- that unwind machinery is unreliable:
// the FIRST Lua error (e.g. a /run syntax error) unwound through libc longjmp
// faulted the client with an unhandled STATUS_SINGLE_STEP (0x80000004), not a
// clean jump back to the protected call. This is the same environment quirk that
// forced mem.h to abandon MSVC SEH / clang __try and use a Vectored Exception
// Handler plus __builtin_longjmp (see mem.h: "verified working under Wine").
//
// So we point Lua at the one jump mechanism proven to work in this environment:
// the GCC builtins. __builtin_setjmp/__builtin_longjmp save/restore only the
// frame pointer, stack pointer and return address (no SEH unwind, no signal
// mask), which is exactly Lua's "jump up the stack to the protected call"
// pattern and sidesteps the broken libc unwinder. __builtin_longjmp requires a
// value of 1; Lua always passes 1, and only tests __builtin_setjmp() == 0.
//
// The buffer must be at least 5 words for __builtin_setjmp; void*[5] satisfies
// that and decays to the void** the builtins expect. Only ldo.c consumes these
// macros (struct lua_longjmp { ... luai_jmpbuf b; }); applying the -include to
// every Lua TU is harmless.
#ifndef ENBMOD_LUA_BUILD_CONFIG_H
#define ENBMOD_LUA_BUILD_CONFIG_H

typedef void* enbmod_lua_jmpbuf[5];
#define luai_jmpbuf      enbmod_lua_jmpbuf
#define LUAI_THROW(L,c)  __builtin_longjmp((c)->b, 1)
#define LUAI_TRY(L,c,a)  if (__builtin_setjmp((c)->b) == 0) { a }

#endif // ENBMOD_LUA_BUILD_CONFIG_H
