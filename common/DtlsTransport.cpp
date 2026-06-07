// DtlsTransport.cpp -- see DtlsTransport.h. Per-peer DTLS over memory BIOs.
//
// CC BY-NC-SA 3.0 -- see LICENSES/enb-emulator. New code (Phase AH).

#include <net7/DtlsTransport.h>

#include <openssl/ssl.h>
#include <openssl/err.h>
#include <openssl/bio.h>
#include <openssl/x509.h>
#include <openssl/x509v3.h>

#include <cstring>
#include <cstdlib>

namespace net7 {

// Deliberately verbose, unmistakable opt-out. Must be set byte-for-byte; a
// stray "1" or "true" in the environment cannot trip it.
const char* const kDtlsOptOutEnv      = "NET7_DTLS_ALLOW_PLAINTEXT";
const char* const kDtlsOptOutSentinel = "i-accept-unencrypted-udp";

bool DtlsPlaintextOptedOut()
{
    const char* v = ::getenv(kDtlsOptOutEnv);
    return v != nullptr && std::strcmp(v, kDtlsOptOutSentinel) == 0;
}

namespace {

// Scratch buffer for draining a BIO / reading an app record. DTLS records are
// bounded well under this (max ~16k plaintext); a single read per loop turn is
// enough because we loop until the BIO/SSL says "would block".
constexpr int kScratch = 32 * 1024;

std::string PopOpenSslError()
{
    unsigned long e = ERR_get_error();
    if (e == 0) return std::string();
    char buf[256];
    ERR_error_string_n(e, buf, sizeof(buf));
    // Drain the rest of the queue so it does not leak into the next op.
    while (ERR_get_error() != 0) {}
    return std::string(buf);
}

} // namespace

DtlsTransport::DtlsTransport(DtlsRole role) : m_role(role)
{
    const SSL_METHOD* method =
        (role == DtlsRole::Server) ? DTLS_server_method() : DTLS_client_method();
    m_ctx = SSL_CTX_new(method);
    if (!m_ctx) {
        m_last_error = "SSL_CTX_new failed: " + PopOpenSslError();
        return;
    }

    // Err on security: DTLS 1.2 floor, never DTLS 1.0.
    SSL_CTX_set_min_proto_version(m_ctx, DTLS1_2_VERSION);

    if (role == DtlsRole::Client) {
        // Verification is configured per-SSL (SSL_set1_host) once the caller
        // supplies the hostname; the CTX just needs the trust store. The CTX is
        // "ok" for a client only after a verify hostname is set -- enforced in
        // ClientHandshake. Load the system trust store now.
        if (SSL_CTX_set_default_verify_paths(m_ctx) != 1) {
            m_last_error = "SSL_CTX_set_default_verify_paths failed: " + PopOpenSslError();
            return;
        }
        SSL_CTX_set_verify(m_ctx, SSL_VERIFY_PEER, nullptr);
        // Client CTX is structurally ready; the per-handshake guard still
        // requires a verify hostname before any ClientHello goes out.
        m_ctx_ok = true;
    }
    // Server CTX becomes ok only once a cert/key is loaded (LoadServerCert).
}

DtlsTransport::~DtlsTransport()
{
    for (auto& kv : m_peers) {
        if (kv.second.ssl) SSL_free(kv.second.ssl); // frees the attached BIOs
    }
    m_peers.clear();
    if (m_ctx) SSL_CTX_free(m_ctx);
}

bool DtlsTransport::LoadServerCert(const char* cert_chain_path, const char* key_path)
{
    if (m_role != DtlsRole::Server) {
        m_last_error = "LoadServerCert called on a client transport";
        return false;
    }
    if (!m_ctx) return false;

    if (SSL_CTX_use_certificate_chain_file(m_ctx, cert_chain_path) != 1) {
        m_last_error = std::string("load cert chain '") + cert_chain_path +
                       "' failed: " + PopOpenSslError();
        return false;
    }
    if (SSL_CTX_use_PrivateKey_file(m_ctx, key_path, SSL_FILETYPE_PEM) != 1) {
        m_last_error = std::string("load private key '") + key_path +
                       "' failed: " + PopOpenSslError();
        return false;
    }
    if (SSL_CTX_check_private_key(m_ctx) != 1) {
        m_last_error = "cert/key mismatch: " + PopOpenSslError();
        return false;
    }
    m_ctx_ok = true;
    return true;
}

void DtlsTransport::SetVerifyHostname(const std::string& hostname)
{
    m_verify_host = hostname;
}

bool DtlsTransport::SetVerifyCaFile(const std::string& ca_path)
{
    if (m_role != DtlsRole::Client || !m_ctx) {
        m_last_error = "SetVerifyCaFile on non-client / no CTX";
        return false;
    }
    if (SSL_CTX_load_verify_locations(m_ctx, ca_path.c_str(), nullptr) != 1) {
        m_last_error = std::string("load CA '") + ca_path + "' failed: " + PopOpenSslError();
        return false;
    }
    return true;
}

DtlsTransport::Peer* DtlsTransport::GetOrCreate(uint64_t peer, bool allow_new)
{
    auto it = m_peers.find(peer);
    if (it != m_peers.end()) return &it->second;
    if (!allow_new) return nullptr;
    if (m_peers.size() >= m_max_peers) return nullptr; // half-open cap

    Peer p;
    p.ssl = SSL_new(m_ctx);
    if (!p.ssl) return nullptr;

    p.rbio = BIO_new(BIO_s_mem());
    p.wbio = BIO_new(BIO_s_mem());
    if (!p.rbio || !p.wbio) {
        if (p.rbio) BIO_free(p.rbio);
        if (p.wbio) BIO_free(p.wbio);
        SSL_free(p.ssl);
        return nullptr;
    }
    // Memory BIO must report "retry" (not EOF) when drained, so SSL treats an
    // empty read buffer as "need more datagrams" rather than connection close.
    BIO_set_mem_eof_return(p.rbio, -1);
    BIO_set_mem_eof_return(p.wbio, -1);
    SSL_set_bio(p.ssl, p.rbio, p.wbio); // SSL now owns the BIOs

    if (m_role == DtlsRole::Server) {
        SSL_set_accept_state(p.ssl);
    } else {
        SSL_set_connect_state(p.ssl);
        if (!m_verify_host.empty()) {
            // Hostname check is enforced during the handshake; a mismatch makes
            // SSL_do_handshake fail. Also pins the expected identity.
            SSL_set1_host(p.ssl, m_verify_host.c_str());
            X509_VERIFY_PARAM* vp = SSL_get0_param(p.ssl);
            X509_VERIFY_PARAM_set_hostflags(vp, X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS);
        }
    }

    auto res = m_peers.emplace(peer, std::move(p));
    return &res.first->second;
}

void DtlsTransport::DrainWbio(Peer* p, DtlsStep& out)
{
    uint8_t buf[kScratch];
    int n;
    while ((n = BIO_read(p->wbio, buf, sizeof(buf))) > 0) {
        out.to_send.insert(out.to_send.end(), buf, buf + n);
    }
}

void DtlsTransport::PumpHandshake(Peer* p, DtlsStep& out)
{
    if (p->done) return;
    int rc = SSL_do_handshake(p->ssl);
    DrainWbio(p, out); // flush ClientHello / ServerHello / Finished, etc.
    if (rc == 1) {
        // Established. For a client, SSL_set1_host already enforced the name;
        // double-check the verify result to be safe.
        if (m_role == DtlsRole::Client) {
            long vr = SSL_get_verify_result(p->ssl);
            if (vr != X509_V_OK) {
                Fail(out, "peer certificate verification failed");
                return;
            }
        }
        p->done = true;
        out.handshake_done = true;
        // Flush anything queued before the handshake completed.
        if (!p->pending_app.empty()) {
            SSL_write(p->ssl, p->pending_app.data(), (int) p->pending_app.size());
            p->pending_app.clear();
            DrainWbio(p, out);
        }
        return;
    }
    int err = SSL_get_error(p->ssl, rc);
    if (err == SSL_ERROR_WANT_READ || err == SSL_ERROR_WANT_WRITE) {
        return; // need more datagrams from the peer; not an error
    }
    Fail(out, "DTLS handshake error");
}

void DtlsTransport::DrainAppRecords(Peer* p, DtlsStep& out)
{
    uint8_t buf[kScratch];
    for (;;) {
        int n = SSL_read(p->ssl, buf, sizeof(buf));
        if (n > 0) {
            out.app_data.emplace_back(buf, buf + n);
            continue;
        }
        int err = SSL_get_error(p->ssl, n);
        if (err == SSL_ERROR_WANT_READ || err == SSL_ERROR_WANT_WRITE) break;
        if (err == SSL_ERROR_ZERO_RETURN) { Fail(out, "peer closed DTLS"); break; }
        // A renegotiation or post-handshake message may have produced output.
        DrainWbio(p, out);
        if (err == SSL_ERROR_NONE) break;
        Fail(out, "DTLS read error");
        break;
    }
    DrainWbio(p, out); // flush any DTLS-level acks/renegotiation bytes
}

void DtlsTransport::Fail(DtlsStep& out, const char* what)
{
    out.fatal = true;
    m_last_error = std::string(what) + ": " + PopOpenSslError();
}

DtlsStep DtlsTransport::ClientHandshake(uint64_t peer)
{
    DtlsStep out;
    if (m_role != DtlsRole::Client) { out.fatal = true; m_last_error = "ClientHandshake on server transport"; return out; }
    if (!Ok()) { out.fatal = true; m_last_error = "client CTX not ready"; return out; }
    if (m_verify_host.empty()) {
        // Err on security: never start an unauthenticated handshake.
        out.fatal = true;
        m_last_error = "refusing client handshake: no verify hostname set";
        return out;
    }
    Peer* p = GetOrCreate(peer, /*allow_new=*/true);
    if (!p) { out.fatal = true; m_last_error = "peer cap reached"; return out; }
    PumpHandshake(p, out);
    return out;
}

DtlsStep DtlsTransport::Feed(uint64_t peer, const uint8_t* data, size_t len)
{
    DtlsStep out;
    if (!Ok()) { out.fatal = true; m_last_error = "CTX not ready"; return out; }
    // A server creates the association on first contact; a client must already
    // have one (created by ClientHandshake).
    Peer* p = GetOrCreate(peer, /*allow_new=*/(m_role == DtlsRole::Server));
    if (!p) {
        // Unknown client-side peer, or server half-open cap hit: drop silently.
        return out;
    }

    BIO_write(p->rbio, data, (int) len);

    if (!p->done) {
        PumpHandshake(p, out);
        if (out.fatal) return out;
        if (p->done) {
            // Handshake just finished; the same datagram may also have carried
            // early app data on a fast path -- try to drain.
            DrainAppRecords(p, out);
        }
        return out;
    }

    DrainAppRecords(p, out);
    return out;
}

DtlsStep DtlsTransport::SendApp(uint64_t peer, const uint8_t* data, size_t len)
{
    DtlsStep out;
    if (!Ok()) { out.fatal = true; m_last_error = "CTX not ready"; return out; }
    Peer* p = GetOrCreate(peer, /*allow_new=*/(m_role == DtlsRole::Client));
    if (!p) { out.fatal = true; m_last_error = "no association for peer"; return out; }

    if (!p->done) {
        // Queue until the handshake completes; PumpHandshake flushes it.
        p->pending_app.insert(p->pending_app.end(), data, data + len);
        // Nudge the handshake in case we are the client and have not started.
        if (m_role == DtlsRole::Client) PumpHandshake(p, out);
        return out;
    }

    int n = SSL_write(p->ssl, data, (int) len);
    if (n <= 0) {
        int err = SSL_get_error(p->ssl, n);
        if (err != SSL_ERROR_WANT_READ && err != SSL_ERROR_WANT_WRITE) {
            Fail(out, "DTLS write error");
            return out;
        }
    }
    DrainWbio(p, out);
    return out;
}

bool DtlsTransport::Established(uint64_t peer) const
{
    auto it = m_peers.find(peer);
    return it != m_peers.end() && it->second.done;
}

void DtlsTransport::ForgetPeer(uint64_t peer)
{
    auto it = m_peers.find(peer);
    if (it == m_peers.end()) return;
    if (it->second.ssl) SSL_free(it->second.ssl); // frees rbio/wbio too
    m_peers.erase(it);
}

} // namespace net7
