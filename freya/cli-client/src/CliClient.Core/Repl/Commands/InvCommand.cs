// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>inv &lt;from&gt; &lt;fromSlot&gt; &lt;to&gt; &lt;toSlot&gt; [num]</c> -- send one
/// 0x0027 INVENTORY_MOVE. The same opcode covers cargo rearrange, equip, vault
/// transfer, vendor buy/sell and jettison; the container keywords select which.
/// </summary>
/// <remarks>
/// Containers: <c>cargo</c>(1) <c>equip</c>(2) <c>vault</c>(3) <c>vendor</c>(4)
/// <c>space</c>(11) <c>manufacture</c>(12). A <c>toSlot</c> of <c>-1</c> (or the
/// keyword <c>free</c>) asks the server to choose the first free slot, which is
/// how vault deposits and vendor sells are sent. Byte format pinned against the
/// live reference server in
/// <see cref="N7.CliClient.Opcodes.Outbound.InventoryMoveCodec"/>.
/// </remarks>
public sealed class InvCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public InvCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "inv";
    public string Summary => "move an item (cargo/equip/vault/vendor/space)";
    public string Usage   =>
        "inv <from> <fromSlot> <to> <toSlot> [num]\n" +
        "  from/to = cargo | equip | vault | vendor | space | manufacture\n" +
        "  fromSlot/toSlot = slot index; toSlot may be 'free' (-1) for first free\n" +
        "  num = stack count to move (default 1)\n" +
        "  e.g.  inv cargo 28 vault free     deposit cargo slot 28 to the vault\n" +
        "        inv cargo 3 vendor free     sell cargo slot 3\n" +
        "        inv vendor 0 cargo free 5   buy 5 of vendor stock slot 0\n" +
        "        inv cargo 2 equip 1         equip cargo slot 2 into equip slot 1";
    public string? Placeholder => "<from> <fromSlot> <to> <toSlot> [num]";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count < 4)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        if (ParseContainer(args[0]) is not { } fromInv)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"unknown container '{args[0]}'")).ConfigureAwait(false);
            return 1;
        }
        if (ParseContainer(args[2]) is not { } toInv)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"unknown container '{args[2]}'")).ConfigureAwait(false);
            return 1;
        }
        if (!TryParseSlot(args[1], out int fromSlot) || !TryParseSlot(args[3], out int toSlot))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("slot must be an integer (or 'free' for toSlot)")).ConfigureAwait(false);
            return 1;
        }

        int num = 1;
        if (args.Count >= 5 && (!int.TryParse(args[4], out num) || num < 1))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("num must be a positive integer")).ConfigureAwait(false);
            return 1;
        }

        byte[] payload = new InventoryMoveCodec().EncodeOutbound(
            new InventoryMoveMessage(id, (int)fromInv, fromSlot, (int)toInv, toSlot, num));

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.InventoryMove.Value, payload), ct).ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("inventory move sent: ") +
            AnsiPalette.Value($"{fromInv}[{fromSlot}] -> {toInv}[{(toSlot == -1 ? "free" : toSlot.ToString())}] x{num}")).ConfigureAwait(false);
        return 0;
    }

    private static InventoryContainer? ParseContainer(string s) => s.ToLowerInvariant() switch
    {
        "cargo"                  => InventoryContainer.Cargo,
        "equip" or "equipped"    => InventoryContainer.Equip,
        "vault" or "secure"      => InventoryContainer.Vault,
        "vendor" or "sell" or "buy" => InventoryContainer.Vendor,
        "space" or "jettison"    => InventoryContainer.Space,
        "manufacture" or "manu"  => InventoryContainer.Manufacture,
        _ => null,
    };

    private static bool TryParseSlot(string s, out int slot)
    {
        if (s.Equals("free", StringComparison.OrdinalIgnoreCase)) { slot = -1; return true; }
        return int.TryParse(s, out slot);
    }
}
