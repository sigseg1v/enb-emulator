// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="GlobalErrorRecord"/> (0x0075 GLOBAL_ERROR), the
/// structured decoder added for plans/31 AD-1. 0x0075 is an error-only reply on
/// the global/login leg, so there is no routine capture to replay; instead we
/// synthesise the EXACT bytes the two agreeing in-repo emitters produce
/// (login-server/Net7SSL/ClientToGlobalServer.cpp:46-55 and
/// proxy/ClientToServer_linux_stubs.cpp:255-264) and assert the decoder reads
/// every field AND consumes every byte (no <c>???</c> gap = full coverage).
///
/// Wire layout: uint32 Length (LE) + uint32 Code (BIG-ENDIAN, = ntohl(index+7))
/// + Length raw message bytes (Latin1, NOT NUL-terminated).
/// </summary>
public sealed class GlobalErrorRecordTests
{
    public GlobalErrorRecordTests() => AnsiPalette.Enabled = false;

    /// <summary>Builds the frame exactly as the emitters do for a given error index + message.</summary>
    private static byte[] Build(int errorIndex, string message)
    {
        byte[] msg = Encoding.Latin1.GetBytes(message);
        var b = new byte[8 + msg.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), (uint)msg.Length);
        // Emitter writes *((int*)p) = ntohl(index + 7); on a little-endian host
        // that lands big-endian on the wire.
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), errorIndex + 7);
        msg.CopyTo(b, 8);
        return b;
    }

    [Theory]
    // index, message (verbatim from the emitter's error table)
    [InlineData(0, "Error: You have been temporarily banned.")]
    [InlineData(4, "Sorry, this name needs enough vowels (a,e,i,o,u & y) to be pronouncable. Please try again.")]
    [InlineData(6, "Error: Ticket Validation Failed.")]
    public void GlobalError_0x0075_FullyDecoded(int index, string message)
    {
        byte[] frame = Build(index, message);

        var rec = PacketRecord.Resolve(0x0075, frame);
        Assert.IsType<GlobalErrorRecord>(rec);

        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);   // every byte decoded
        Assert.DoesNotContain("[!]", dump);   // no truncation / overrun flag

        // Independent field readback.
        Assert.Equal((uint)Encoding.Latin1.GetByteCount(message),
                     BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0)));
        Assert.Equal(index + 7, BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4)));
        Assert.Contains(message, dump);
        Assert.Contains($"error index {index}", dump);   // Code decodes back to the index
    }

    [Fact]
    public void GlobalError_0x0075_TruncatedHeader_Flags()
    {
        var rec = PacketRecord.Resolve(0x0075, new byte[] { 0x01, 0x00, 0x00 });   // < 8 bytes
        string dump = rec.DumpToString();
        Assert.Contains("[!]", dump);
    }

    [Fact]
    public void GlobalError_0x0075_DeclaredLengthOverruns_Flags()
    {
        // Length says 100 but only a few message bytes are present.
        var b = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), 100);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), 7);
        var rec = PacketRecord.Resolve(0x0075, b);
        string dump = rec.DumpToString();
        Assert.Contains("[!]", dump);
    }
}
