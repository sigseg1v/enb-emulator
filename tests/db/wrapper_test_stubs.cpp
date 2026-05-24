// tests/db/wrapper_test_stubs.cpp
//
// Tiny stubs that satisfy the symbols mysqlplus.cpp transitively pulls in
// from Net7.h / Net7.cpp without dragging the whole server into the test
// binary. Keep this file minimal.

#include <cstdarg>
#include <cstdio>

void LogMessage(const char * /*fmt*/, ...) {
    // Test harness: swallow.
}

void LogMySQLMsg(char *fmt, ...) {
    if (!fmt) return;
    va_list ap;
    va_start(ap, fmt);
    std::vprintf(fmt, ap);
    va_end(ap);
}
