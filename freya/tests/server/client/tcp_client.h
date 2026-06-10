// tests/client/tcp_client.h
//
// Minimal blocking TCP client used by the integration test harness to
// connect to the Net7Proxy port on the server container. No async, no
// threading — tests stay readable. POSIX-only (Linux).

#pragma once

#include <cstdint>
#include <string>

namespace enbtest {

class TcpClient {
public:
    TcpClient() = default;
    ~TcpClient();

    TcpClient(const TcpClient&) = delete;
    TcpClient& operator=(const TcpClient&) = delete;

    // Returns true on success. Sets last_error() on failure.
    bool Connect(const std::string& host, uint16_t port, int timeout_ms = 5000);

    // Returns number of bytes sent, or -1 on error.
    int Send(const void* buffer, int length);

    // Reads exactly `length` bytes (blocking). Returns true if all
    // bytes were read before the timeout.
    bool RecvExact(void* buffer, int length, int timeout_ms = 5000);

    // Reads up to `length` bytes (single recv). Returns number read,
    // 0 on EOF, -1 on error.
    int RecvSome(void* buffer, int length, int timeout_ms = 5000);

    void Close();
    bool IsOpen() const { return fd_ >= 0; }
    const std::string& last_error() const { return last_error_; }

private:
    bool SetRecvTimeout(int timeout_ms);

    int fd_ = -1;
    std::string last_error_;
};

}  // namespace enbtest
