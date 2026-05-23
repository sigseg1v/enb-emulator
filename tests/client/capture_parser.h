// tests/client/capture_parser.h
//
// Parser for the human-readable packet-capture text format under
// archive/kyp-snapshot/capturedPackets/. Captures look like:
//
//     Packet #217: 86 bytes, Server->Client  159.153.232.146:3801
//     -----------------------------------------------------------
//
//      01               ACK1
//      14               -unknown-
//      25 46            Session ID
//      40 E6 01 FB      -unknown-
//      ...
//
// We extract the per-packet metadata (sequence, declared length, direction,
// peer address/port) and the raw hex byte stream. Annotations after the hex
// bytes are stripped.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace enbtest {

enum class Direction {
    kClientToServer,
    kServerToClient,
};

struct Packet {
    int sequence = 0;
    int declared_length = 0;          // bytes reported in the header line
    Direction direction = Direction::kClientToServer;
    std::string peer_ip;
    uint16_t peer_port = 0;
    std::vector<unsigned char> bytes; // actual extracted hex bytes
};

// Parses `text` (full capture file contents) into a flat vector of packets.
// Skips section markers like "-- Server Handoff --" and blank lines.
std::vector<Packet> ParseCapture(const std::string& text);

// Convenience: load file then parse.
std::vector<Packet> ParseCaptureFile(const std::string& path);

}  // namespace enbtest
