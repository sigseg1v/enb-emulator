// Ticks.h — monotonic millisecond tick counter for Phase M vocabulary sweep.
//
// Replaces every Linux-active call site of the Win32 `GetTickCount()` API.
// The historical pattern in this codebase is
//
//     uint32_t t0 = GetTickCount();
//     ... do work ...
//     uint32_t elapsed = GetTickCount() - t0;
//
// where the 32-bit wrap and the resolution-in-milliseconds were assumed.
// `Net7TickMs()` preserves both: it returns the low 32 bits of a monotonic
// millisecond counter sourced from `CLOCK_MONOTONIC`. Subtraction across
// wrap-around still works modulo 2^32.

#ifndef NET7_TICKS_H
#define NET7_TICKS_H

#include <cstdint>
#include <ctime>

inline std::uint32_t Net7TickMs()
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<std::uint32_t>(
        (static_cast<std::uint64_t>(ts.tv_sec) * 1000ull
         + static_cast<std::uint64_t>(ts.tv_nsec) / 1000000ull) & 0xFFFFFFFFu);
}

#endif // NET7_TICKS_H
