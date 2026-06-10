// tests/client/tcp_client.cpp

#include "tcp_client.h"

#include <arpa/inet.h>
#include <fcntl.h>
#include <netdb.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cerrno>
#include <cstring>

namespace enbtest {

namespace {

std::string Errno(const std::string& where) {
    return where + ": " + std::strerror(errno);
}

}  // namespace

TcpClient::~TcpClient() { Close(); }

void TcpClient::Close() {
    if (fd_ >= 0) {
        ::close(fd_);
        fd_ = -1;
    }
}

bool TcpClient::SetRecvTimeout(int timeout_ms) {
    timeval tv{};
    tv.tv_sec = timeout_ms / 1000;
    tv.tv_usec = (timeout_ms % 1000) * 1000;
    if (::setsockopt(fd_, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv)) < 0) {
        last_error_ = Errno("setsockopt(SO_RCVTIMEO)");
        return false;
    }
    return true;
}

bool TcpClient::Connect(const std::string& host, uint16_t port, int timeout_ms) {
    Close();
    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;

    char port_buf[16];
    std::snprintf(port_buf, sizeof(port_buf), "%u", static_cast<unsigned>(port));

    addrinfo* res = nullptr;
    int rc = ::getaddrinfo(host.c_str(), port_buf, &hints, &res);
    if (rc != 0) {
        last_error_ = std::string("getaddrinfo: ") + gai_strerror(rc);
        return false;
    }

    int sock = -1;
    for (addrinfo* a = res; a; a = a->ai_next) {
        sock = ::socket(a->ai_family, a->ai_socktype, a->ai_protocol);
        if (sock < 0) continue;

        // Non-blocking connect so we can apply timeout_ms.
        int flags = ::fcntl(sock, F_GETFL, 0);
        ::fcntl(sock, F_SETFL, flags | O_NONBLOCK);

        rc = ::connect(sock, a->ai_addr, a->ai_addrlen);
        if (rc == 0) {
            ::fcntl(sock, F_SETFL, flags);
            break;
        }
        if (errno == EINPROGRESS) {
            fd_set wfds;
            FD_ZERO(&wfds);
            FD_SET(sock, &wfds);
            timeval tv{};
            tv.tv_sec = timeout_ms / 1000;
            tv.tv_usec = (timeout_ms % 1000) * 1000;
            rc = ::select(sock + 1, nullptr, &wfds, nullptr, &tv);
            if (rc > 0) {
                int sock_err = 0;
                socklen_t err_len = sizeof(sock_err);
                ::getsockopt(sock, SOL_SOCKET, SO_ERROR, &sock_err, &err_len);
                if (sock_err == 0) {
                    ::fcntl(sock, F_SETFL, flags);
                    break;
                }
                errno = sock_err;
            } else if (rc == 0) {
                errno = ETIMEDOUT;
            }
        }
        ::close(sock);
        sock = -1;
    }
    ::freeaddrinfo(res);

    if (sock < 0) {
        last_error_ = Errno("connect");
        return false;
    }
    fd_ = sock;
    return SetRecvTimeout(timeout_ms);
}

int TcpClient::Send(const void* buffer, int length) {
    if (fd_ < 0) {
        last_error_ = "Send: socket not open";
        return -1;
    }
    int sent = 0;
    const char* p = static_cast<const char*>(buffer);
    while (sent < length) {
        int n = ::send(fd_, p + sent, length - sent, MSG_NOSIGNAL);
        if (n < 0) {
            if (errno == EINTR) continue;
            last_error_ = Errno("send");
            return -1;
        }
        sent += n;
    }
    return sent;
}

bool TcpClient::RecvExact(void* buffer, int length, int timeout_ms) {
    if (fd_ < 0) {
        last_error_ = "RecvExact: socket not open";
        return false;
    }
    if (!SetRecvTimeout(timeout_ms)) return false;
    int got = 0;
    char* p = static_cast<char*>(buffer);
    while (got < length) {
        int n = ::recv(fd_, p + got, length - got, 0);
        if (n < 0) {
            if (errno == EINTR) continue;
            last_error_ = Errno("recv");
            return false;
        }
        if (n == 0) {
            last_error_ = "RecvExact: peer closed (got " + std::to_string(got) +
                          " of " + std::to_string(length) + " bytes)";
            return false;
        }
        got += n;
    }
    return true;
}

int TcpClient::RecvSome(void* buffer, int length, int timeout_ms) {
    if (fd_ < 0) {
        last_error_ = "RecvSome: socket not open";
        return -1;
    }
    if (!SetRecvTimeout(timeout_ms)) return -1;
    int n = ::recv(fd_, buffer, length, 0);
    if (n < 0) {
        last_error_ = Errno("recv");
        return -1;
    }
    return n;
}

}  // namespace enbtest
