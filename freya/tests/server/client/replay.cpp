// tests/client/replay.cpp

#include "replay.h"

#include <cstring>

namespace enbtest {

std::vector<Packet> FilterByPort(const std::vector<Packet>& packets, uint16_t peer_port) {
    std::vector<Packet> out;
    for (const auto& p : packets) {
        if (p.peer_port == peer_port)
            out.push_back(p);
    }
    return out;
}

ReplayStats RunReplay(
    TcpClient& client, const std::vector<Packet>& packets, const ReplayOptions& opts,
    westwood::Rc4* tx, westwood::Rc4* rx,
    const std::function<void(const Packet&, const std::vector<unsigned char>&)>& on_response) {
    ReplayStats stats;

    for (const auto& pkt : packets) {
        if (pkt.bytes.empty())
            continue;

        if (pkt.direction == Direction::kClientToServer) {
            std::vector<unsigned char> buf(pkt.bytes);
            if (opts.apply_rc4 && tx) {
                tx->Crypt(buf.data(), static_cast<long>(buf.size()));
            }
            int sent = client.Send(buf.data(), static_cast<int>(buf.size()));
            if (sent != static_cast<int>(buf.size())) {
                stats.last_error = "send failed at packet #" + std::to_string(pkt.sequence) + ": " +
                                   client.last_error();
                return stats;
            }
            ++stats.packets_sent;
        } else {
            std::vector<unsigned char> buf(pkt.bytes.size(), 0);
            if (!opts.require_responses) {
                // This captured response may be unreproducible from a raw
                // byte-replay (see ReplayOptions::require_responses). Do a
                // best-effort single read to classify the outcome:
                //   n > 0  -- the proxy replied; fall through to the checks.
                //   n == 0 -- the proxy cleanly CLOSED the connection: a real
                //             regression (it dropped us on valid traffic).
                //   n < 0  -- timeout (the dominant case here): the proxy
                //             accepted our send and is still processing the
                //             handoff. Record the missing response, continue.
                int n = client.RecvSome(buf.data(), static_cast<int>(buf.size()),
                                        opts.response_timeout_ms);
                if (n == 0) {
                    stats.last_error = "peer closed connection at packet #" +
                                       std::to_string(pkt.sequence) +
                                       " (proxy dropped a live connection)";
                    return stats;
                }
                if (n < 0) {
                    ++stats.responses_missing;
                    continue;
                }
                // n > 0: a real reply arrived. Trim to what we actually read
                // so the opcode check below inspects valid bytes.
                buf.resize(static_cast<size_t>(n));
            } else if (!client.RecvExact(buf.data(), static_cast<int>(buf.size()),
                                         opts.response_timeout_ms)) {
                stats.last_error = "recv failed at packet #" + std::to_string(pkt.sequence) + ": " +
                                   client.last_error();
                return stats;
            }
            if (opts.apply_rc4 && rx) {
                rx->Crypt(buf.data(), static_cast<long>(buf.size()));
            }
            ++stats.packets_received;

            if (opts.verify_response_opcode && pkt.bytes.size() >= 4 && buf.size() >= 4) {
                // EnB TCP header: length (LE u16) + opcode (LE u16). We
                // compare only the opcode (bytes 2-3); the length is
                // already implied by how many bytes we read.
                if (buf[2] != pkt.bytes[2] || buf[3] != pkt.bytes[3]) {
                    ++stats.opcode_mismatches;
                }
            }
            if (on_response)
                on_response(pkt, buf);
        }
    }
    return stats;
}

} // namespace enbtest
