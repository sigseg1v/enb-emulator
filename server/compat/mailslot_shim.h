// server/compat/mailslot_shim.h
//
// Windows mailslot IPC stub. The Net-7 server uses mailslots
// (CreateMailslot / WriteFile / ReadFile on slot handles) as a one-way
// IPC channel between cooperating processes (e.g. login -> sector). On
// Linux there is no direct equivalent; the planned replacement is Unix
// domain sockets, but that's a Phase B-continuation deliverable.
//
// For now: CreateMailslot returns a sentinel HANDLE. WriteFile/ReadFile
// over a slot handle log and pretend to succeed (zero bytes read).
// Anything actually relying on mailslot delivery will silently drop data
// at runtime — that's acceptable during Phase B since the focus is the
// build, not runtime correctness.

#ifndef NET7_MAILSLOT_SHIM_H
#define NET7_MAILSLOT_SHIM_H

#if !defined(_WIN32)

#include "win32_shim.h"

#include <cstddef>

// CreateMailslot(name, max_msg_size, read_timeout, security)
HANDLE CreateMailslotA(LPCSTR name,
                       DWORD  max_msg_size,
                       DWORD  read_timeout,
                       void*  security);

// MSVC's name-decorating define normally exposes CreateMailslot -> -W or -A.
// We're ANSI-only; alias the bare name to the -A variant.
#ifndef CreateMailslot
#  define CreateMailslot CreateMailslotA
#endif

// File I/O over a mailslot handle. The legacy code uses the regular
// Win32 file APIs, so we provide compatible signatures here. These are
// scoped to mailslot handles specifically — they are NOT a general-
// purpose ReadFile/WriteFile replacement.
BOOL WriteFile(HANDLE handle,
               const void* buffer,
               DWORD bytes_to_write,
               DWORD* bytes_written,
               void* overlapped);

BOOL ReadFile(HANDLE handle,
              void* buffer,
              DWORD bytes_to_read,
              DWORD* bytes_read,
              void* overlapped);

// Some sites use GetMailslotInfo to peek at queued message counts.
BOOL GetMailslotInfo(HANDLE handle,
                     DWORD* max_msg_size,
                     DWORD* next_size,
                     DWORD* msg_count,
                     DWORD* read_timeout);

#endif  // !_WIN32
#endif  // NET7_MAILSLOT_SHIM_H
