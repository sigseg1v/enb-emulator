#pragma once
#include <string>

namespace enb {
// Append a line to enbmod.log (next to the DLL) and to the debugger.
void log_init();
void logf(const char* fmt, ...);
void logs(const std::string& s);
// Directory the DLL was loaded from (where enbmod.log lives). Empty before
// log_init(). Used to site sibling files like the enbmod.cmd console channel.
const char* log_dir();
} // namespace enb
