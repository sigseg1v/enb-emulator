// server/compat/posix_ipc.h
//
// Phase J continuation: AF_UNIX SOCK_DGRAM replacement for the Win32
// mailslot IPC used between the Net7 server and the Net7SSL login
// server. Same-host, low-volume (one ping every 10s in the current
// codebase), message-oriented — datagram semantics match mailslots
// exactly, no framing layer needed.
//
// Each process binds a "recv" socket at a well-known path and opens a
// "send" socket on demand to the peer's path. Buffers are bounded to
// the mailslot wire ceiling (1024 bytes) so an oversized message gets
// truncated rather than overwriting unrelated memory.
//
// Header is included on both POSIX and Win32 (the Win32 path compiles
// to nothing — see #ifndef _WIN32 guard).

#ifndef NET7_POSIX_IPC_H
#define NET7_POSIX_IPC_H

#ifndef _WIN32

#include <cstddef>
#include <string>

namespace net7ipc {

constexpr std::size_t kMaxMessageSize = 1024;

class PosixIpc {
public:
    // recv_path: AF_UNIX socket path this process reads from
    // send_path: AF_UNIX socket path of the peer (writes go there)
    PosixIpc(const char* recv_path, const char* send_path);
    ~PosixIpc();

    PosixIpc(const PosixIpc&) = delete;
    PosixIpc& operator=(const PosixIpc&) = delete;

    // Send a NUL-terminated message to the peer. Lazily opens the send
    // socket on first call. Returns true if the datagram was queued.
    bool WriteMessage(const char* message);

    // Try to read one pending datagram. Non-blocking. Returns 0 if no
    // message is ready, >0 = bytes received, -1 = error. The buffer is
    // null-terminated on success (truncated if oversized).
    int ReadMessage(char* buffer, std::size_t buf_size);

    // Reopen the recv socket (mirrors Win32 ResetMailSystem behaviour).
    void Reset();

    bool valid() const { return m_recv_fd >= 0; }
    const std::string& recv_path() const { return m_recv_path; }
    const std::string& send_path() const { return m_send_path; }

private:
    bool OpenRecv();
    bool OpenSend();
    void CloseRecv();
    void CloseSend();

    int m_recv_fd = -1;
    int m_send_fd = -1;
    std::string m_recv_path;
    std::string m_send_path;
};

}  // namespace net7ipc

#endif  // !_WIN32
#endif  // NET7_POSIX_IPC_H
