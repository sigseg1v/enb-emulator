// dtls_transport_test.cpp -- Phase AH. Proves the shared DtlsTransport drives a
// full client<->server DTLS handshake over its in-memory BIO pump and then
// carries application datagrams in both directions, decrypting byte-for-byte.
// Also pins the fail-closed opt-out policy semantics.
//
// The cert is generated in-process (self-signed EC P-256 with a SAN), written
// to temp files, loaded by the server transport, and trusted by the client via
// SetVerifyCaFile -- so the handshake runs with peer verification ON, exactly
// as production will (production swaps the in-test CA for the real Let's Encrypt
// chain + the system trust store).
//
// CC BY-NC-SA 3.0 -- see LICENSES/enb-emulator. New code (Phase AH).

#include <gtest/gtest.h>

#include <net7/DtlsTransport.h>

#include <openssl/evp.h>
#include <openssl/x509.h>
#include <openssl/x509v3.h>
#include <openssl/pem.h>

#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

using net7::DtlsPeerKey;
using net7::DtlsRole;
using net7::DtlsStep;
using net7::DtlsTransport;

namespace {

constexpr const char* kHost = "dtls.test.local";

// Generate a self-signed EC P-256 cert with SAN=DNS:kHost; write PEM cert + key
// to the given paths. Returns false on any OpenSSL failure.
bool GenSelfSigned(const std::string& cert_path, const std::string& key_path) {
    bool ok = false;
    EVP_PKEY* pkey = nullptr;
    X509* x509 = nullptr;
    EVP_PKEY_CTX* pctx = EVP_PKEY_CTX_new_id(EVP_PKEY_EC, nullptr);
    if (!pctx)
        return false;
    if (EVP_PKEY_keygen_init(pctx) != 1) {
        EVP_PKEY_CTX_free(pctx);
        return false;
    }
    if (EVP_PKEY_CTX_set_ec_paramgen_curve_nid(pctx, NID_X9_62_prime256v1) != 1) {
        EVP_PKEY_CTX_free(pctx);
        return false;
    }
    if (EVP_PKEY_keygen(pctx, &pkey) != 1) {
        EVP_PKEY_CTX_free(pctx);
        return false;
    }
    EVP_PKEY_CTX_free(pctx);

    x509 = X509_new();
    if (!x509) {
        EVP_PKEY_free(pkey);
        return false;
    }
    X509_set_version(x509, 2); // v3
    ASN1_INTEGER_set(X509_get_serialNumber(x509), 1);
    X509_gmtime_adj(X509_getm_notBefore(x509), 0);
    X509_gmtime_adj(X509_getm_notAfter(x509), 60L * 60L * 24L); // 1 day
    X509_set_pubkey(x509, pkey);

    X509_NAME* name = X509_get_subject_name(x509);
    X509_NAME_add_entry_by_txt(name, "CN", MBSTRING_ASC, (const unsigned char*)kHost, -1, -1, 0);
    X509_set_issuer_name(x509, name); // self-signed

    // SAN: DNS:kHost -- this is what SSL_set1_host matches against.
    {
        std::string san = std::string("DNS:") + kHost;
        X509_EXTENSION* ext =
            X509V3_EXT_conf_nid(nullptr, nullptr, NID_subject_alt_name, san.c_str());
        if (ext) {
            X509_add_ext(x509, ext, -1);
            X509_EXTENSION_free(ext);
        }
    }

    if (X509_sign(x509, pkey, EVP_sha256()) == 0)
        goto done;

    {
        FILE* cf = std::fopen(cert_path.c_str(), "wb");
        if (!cf)
            goto done;
        PEM_write_X509(cf, x509);
        std::fclose(cf);
        FILE* kf = std::fopen(key_path.c_str(), "wb");
        if (!kf)
            goto done;
        PEM_write_PrivateKey(kf, pkey, nullptr, nullptr, 0, nullptr, nullptr);
        std::fclose(kf);
    }
    ok = true;
done:
    X509_free(x509);
    EVP_PKEY_free(pkey);
    return ok;
}

// Drive both transports until both report the handshake established, or we hit
// the round cap. Returns true on mutual establishment.
bool RunHandshake(DtlsTransport& client, DtlsTransport& server,
                  uint64_t srv_peer /*server addr from client's view*/,
                  uint64_t cli_peer /*client addr from server's view*/) {
    DtlsStep s = client.ClientHandshake(srv_peer);
    if (s.fatal)
        return false;

    for (int round = 0; round < 32; ++round) {
        if (!s.to_send.empty()) {
            // Deliver client's bytes -> server.
            DtlsStep r = server.Feed(cli_peer, s.to_send.data(), s.to_send.size());
            if (r.fatal)
                return false;
            if (!r.to_send.empty()) {
                s = client.Feed(srv_peer, r.to_send.data(), r.to_send.size());
                if (s.fatal)
                    return false;
            } else {
                s.to_send.clear();
            }
        }
        if (client.Established(srv_peer) && server.Established(cli_peer))
            return true;
        if (s.to_send.empty() && !client.Established(srv_peer) && !server.Established(cli_peer)) {
            // No progress and not done -> stuck.
            return false;
        }
    }
    return client.Established(srv_peer) && server.Established(cli_peer);
}

struct CertFiles {
    std::string cert, key;
    CertFiles() {
        char tmpl[] = "/tmp/dtls_test_XXXXXX";
        char* d = mkdtemp(tmpl);
        std::string dir = d ? d : "/tmp";
        cert = dir + "/cert.pem";
        key = dir + "/key.pem";
    }
};

// Load the server cert on `server`, trust it on `client`, and run a verified
// handshake to mutual establishment. Mirrors production setup (verify ON).
bool EstablishSession(DtlsTransport& client, DtlsTransport& server, const CertFiles& cf,
                      uint64_t srv_peer, uint64_t cli_peer) {
    if (!server.LoadServerCert(cf.cert.c_str(), cf.key.c_str()))
        return false;
    if (!client.SetVerifyCaFile(cf.cert))
        return false;
    client.SetVerifyHostname(kHost);
    return RunHandshake(client, server, srv_peer, cli_peer);
}

} // namespace

TEST(DtlsTransport, HandshakeAndBidirectionalRoundTrip) {
    CertFiles cf;
    ASSERT_TRUE(GenSelfSigned(cf.cert, cf.key)) << "cert generation failed";

    DtlsTransport server(DtlsRole::Server);
    ASSERT_TRUE(server.LoadServerCert(cf.cert.c_str(), cf.key.c_str())) << server.LastError();
    ASSERT_TRUE(server.Ok());

    DtlsTransport client(DtlsRole::Client);
    ASSERT_TRUE(client.SetVerifyCaFile(cf.cert)) << client.LastError();
    client.SetVerifyHostname(kHost);
    ASSERT_TRUE(client.Ok());

    const uint64_t srv_peer = DtlsPeerKey(0x0A000001u, 3810);
    const uint64_t cli_peer = DtlsPeerKey(0x0A0000FEu, 50000);

    ASSERT_TRUE(RunHandshake(client, server, srv_peer, cli_peer))
        << "handshake failed: client=" << client.LastError() << " server=" << server.LastError();

    // Client -> server application datagram.
    const std::string c2s = "hello-from-proxy\x00\x01\x02 with NULs";
    {
        DtlsStep s =
            client.SendApp(srv_peer, reinterpret_cast<const uint8_t*>(c2s.data()), c2s.size());
        ASSERT_FALSE(s.fatal) << client.LastError();
        ASSERT_FALSE(s.to_send.empty());
        DtlsStep r = server.Feed(cli_peer, s.to_send.data(), s.to_send.size());
        ASSERT_FALSE(r.fatal) << server.LastError();
        ASSERT_EQ(r.app_data.size(), 1u);
        std::string got(r.app_data[0].begin(), r.app_data[0].end());
        EXPECT_EQ(got, c2s);
    }

    // Server -> client application datagram.
    const std::string s2c = "reply-from-server-\xFF\xFE";
    {
        DtlsStep s =
            server.SendApp(cli_peer, reinterpret_cast<const uint8_t*>(s2c.data()), s2c.size());
        ASSERT_FALSE(s.fatal) << server.LastError();
        ASSERT_FALSE(s.to_send.empty());
        DtlsStep r = client.Feed(srv_peer, s.to_send.data(), s.to_send.size());
        ASSERT_FALSE(r.fatal) << client.LastError();
        ASSERT_EQ(r.app_data.size(), 1u);
        std::string got(r.app_data[0].begin(), r.app_data[0].end());
        EXPECT_EQ(got, s2c);
    }

    std::remove(cf.cert.c_str());
    std::remove(cf.key.c_str());
}

// ---------------------------------------------------------------------------
// Parked-sector restart / proxy-session reuse (Phase AI Stage 2).
//
// Repro of the production gate-wedge: a sector is parked (its UDP listener +
// DTLS transport torn down) and later restarted on the SAME deterministic port.
// The client-side proxy keys its DTLS association by (server_ip, port) and, once
// established, does NOT re-handshake (UDPClient::DtlsKickHandshake is a no-op
// while the peer is Established) -- it just resends application datagrams (the
// sector-login among them) under the existing association. So whether the
// returning login is delivered depends ENTIRELY on whether the server kept that
// association across the park.
//
// These two tests pin both sides of the fix: destroying+recreating the server
// transport (the old DropListener/StartListener behaviour) silently drops the
// reused-session login (the wedge), while preserving the transport across the
// park (DetachDtls/AdoptDtls) keeps decrypting it.
// ---------------------------------------------------------------------------

// The BUG: a fresh server transport after the park cannot decrypt the proxy's
// reused-session record, so the sector-login is silently dropped and no
// "Handle Sector Login" ever fires -> the client wedges on the gate transition.
TEST(DtlsTransport, ParkedRestartFreshServerTransportDropsReusedProxySession) {
    CertFiles cf;
    ASSERT_TRUE(GenSelfSigned(cf.cert, cf.key));

    // Same port (3504) before and after the park -- the deterministic sector port.
    const uint64_t srv_peer = DtlsPeerKey(0x0A000001u, 3504);
    const uint64_t cli_peer = DtlsPeerKey(0x0A0000FEu, 50000);

    DtlsTransport client(DtlsRole::Client); // the proxy: persists across the park
    {
        DtlsTransport server1(DtlsRole::Server);
        ASSERT_TRUE(EstablishSession(client, server1, cf, srv_peer, cli_peer))
            << "initial handshake failed: client=" << client.LastError()
            << " server=" << server1.LastError();
        // server1 goes out of scope here == the park's `delete m_Dtls` (all
        // per-peer SSL associations torn down).
    }

    // Restart the parked sector the OLD way: a brand-new server transport bound
    // to the same port. It has no per-peer association for the proxy.
    DtlsTransport server2(DtlsRole::Server);
    ASSERT_TRUE(server2.LoadServerCert(cf.cert.c_str(), cf.key.c_str()));

    // The proxy still considers its session established and will NOT re-handshake.
    ASSERT_TRUE(client.Established(srv_peer))
        << "proxy lost its association -- it would re-handshake and mask the bug";

    const std::string login = "sector-login-under-reused-session";
    DtlsStep s =
        client.SendApp(srv_peer, reinterpret_cast<const uint8_t*>(login.data()), login.size());
    ASSERT_FALSE(s.fatal) << client.LastError();
    ASSERT_FALSE(s.to_send.empty());

    DtlsStep r = server2.Feed(cli_peer, s.to_send.data(), s.to_send.size());
    // The record was encrypted under an epoch/keys the fresh server never
    // negotiated: it CANNOT surface as application data. This is the wedge.
    EXPECT_TRUE(r.app_data.empty())
        << "fresh server unexpectedly decrypted a reused-session record (" << r.app_data.size()
        << " datagrams)";

    std::remove(cf.cert.c_str());
    std::remove(cf.key.c_str());
}

// The FIX: preserving the SAME server transport across the park (DetachDtls on
// DropListener, AdoptDtls on StartListener) keeps the proxy's reused-session
// login decrypting byte-for-byte -> the sector-login lands and the gate completes.
TEST(DtlsTransport, ParkedRestartPreservedServerTransportKeepsReusedProxySession) {
    CertFiles cf;
    ASSERT_TRUE(GenSelfSigned(cf.cert, cf.key));

    const uint64_t srv_peer = DtlsPeerKey(0x0A000001u, 3504);
    const uint64_t cli_peer = DtlsPeerKey(0x0A0000FEu, 50000);

    DtlsTransport client(DtlsRole::Client);
    // The server transport is PRESERVED across the park: DropListener detaches it
    // from the torn-down listener and StartListener re-adopts it on the rebound
    // socket, so this one object outlives the park exactly as in the fix.
    DtlsTransport server(DtlsRole::Server);
    ASSERT_TRUE(EstablishSession(client, server, cf, srv_peer, cli_peer))
        << "initial handshake failed: client=" << client.LastError()
        << " server=" << server.LastError();

    // Park + restart happen here with the association intact on both ends: the
    // socket was closed and rebound (irrelevant to DTLS, which keys by peer addr),
    // but the transport -- and thus the per-peer SSL state -- survived.
    ASSERT_TRUE(client.Established(srv_peer));
    ASSERT_TRUE(server.Established(cli_peer));

    const std::string login = "sector-login-after-park";
    DtlsStep s =
        client.SendApp(srv_peer, reinterpret_cast<const uint8_t*>(login.data()), login.size());
    ASSERT_FALSE(s.fatal) << client.LastError();
    ASSERT_FALSE(s.to_send.empty());

    DtlsStep r = server.Feed(cli_peer, s.to_send.data(), s.to_send.size());
    ASSERT_FALSE(r.fatal) << server.LastError();
    ASSERT_EQ(r.app_data.size(), 1u)
        << "preserved association failed to decrypt the post-park login";
    std::string got(r.app_data[0].begin(), r.app_data[0].end());
    EXPECT_EQ(got, login);

    std::remove(cf.cert.c_str());
    std::remove(cf.key.c_str());
}

// A client that never set a verify hostname must REFUSE to start a handshake
// (err-on-security: no unauthenticated channel).
TEST(DtlsTransport, ClientRefusesHandshakeWithoutVerifyHostname) {
    DtlsTransport client(DtlsRole::Client);
    ASSERT_TRUE(client.Ok());
    DtlsStep s = client.ClientHandshake(DtlsPeerKey(0x7F000001u, 3810));
    EXPECT_TRUE(s.fatal);
    EXPECT_TRUE(s.to_send.empty());
}

// A wrong verify hostname must make the handshake fail (no silent accept).
TEST(DtlsTransport, HandshakeFailsOnHostnameMismatch) {
    CertFiles cf;
    ASSERT_TRUE(GenSelfSigned(cf.cert, cf.key));

    DtlsTransport server(DtlsRole::Server);
    ASSERT_TRUE(server.LoadServerCert(cf.cert.c_str(), cf.key.c_str()));

    DtlsTransport client(DtlsRole::Client);
    ASSERT_TRUE(client.SetVerifyCaFile(cf.cert));
    client.SetVerifyHostname("wrong.host.example");

    const uint64_t srv_peer = DtlsPeerKey(0x0A000001u, 3810);
    const uint64_t cli_peer = DtlsPeerKey(0x0A0000FEu, 50000);
    EXPECT_FALSE(RunHandshake(client, server, srv_peer, cli_peer));

    std::remove(cf.cert.c_str());
    std::remove(cf.key.c_str());
}

// Opt-out policy: required by default, tripped ONLY by the exact sentinel.
TEST(DtlsTransport, PlaintextOptOutRequiresExactSentinel) {
    unsetenv(net7::kDtlsOptOutEnv);
    EXPECT_FALSE(net7::DtlsPlaintextOptedOut()); // default: required

    setenv(net7::kDtlsOptOutEnv, "1", 1);
    EXPECT_FALSE(net7::DtlsPlaintextOptedOut()); // stray "1" ignored

    setenv(net7::kDtlsOptOutEnv, "true", 1);
    EXPECT_FALSE(net7::DtlsPlaintextOptedOut()); // "true" ignored

    setenv(net7::kDtlsOptOutEnv, net7::kDtlsOptOutSentinel, 1);
    EXPECT_TRUE(net7::DtlsPlaintextOptedOut()); // exact value opts out

    unsetenv(net7::kDtlsOptOutEnv);
}
