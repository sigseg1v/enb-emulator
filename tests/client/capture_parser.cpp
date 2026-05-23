// tests/client/capture_parser.cpp

#include "capture_parser.h"

#include <cctype>
#include <fstream>
#include <regex>
#include <sstream>
#include <stdexcept>

namespace enbtest {

namespace {

bool IsHexChar(char c) {
    return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}

int HexNibble(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
    return 10 + (c - 'A');
}

// Extract leading whitespace-separated 2-char hex tokens from `line`.
// Stops at the first token that isn't valid hex (typically an ASCII gloss
// or a comment word). Returns the bytes parsed.
std::vector<unsigned char> ExtractHexBytes(const std::string& line) {
    std::vector<unsigned char> out;
    size_t i = 0;
    while (i < line.size()) {
        while (i < line.size() && std::isspace(static_cast<unsigned char>(line[i]))) ++i;
        if (i + 1 >= line.size()) break;
        if (!IsHexChar(line[i]) || !IsHexChar(line[i + 1])) break;
        // Next character after the pair must be whitespace or end-of-line,
        // otherwise it's actually the start of a longer word like "RC4".
        if (i + 2 < line.size() && !std::isspace(static_cast<unsigned char>(line[i + 2]))) break;
        unsigned char byte = static_cast<unsigned char>((HexNibble(line[i]) << 4) | HexNibble(line[i + 1]));
        out.push_back(byte);
        i += 2;
    }
    return out;
}

}  // namespace

std::vector<Packet> ParseCapture(const std::string& text) {
    // Header regex tolerates variable whitespace.
    // Example: "Packet #217: 86 bytes, Server->Client  159.153.232.146:3801"
    static const std::regex header_re(
        R"(^Packet\s+#(\d+):\s+(\d+)\s+bytes,\s+(Client->Server|Server->Client)\s+([0-9.]+):(\d+)\s*$)");

    std::vector<Packet> packets;
    std::istringstream in(text);
    std::string line;

    Packet current;
    bool in_packet = false;

    auto flush = [&]() {
        if (in_packet) {
            packets.push_back(std::move(current));
            current = Packet{};
            in_packet = false;
        }
    };

    while (std::getline(in, line)) {
        // Strip trailing CR (capture files have mixed line endings).
        if (!line.empty() && line.back() == '\r') line.pop_back();

        std::smatch m;
        if (std::regex_match(line, m, header_re)) {
            flush();
            current.sequence = std::stoi(m[1]);
            current.declared_length = std::stoi(m[2]);
            current.direction = (m[3] == "Client->Server")
                                    ? Direction::kClientToServer
                                    : Direction::kServerToClient;
            current.peer_ip = m[4];
            current.peer_port = static_cast<uint16_t>(std::stoi(m[5]));
            in_packet = true;
            continue;
        }

        // Skip dashed separators and section markers.
        if (line.find("---") != std::string::npos) continue;
        if (!in_packet) continue;

        auto bytes = ExtractHexBytes(line);
        if (!bytes.empty()) {
            // Some captures append decrypted-annotation hex (e.g. the
            // "RC4 session key:" block under SYN2) that is NOT part of the
            // on-wire packet. Cap at the declared length.
            size_t room = (current.declared_length > 0 &&
                           current.bytes.size() < static_cast<size_t>(current.declared_length))
                              ? static_cast<size_t>(current.declared_length) - current.bytes.size()
                              : 0;
            if (current.declared_length <= 0) {
                room = bytes.size();
            }
            if (room < bytes.size()) bytes.resize(room);
            if (!bytes.empty()) {
                current.bytes.insert(current.bytes.end(), bytes.begin(), bytes.end());
            }
        }
    }
    flush();
    return packets;
}

std::vector<Packet> ParseCaptureFile(const std::string& path) {
    std::ifstream f(path);
    if (!f) throw std::runtime_error("ParseCaptureFile: cannot open " + path);
    std::ostringstream buf;
    buf << f.rdbuf();
    return ParseCapture(buf.str());
}

}  // namespace enbtest
