// ClientToServer_linux_stubs.cpp
//
// Phase J Linux: stubs for the Global and Sector server opcode
// dispatch tables. The real Win32 implementations live in
// ClientToGlobalServer.cpp (286 LOC, ~15 handlers) and
// ClientToSectorServer.cpp (757 LOC, ~50+ handlers) — porting them in
// full is a Phase K task. For now we log each opcode and return so the
// frame is consumed correctly and we can SEE what real clients send
// (handy as input for the next iteration's handler-by-handler port).
//
// This file is Linux-only (the WIN32 build picks up the real dispatch
// from ClientToGlobalServer.cpp / ClientToSectorServer.cpp).

#ifndef WIN32

#include "Net7.h"
#include "Connection.h"
#include "Opcodes.h"

void Connection::ProcessGlobalServerOpcode(short opcode, short bytes)
{
    LogMessage("Linux stub: ProcessGlobalServerOpcode 0x%04x (%d bytes) — not yet implemented\n",
               (unsigned short) opcode, (int) bytes);
}

void Connection::ProcessSectorServerOpcode(short opcode, short bytes)
{
    LogMessage("Linux stub: ProcessSectorServerOpcode 0x%04x (%d bytes) — not yet implemented\n",
               (unsigned short) opcode, (int) bytes);
}

#endif // !WIN32
