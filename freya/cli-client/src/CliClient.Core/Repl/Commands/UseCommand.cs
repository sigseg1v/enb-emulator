// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>use &lt;slot&gt; [invNum]</c> -- send one 0x005D EQUIP_USE, manually
/// activating the equipped item in <c>slot</c> (fire a weapon, toggle a device).
/// Equip/unequip and ammo loading are inventory moves (<c>inv ... equip ...</c>,
/// 0x0027); this fires what is already equipped.
/// </summary>
/// <remarks>
/// The server reads only the equipment slot index
/// (<c>m_Equip[InvSlot].ManualActivate()</c>); GameID and InvNum are not
/// consumed but are sent to match the retail client wire shape. Byte format
/// pinned in <see cref="N7.CliClient.Opcodes.Outbound.EquipUseCodec"/>.
/// </remarks>
public sealed class UseCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public UseCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name => "use";
    public string Summary => "activate an equipped item (fire weapon / toggle device)";
    public string Usage =>
        "use <slot> [invNum]\n" +
        "  slot   = equipment slot index to activate\n" +
        "  invNum = inventory page (default 2, matches the retail client)\n" +
        "  e.g.  use 3      fire the weapon equipped in slot 3\n" +
        "        use 11     activate the device equipped in slot 11";
    public string? Placeholder => "<slot> [invNum]";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count < 1)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }
        if (!byte.TryParse(args[0], out byte slot))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("slot must be a byte (0-255)")).ConfigureAwait(false);
            return 1;
        }

        byte invNum = EquipUseMessage.DefaultInvNum;
        if (args.Count >= 2 && !byte.TryParse(args[1], out invNum))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("invNum must be a byte (0-255)")).ConfigureAwait(false);
            return 1;
        }

        byte[] payload = new EquipUseCodec().EncodeOutbound(new EquipUseMessage(id, invNum, slot));

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.EquipUse.Value, payload), ct).ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("equip-use sent: ") +
            AnsiPalette.Value($"slot {slot} (invNum {invNum})")).ConfigureAwait(false);
        return 0;
    }
}
