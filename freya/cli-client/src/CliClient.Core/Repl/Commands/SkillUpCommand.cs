// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>skillup &lt;skillId&gt;</c> -- send one 0x0057 SKILL_UP, spending a skill
/// point to raise <c>skillId</c> by a level (the "train" button in the skill
/// UI). The server validates the spend against the avatar's available points
/// and the skill's availability table; an over-spend or maxed skill is a no-op.
/// </summary>
/// <remarks>
/// The server (Player::HandleSkillAction) ignores the client-supplied
/// SkillPoints field and recomputes the cost itself; only the skill id matters.
/// Byte format pinned in
/// <see cref="N7.CliClient.Opcodes.Outbound.SkillUpCodec"/>.
/// </remarks>
public sealed class SkillUpCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public SkillUpCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "skillup";
    public string Summary => "spend a skill point to raise a skill by one level";
    public string Usage   =>
        "skillup <skillId>\n" +
        "  skillId = the skill to raise (e.g. 55)\n" +
        "  e.g.  skillup 55     train skill 55 one level";
    public string? Placeholder => "<skillId>";
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
        if (!int.TryParse(args[0], out int skillId))
        {
            await output.WriteLineAsync(AnsiPalette.Warn("skillId must be an integer")).ConfigureAwait(false);
            return 1;
        }

        byte[] payload = new SkillUpCodec().EncodeOutbound(new SkillUpMessage(id, skillId));

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.SkillUp.Value, payload), ct).ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("skill-up sent: ") +
            AnsiPalette.Value($"skill {skillId}")).ConfigureAwait(false);
        return 0;
    }
}
