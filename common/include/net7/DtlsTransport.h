// DtlsTransport.h -- shared per-peer DTLS state machine for the proxy<->server
// UDP leg (Phase AH). One DtlsTransport instance wraps ONE UDP socket; it holds
// a separate DTLS association per remote peer keyed by (ip,port), driven over
// in-memory BIOs so the caller keeps doing its own recvfrom()/sendto() exactly
// as before. This is transport plumbing only -- it does NOT touch wire format;
// the bytes inside the channel are the same cleartext EnbUdp frames as today.
//
// Why memory BIOs (not BIO_new_dgram): every server listener and the proxy's
// global plane multiplex MANY peers over ONE unconnected socket (the global
// plane even fans in two server ports, :3810 + :3806, on one fd). A datagram
// BIO connects to a single peer; memory BIOs let us route each datagram to the
// right per-peer SSL by source (ip,port) and pump bytes manually.
//
// Security posture (Phase AH, err-on-security):
//   * DTLS 1.2 floor (no DTLS 1.0).
//   * Client side MANDATORILY verifies the server cert against a hostname
//     (SSL_set1_host + SSL_VERIFY_PEER against the system trust store). A client
//     transport with no verify hostname set refuses to start a handshake.
//   * Server side loads the real cert chain + private key.
// Cookie-exchange (HelloVerifyRequest) anti-amplification is a tracked follow-up
// (AH-2a); a per-transport cap on half-open associations is the interim guard.
//
// CC BY-NC-SA 3.0 -- see LICENSES/enb-emulator. New code (Phase AH).

#ifndef NET7_DTLS_TRANSPORT_H_INCLUDED
#define NET7_DTLS_TRANSPORT_H_INCLUDED

#include <cstdint>
#include <cstddef>
#include <map>
#include <mutex>
#include <vector>
#include <string>

struct ssl_ctx_st;
struct ssl_st;
struct bio_st;

namespace net7 {

enum class DtlsRole { Server, Client };

// ---------------------------------------------------------------------------
// Security policy for the Phase AH gate. DTLS is REQUIRED by default. Plaintext
// UDP is permitted ONLY when the operator sets the exact opt-out sentinel below
// -- a deliberate, unmistakable value so a stray "1"/"true"/"yes" elsewhere in
// the environment can never silently disable encryption. Unset, empty, or any
// other value all mean "DTLS required".
//
// Startup contract (enforced by server + proxy main):
//   * opted out      -> run cleartext, but print a LOUD startup warning.
//   * NOT opted out  -> build the DtlsTransport; if it fails to configure
//                       (missing/bad cert, CTX error, ...) the process FAILS TO
//                       START (fail-closed) rather than falling back to plaintext.
extern const char* const kDtlsOptOutEnv;       // "NET7_DTLS_ALLOW_PLAINTEXT"
extern const char* const kDtlsOptOutSentinel;  // the exact required opt-out value

// True iff the operator has EXPLICITLY opted out of DTLS (env == sentinel,
// byte-for-byte). Anything else -> false -> DTLS is required.
bool DtlsPlaintextOptedOut();

// Output of a single Feed/Send/Handshake step. The caller must sendto() every
// byte in `to_send` to the peer, and dispatch every datagram in `app_data` as a
// decrypted application message (one entry per decrypted record).
struct DtlsStep {
    std::vector<uint8_t>              to_send;    // raw bytes to put on the wire
    std::vector<std::vector<uint8_t>> app_data;   // decrypted app datagrams
    bool handshake_done = false;                  // association is established
    bool fatal          = false;                  // association died; forget it
};

// Pack a peer (host-order IPv4 + host-order UDP port) into a stable 48-bit key.
inline uint64_t DtlsPeerKey(uint32_t ip_host_order, uint16_t port_host_order)
{
    return (static_cast<uint64_t>(ip_host_order) << 16) | port_host_order;
}

class DtlsTransport {
public:
    explicit DtlsTransport(DtlsRole role);
    ~DtlsTransport();

    DtlsTransport(const DtlsTransport&)            = delete;
    DtlsTransport& operator=(const DtlsTransport&) = delete;

    // Server only: load the PEM cert chain + private key (e.g. the Let's Encrypt
    // fullchain.pem + privkey.pem). Returns false on any load/consistency error.
    bool LoadServerCert(const char* cert_chain_path, const char* key_path);

    // Client only: the hostname the server cert MUST match (the cert SAN, e.g.
    // the public game hostname). Required -- a client with no verify hostname
    // will not initiate a handshake. Uses the system trust store.
    void SetVerifyHostname(const std::string& hostname);

    // Client only (optional): trust ONLY this CA/cert file instead of the whole
    // system trust store -- tighter, and the hook the handshake test uses. Call
    // before the first ClientHandshake. Returns false on a load error.
    bool SetVerifyCaFile(const std::string& ca_path);

    // True once the SSL_CTX is built and (server) a usable cert/key is loaded.
    bool Ok() const { return m_ctx != nullptr && m_ctx_ok; }
    const std::string& LastError() const { return m_last_error; }

    // Client: kick off a handshake to `peer` (produces the ClientHello in
    // to_send). Safe to call once per new peer before the first SendApp.
    DtlsStep ClientHandshake(uint64_t peer);

    // Feed one inbound raw UDP datagram received from `peer`. Drives the
    // handshake and/or decrypts application records.
    DtlsStep Feed(uint64_t peer, const uint8_t* data, size_t len);

    // Encrypt one application datagram destined for `peer` (handshake must be
    // complete; if not, the bytes are queued and flushed when it completes).
    DtlsStep SendApp(uint64_t peer, const uint8_t* data, size_t len);

    // Is the association to `peer` established (handshake complete)?
    bool Established(uint64_t peer) const;

    // Tear down and forget the association to `peer`.
    void ForgetPeer(uint64_t peer);

    // Number of live associations (for diagnostics / the half-open cap).
    size_t PeerCount() const { std::lock_guard<std::mutex> lock(m_mu); return m_peers.size(); }

    // Cap on simultaneous associations; a Feed that would exceed it on a NEW
    // peer is dropped (interim anti-amplification guard until AH-2a cookies).
    void SetMaxPeers(size_t n) { m_max_peers = n; }

private:
    struct Peer {
        ssl_st*                  ssl  = nullptr;
        struct bio_st*           rbio = nullptr;  // we BIO_write inbound here
        struct bio_st*           wbio = nullptr;  // we BIO_read outbound here
        bool                     done = false;
        std::vector<uint8_t>     pending_app;     // app bytes queued pre-handshake
    };

    Peer*   GetOrCreate(uint64_t peer, bool allow_new);
    void    DrainWbio(Peer* p, DtlsStep& out);
    void    PumpHandshake(Peer* p, DtlsStep& out);
    void    DrainAppRecords(Peer* p, DtlsStep& out);
    void    Fail(DtlsStep& out, const char* what);

    DtlsRole                     m_role;
    ssl_ctx_st*                  m_ctx     = nullptr;
    bool                         m_ctx_ok  = false;
    std::string                  m_verify_host;
    std::string                  m_last_error;
    size_t                       m_max_peers = 4096;
    std::map<uint64_t, Peer>     m_peers;
    // Guards m_peers and every per-peer SSL op. The server's recv thread (Feed/
    // SSL_read) and its sender threads (SendApp/SSL_write) hit the same SSL
    // concurrently, which OpenSSL does not allow on one SSL object. One
    // transport-level lock is correct and the DTLS op rate on this leg is low.
    mutable std::mutex           m_mu;
};

} // namespace net7

#endif // NET7_DTLS_TRANSPORT_H_INCLUDED
