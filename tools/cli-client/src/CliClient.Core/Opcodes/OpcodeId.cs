// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes;

/// <summary>
/// Wrapper around the 16-bit EnB opcode identifier. Mirrors the
/// <c>ENB_OPCODE_xxxx</c> #defines in <c>common/include/net7/Opcodes.h</c>.
/// Wraps a <see cref="ushort"/> for type safety so an opcode can't be
/// accidentally passed where an arbitrary int is expected.
/// </summary>
public readonly record struct OpcodeId(ushort Value)
{
    public static implicit operator ushort(OpcodeId id) => id.Value;
    public static explicit operator OpcodeId(ushort value) => new(value);

    public override string ToString() => $"0x{Value:X4}";

    /// <summary>
    /// Subset of the opcodes consumed in Phase K integration tests
    /// (and therefore the first opcodes Phase S round-trips). The
    /// remaining opcodes (~200+) come online with
    /// <see cref="OpcodeRegistry"/> + per-opcode stubs in Inbound/
    /// and Outbound/ per plans/19-phase-s-cli-client.md Item 15.
    /// </summary>
    public static class Known
    {
        public static readonly OpcodeId VersionRequest         = new(0x0000);
        public static readonly OpcodeId VersionResponse        = new(0x0001);
        public static readonly OpcodeId Login                  = new(0x0002);
        public static readonly OpcodeId Logoff                 = new(0x0003);
        public static readonly OpcodeId Start                  = new(0x0005);
        public static readonly OpcodeId StartAck               = new(0x0006);
        public static readonly OpcodeId Turn                   = new(0x0012);
        public static readonly OpcodeId Tilt                   = new(0x0013);
        public static readonly OpcodeId Move                   = new(0x0014);
        public static readonly OpcodeId RequestTarget          = new(0x0017);
        public static readonly OpcodeId RequestTargetsTarget   = new(0x0018);
        public static readonly OpcodeId SetTarget              = new(0x0019);
        public static readonly OpcodeId MessageString          = new(0x001D);
        public static readonly OpcodeId Action                 = new(0x002C);
        public static readonly OpcodeId Action2                = new(0x002D);
        public static readonly OpcodeId ClientChat             = new(0x0033);
        public static readonly OpcodeId ClientSetTime          = new(0x0034);
        public static readonly OpcodeId MasterJoin             = new(0x0035);
        public static readonly OpcodeId ServerRedirect         = new(0x0036);
        public static readonly OpcodeId ClientAvatar           = new(0x0037);
        public static readonly OpcodeId ServerHandoff          = new(0x003A);
        public static readonly OpcodeId ClientType             = new(0x003C);
        public static readonly OpcodeId RequestTime            = new(0x0044);
        public static readonly OpcodeId StarbaseRequest        = new(0x004E);
        public static readonly OpcodeId SkillUp                = new(0x0057);
        public static readonly OpcodeId VerbRequest            = new(0x005A);
        public static readonly OpcodeId GlobalConnect          = new(0x006D);
        public static readonly OpcodeId GlobalTicketRequest    = new(0x006E);
        public static readonly OpcodeId GlobalTicket           = new(0x006F);
        public static readonly OpcodeId GlobalAvatarList       = new(0x0070);
        public static readonly OpcodeId GlobalDeleteCharacter  = new(0x0071);
        public static readonly OpcodeId GlobalCreateCharacter  = new(0x0072);
        public static readonly OpcodeId GlobalError            = new(0x0075);
        public static readonly OpcodeId StarbaseRoomChange     = new(0x009F);
        public static readonly OpcodeId LoginStage             = new(0x2020);
        public static readonly OpcodeId LoginStageAck          = new(0x2021);
    }
}
