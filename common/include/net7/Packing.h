// common/include/net7/Packing.h
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
** Phase R Wave 2 (2026-05-23): extracted from per-process Net7.h / Net7SSL.h
** so common/include/net7/PacketStructures.h can be shared across server,
** proxy, and login-server without pulling in the rest of those headers.
*/

#ifndef _NET7_PACKING_H_INCLUDED_
#define _NET7_PACKING_H_INCLUDED_

#include <stdint.h>
#include <stddef.h>
#include <wchar.h>

// MSVC uses #pragma pack(1) globally + an empty macro on each struct;
// gcc/clang use __attribute__((packed)) at struct close.
#ifdef WIN32
#pragma pack(1)
#define ATTRIB_PACKED
#else
#define ATTRIB_PACKED __attribute__((packed))
#endif

// Sized integer typedefs used by the wire-format structs.
// Win32: was MSVC __intN; Linux: stdint.
#ifdef WIN32
typedef unsigned __int64 u64;
typedef signed __int64   s64;
typedef unsigned __int32 u32;
typedef signed __int32   s32;
typedef unsigned short   u16;
typedef signed short     s16;
typedef unsigned char    u8;
typedef signed char      s8;
#else
typedef uint64_t u64;
typedef int64_t  s64;
typedef uint32_t u32;
typedef int32_t  s32;
typedef uint16_t u16;
typedef int16_t  s16;
typedef uint8_t  u8;
typedef int8_t   s8;
#endif

// BSTR: COM string type on Windows (wchar_t*, 16-bit elements). Linux
// reproduces only the typedef — wchar_t is 32-bit there. The structs in
// PacketStructures.h that reference BSTR are only consumed on the server
// path; proxy/login compile them but never read/write them.
#ifndef BSTR
typedef wchar_t* BSTR;
#endif

#endif // _NET7_PACKING_H_INCLUDED_
