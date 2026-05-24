// SingleInstance.h
//
// Phase M: replacement for the Win32 CreateMutex(NULL, TRUE, "InstanceName")
// pattern that the server, sector, and login binaries used to guard against
// running two copies on the same host. POSIX equivalent is flock() on a
// per-process pid file.
//
// Usage:
//     net7::SingleInstance guard;
//     if (!guard.Acquire("Net7Server")) {
//         fprintf(stderr, "Another Net-7 server instance is already running\n");
//         return 1;
//     }
//     // ... main loop ...
//     // guard's destructor releases the lock + unlinks the pid file.
//
// The lock file lives at /run/enb-emulator/<name>.pid if /run is writable,
// otherwise /tmp/enb-emulator-<name>.pid. The lock is held for the lifetime
// of the SingleInstance object; closing the fd (which happens on destruct or
// at process exit) releases it.

#ifndef NET7_SINGLE_INSTANCE_H_
#define NET7_SINGLE_INSTANCE_H_

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fcntl.h>
#include <sys/file.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#include <string>

namespace net7 {

class SingleInstance
{
public:
    SingleInstance() : m_fd(-1) {}
    ~SingleInstance() { Release(); }

    SingleInstance(const SingleInstance &) = delete;
    SingleInstance & operator=(const SingleInstance &) = delete;

    // Returns true if we now hold the lock; false if another process does
    // (or we failed to even open the lock file — caller should treat both
    // as "do not start").
    bool Acquire(const char *name)
    {
        m_path = ResolvePath(name);
        m_fd = ::open(m_path.c_str(), O_CREAT | O_RDWR, 0644);
        if (m_fd < 0) {
            std::fprintf(stderr, "SingleInstance: open(%s) failed: %s\n",
                         m_path.c_str(), std::strerror(errno));
            return false;
        }
        if (::flock(m_fd, LOCK_EX | LOCK_NB) != 0) {
            ::close(m_fd);
            m_fd = -1;
            return false;
        }
        // Stamp our pid for `ps`-style diagnostics. Truncate first.
        if (::ftruncate(m_fd, 0) == 0) {
            char buf[32];
            int n = std::snprintf(buf, sizeof(buf), "%d\n", (int)::getpid());
            if (n > 0) (void)::write(m_fd, buf, (size_t)n);
        }
        return true;
    }

    void Release()
    {
        if (m_fd >= 0) {
            ::flock(m_fd, LOCK_UN);
            ::close(m_fd);
            m_fd = -1;
            if (!m_path.empty()) ::unlink(m_path.c_str());
        }
    }

private:
    static std::string ResolvePath(const char *name)
    {
        std::string sanitized;
        sanitized.reserve(std::strlen(name));
        for (const char *p = name; *p; ++p) {
            char c = *p;
            sanitized += (c == ' ' || c == '/' || c == '\\') ? '_' : c;
        }
        // Prefer /run/enb-emulator if writable.
        struct stat st{};
        if (::mkdir("/run/enb-emulator", 0755) == 0 || errno == EEXIST) {
            if (::access("/run/enb-emulator", W_OK) == 0) {
                return std::string("/run/enb-emulator/") + sanitized + ".pid";
            }
        }
        (void)st;
        return std::string("/tmp/enb-emulator-") + sanitized + ".pid";
    }

    int         m_fd;
    std::string m_path;
};

} // namespace net7

#endif // NET7_SINGLE_INSTANCE_H_
