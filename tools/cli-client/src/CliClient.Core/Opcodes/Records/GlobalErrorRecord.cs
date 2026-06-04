// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0075 GLOBAL_ERROR. The global/login server (and the proxy, when it
/// short-circuits a failed login) sends the client a human-readable error
/// string to display. Wire layout (citable from two agreeing in-repo
/// emitters: login-server/Net7SSL/ClientToGlobalServer.cpp:46-55 and
/// proxy/ClientToServer_linux_stubs.cpp:255-264):
///   uint32 Length   @0   message byte length (little-endian)
///   uint32 Code     @4   error code, BIG-ENDIAN -- the emitter writes
///                        ntohl(errorIndex + 7), so ErrorIndex = Code - 7
///   char   Message  @8   Length raw bytes, Latin1, NOT NUL-terminated
///
/// The message is length-prefixed, not NUL-terminated; do not scan for a NUL.
/// </summary>
public sealed class GlobalErrorRecord : PacketRecord
{
    public GlobalErrorRecord(ReadOnlySpan<byte> payload) : base(0x0075, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8)
        {
            Flag(sb, $"GLOBAL_ERROR truncated -- {Payload.Length} bytes, expected at least 8 (length + code)");
            return;
        }

        uint length = ReadU32LE(Payload, 0);
        FDec(sb, 0, "Length", unchecked((int)length));

        int code = ReadI32BE(Payload, 4);   // big-endian: the emitter writes ntohl(index + 7)
        FHex(sb, 4, "Code", code, $"error index {code - 7}");

        if (8 + (long)length > Payload.Length)
        {
            Flag(sb, $"GLOBAL_ERROR message truncated -- declared {length} bytes at offset 8, only {Payload.Length - 8} remain");
            return;
        }

        string message = Encoding.Latin1.GetString(Payload, 8, (int)length);
        FStr(sb, 8, (int)length, "Message", message, required: true);
    }
}
