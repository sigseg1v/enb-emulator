#pragma once
#include <string>

namespace enb {
// Append a line to enbmod.log (next to the DLL) and to the debugger.
void log_init();
void logf(const char* fmt, ...);
void logs(const std::string& s);
} // namespace enb
