// server/compat/windows.h
//
// Empty shim that satisfies legacy `#include <windows.h>` from vendored
// third-party headers (notably server/src/openssl/*.h) when building on
// Linux. The Win32 types/macros those headers actually reference are
// already provided by server/src/Net7.h's LINUX compat block, which is
// transitively included via Net7.h.
//
// Do NOT use this for new code — include the POSIX headers directly.

#pragma once

#ifndef WIN32
#  include "Net7.h"
#endif
