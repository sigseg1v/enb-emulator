// server/compat/posix_ipc.cpp
//
// See posix_ipc.h. AF_UNIX SOCK_DGRAM replacement for Win32 mailslots.

#ifndef _WIN32

#include "posix_ipc.h"

#include <cerrno>
#include <cstdio>
#include <cstring>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/un.h>
#include <unistd.h>
#include <fcntl.h>

namespace net7ipc {

namespace {

bool MakeParentDir(const char* path) {
    const char* slash = std::strrchr(path, '/');
    if (!slash || slash == path) return true;
    std::string dir(path, slash - path);
    if (mkdir(dir.c_str(), 0777) == 0) return true;
    return errno == EEXIST;
}

bool FillSockaddrUn(const char* path, sockaddr_un& sa) {
    if (!path) return false;
    if (std::strlen(path) >= sizeof(sa.sun_path)) return false;
    std::memset(&sa, 0, sizeof(sa));
    sa.sun_family = AF_UNIX;
    std::strncpy(sa.sun_path, path, sizeof(sa.sun_path) - 1);
    return true;
}

}  // namespace

PosixIpc::PosixIpc(const char* recv_path, const char* send_path)
    : m_recv_path(recv_path ? recv_path : ""),
      m_send_path(send_path ? send_path : "") {
    if (!m_recv_path.empty()) OpenRecv();
}

PosixIpc::~PosixIpc() {
    CloseSend();
    CloseRecv();
}

bool PosixIpc::OpenRecv() {
    sockaddr_un sa;
    if (!FillSockaddrUn(m_recv_path.c_str(), sa)) return false;

    MakeParentDir(m_recv_path.c_str());

    int fd = ::socket(AF_UNIX, SOCK_DGRAM, 0);
    if (fd < 0) {
        std::fprintf(stderr, "posix_ipc: socket() failed: %s\n",
                     std::strerror(errno));
        return false;
    }

    int flags = fcntl(fd, F_GETFL, 0);
    if (flags >= 0) fcntl(fd, F_SETFL, flags | O_NONBLOCK);

    // Stale socket file from a prior process gets unlinked first.
    unlink(m_recv_path.c_str());

    if (::bind(fd, reinterpret_cast<sockaddr*>(&sa), sizeof(sa)) < 0) {
        std::fprintf(stderr, "posix_ipc: bind(%s) failed: %s\n",
                     m_recv_path.c_str(), std::strerror(errno));
        ::close(fd);
        return false;
    }

    // Loosen permissions so the peer (possibly a different uid) can
    // sendto() into this socket. Same-host trust model.
    chmod(m_recv_path.c_str(), 0666);

    m_recv_fd = fd;
    return true;
}

bool PosixIpc::OpenSend() {
    if (m_send_fd >= 0) return true;
    if (m_send_path.empty()) return false;

    int fd = ::socket(AF_UNIX, SOCK_DGRAM, 0);
    if (fd < 0) return false;
    m_send_fd = fd;
    return true;
}

void PosixIpc::CloseRecv() {
    if (m_recv_fd >= 0) {
        ::close(m_recv_fd);
        m_recv_fd = -1;
    }
    if (!m_recv_path.empty()) {
        unlink(m_recv_path.c_str());
    }
}

void PosixIpc::CloseSend() {
    if (m_send_fd >= 0) {
        ::close(m_send_fd);
        m_send_fd = -1;
    }
}

bool PosixIpc::WriteMessage(const char* message) {
    if (!message) return false;
    if (!OpenSend()) return false;

    sockaddr_un sa;
    if (!FillSockaddrUn(m_send_path.c_str(), sa)) return false;

    const std::size_t len = std::strlen(message) + 1;  // include NUL
    if (len > kMaxMessageSize) return false;

    ssize_t sent = ::sendto(m_send_fd, message, len, MSG_NOSIGNAL,
                            reinterpret_cast<sockaddr*>(&sa), sizeof(sa));
    if (sent < 0) {
        // Peer may not be up yet — that's expected during startup. Don't
        // spam; the caller's polling loop retries.
        return false;
    }
    return static_cast<std::size_t>(sent) == len;
}

int PosixIpc::ReadMessage(char* buffer, std::size_t buf_size) {
    if (m_recv_fd < 0 || !buffer || buf_size == 0) return -1;

    ssize_t got = ::recv(m_recv_fd, buffer, buf_size - 1, MSG_DONTWAIT);
    if (got < 0) {
        if (errno == EAGAIN || errno == EWOULDBLOCK) return 0;
        return -1;
    }
    buffer[got] = '\0';
    return static_cast<int>(got);
}

void PosixIpc::Reset() {
    CloseRecv();
    OpenRecv();
}

}  // namespace net7ipc

#endif  // !_WIN32
