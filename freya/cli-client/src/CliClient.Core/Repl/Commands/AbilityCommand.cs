// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>ability &lt;abilityIndex&gt; [target]</c> -- activate a trained ability
/// (0x0058 SKILL_ABILITY). With no target it fires self-buffs / non-targeted
/// abilities; with a target it first sends 0x0017 REQUEST_TARGET to lock the
/// object, then the ability -- the "use device on mob" flow, since the ability
/// handler reads the server-side target lock (<c>ShipIndex()-&gt;GetTargetGameID()</c>)
/// rather than a target field in its own packet.
/// </summary>
/// <remarks>
/// <paramref>target</paramref> accepts a game-id literal (<c>0x000187ED</c>,
/// <c>gid=...</c>, decimal) or a tracked object name resolved against the sector
/// world. Byte formats pinned in
/// <see cref="N7.CliClient.Opcodes.Outbound.SkillUseCodec"/> and
/// <see cref="N7.CliClient.Opcodes.Outbound.RequestTargetCodec"/>.
/// </remarks>
public sealed class AbilityCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public AbilityCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name => "ability";
    public string Summary => "activate a trained ability (optionally on a target)";
    public string Usage =>
        "ability <abilityIndex> [target]\n" +
        "  abilityIndex = the ability slot to fire (e.g. 46)\n" +
        "  target       = gid (0x..., gid=..., decimal) or object name; sends\n" +
        "                 REQUEST_TARGET first so the ability fires on it\n" +
        "  e.g.  ability 46            fire a self-buff / non-targeted ability\n" +
        "        ability 46 0x187ED    target the mob, then fire the device on it";
    public string? Placeholder => "<abilityIndex> [target]";
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
        if (!int.TryParse(args[0], out int abilityIndex))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("abilityIndex must be an integer")).ConfigureAwait(false);
            return 1;
        }

        // Optional target: lock it first via REQUEST_TARGET so the ability,
        // which reads the server-side target lock, fires on it.
        int? targetGid = null;
        if (args.Count >= 2)
        {
            if (TargetArg.Resolve(args[1], _ctx.World) is not { } gid)
            {
                await output.WriteLineAsync(AnsiPalette.Warn($"could not resolve target '{args[1]}'")).ConfigureAwait(false);
                return 1;
            }
            targetGid = gid;

            byte[] targetPayload = new RequestTargetCodec().EncodeOutbound(
                new RequestTargetMessage(id, gid));
            await _ctx.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTarget.Value, targetPayload), ct).ConfigureAwait(false);
        }

        byte[] payload = new SkillUseCodec().EncodeOutbound(new SkillUseMessage(id, abilityIndex));
        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.SkillAbility.Value, payload), ct).ConfigureAwait(false);

        string targetNote = targetGid is { } t ? $" on 0x{t:X8}" : "";
        await output.WriteLineAsync(
            AnsiPalette.Ok("ability sent: ") +
            AnsiPalette.Value($"index {abilityIndex}{targetNote}")).ConfigureAwait(false);
        return 0;
    }
}
