// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator
//
// Phase AH (AH-8/AH-9): the accept/drop decision for the per-packet C->S auth
// wrapper, extracted as a pure, self-contained function so the runtime
// (server/src/UDPConnection.cpp) and the unit test
// (tests/server/protocol/auth_wrapper_test.cpp) exercise the SAME logic -- a
// forged-token test that passed against a copy would prove nothing.
//
// Deliberately self-contained: it works on raw byte offsets (pinned by
// tests/server/protocol/header_layout_test.cpp) rather than the packed
// EnbUdpHeader / EnbUdpAuthWrapper structs, so it needs no project headers and
// no libsodium -- it builds cleanly in the host gtest tree where libsodium-dev
// is absent. The constants below MUST match
// common/include/net7/PacketStructures.h (NET7_UDP_AUTH_WRAPPER_VERSION,
// NET7_UDP_AUTH_TOKEN_LEN, EnbUdpHeader layout).

#ifndef NET7_AUTH_WRAPPER_H_INCLUDED
#define NET7_AUTH_WRAPPER_H_INCLUDED

#include <cstdint>
#include <cstddef>

namespace net7 {

inline constexpr uint8_t kAuthWrapperVersion   = 0x01;   // NET7_UDP_AUTH_WRAPPER_VERSION
inline constexpr size_t  kAuthTokenLen         = 16;     // NET7_UDP_AUTH_TOKEN_LEN
inline constexpr size_t  kAuthWrapperLen       = 1 + kAuthTokenLen;  // 17, sizeof(EnbUdpAuthWrapper)
inline constexpr size_t  kUdpHeaderLen         = 12;     // sizeof(EnbUdpHeader)
inline constexpr size_t  kUdpHeaderPlayerIdOff = 4;      // offsetof(EnbUdpHeader, player_id)

enum class AuthWrapperVerdict {
    Accept,                 // strip wrapper, dispatch the inner datagram
    DropShortOrOversize,    // record can't hold a 17-byte wrapper + 12-byte header, or is too big
    DropBadVersion,         // wrapper version byte != kAuthWrapperVersion
    DropTokenMismatch,      // gameplay datagram with no/forged token for its GameID
};

// Human-readable reason for a verdict, for the rate-limited drop log. NEVER
// includes the token. Kept in lock-step with the runtime's log strings.
inline const char *AuthWrapperReason(AuthWrapperVerdict v)
{
    switch (v) {
        case AuthWrapperVerdict::Accept:              return "accept";
        case AuthWrapperVerdict::DropShortOrOversize: return "short/oversize frame";
        case AuthWrapperVerdict::DropBadVersion:      return "bad wrapper version";
        case AuthWrapperVerdict::DropTokenMismatch:   return "token mismatch";
    }
    return "unknown";
}

// Fixed-purpose constant-time equality over the 16-byte token. No early return
// on first-difference: the loop time does not depend on WHERE the bytes differ,
// only on the (public, fixed) length -- so a forged token leaks no timing
// signal about how many leading bytes were correct. `volatile` stops the
// compiler from optimizing the accumulation into a short-circuit.
inline bool ConstantTimeTokenEqual(const unsigned char *a, const unsigned char *b)
{
    volatile unsigned char diff = 0;
    for (size_t i = 0; i < kAuthTokenLen; i++)
        diff = (unsigned char)(diff | (unsigned char)(a[i] ^ b[i]));
    return diff == 0;
}

// The pure decision for one DTLS C->S app record `rec` of length `rec_len`.
//
//   `bound_token_for(player_id, out16)` -> bool: fills out16 with the 16-byte
//   token bound to that player at character-select and returns true, or returns
//   false if there is no such player / no token bound. Consulted ONLY for
//   gameplay datagrams (inner header player_id != 0). Pre-auth datagrams
//   (player_id == 0) precede ticket-learning and are gated by ticket validation
//   elsewhere, so they pass the wrapper check without a token compare.
//
// On Accept, *inner / *inner_len point at the unwrapped [EnbUdpHeader][payload]
// inside `rec` -- byte-for-byte what the plaintext handlers already expect. On
// any drop verdict the out params are left untouched; the caller logs
// AuthWrapperReason(verdict) (rate-limited) and discards the datagram.
template <typename TokenLookup>
inline AuthWrapperVerdict CheckAuthWrapper(
    const unsigned char *rec, size_t rec_len, size_t max_buffer,
    TokenLookup &&bound_token_for,
    const unsigned char **inner, int *inner_len)
{
    if (rec_len < kAuthWrapperLen + kUdpHeaderLen || rec_len >= max_buffer)
        return AuthWrapperVerdict::DropShortOrOversize;

    if (rec[0] != kAuthWrapperVersion)
        return AuthWrapperVerdict::DropBadVersion;

    const unsigned char *body = rec + kAuthWrapperLen;
    int body_len = (int)(rec_len - kAuthWrapperLen);

    // player_id is a host-order (little-endian on x86) int32 at offset 4.
    int32_t player_id;
    for (size_t i = 0; i < sizeof(player_id); i++)
        ((unsigned char *)&player_id)[i] = body[kUdpHeaderPlayerIdOff + i];

    if (player_id != 0) {
        unsigned char bound[kAuthTokenLen];
        if (!bound_token_for(player_id, bound))
            return AuthWrapperVerdict::DropTokenMismatch;   // no such player / no bound token
        if (!ConstantTimeTokenEqual(bound, rec + 1))
            return AuthWrapperVerdict::DropTokenMismatch;   // forged / stale token
    }

    *inner = body;
    *inner_len = body_len;
    return AuthWrapperVerdict::Accept;
}

}  // namespace net7

#endif  // NET7_AUTH_WRAPPER_H_INCLUDED
