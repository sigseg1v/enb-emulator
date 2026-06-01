// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>
/// Candidate top-level Aux schemas tried by AuxDataRecord, plus the GameID
/// gate used to prune obviously-wrong candidates before the exact-consumption
/// flag-walk. AuxPlayerIndex always carries GameID==0 (its BuildPacket hard-
/// codes the id slot to 0); entity structures (ship/mob) carry a real id.
/// </summary>
public static class AuxSchemaRegistry
{
    public enum IdGate { Any, Zero, NonZero }

    public static readonly IReadOnlyList<(AuxSchema Schema, IdGate Gate)> Entries = new[]
    {
        (AuxSchemas.PlayerIndex, IdGate.Zero),
    };

    public static IEnumerable<AuxSchema> Candidates
    {
        get { foreach (var e in Entries) yield return e.Schema; }
    }

    public static bool GameIdMatches(AuxSchema schema, uint gameId)
    {
        foreach (var e in Entries)
            if (ReferenceEquals(e.Schema, schema))
                return e.Gate switch
                {
                    IdGate.Zero    => gameId == 0,
                    IdGate.NonZero => gameId != 0,
                    _              => true,
                };
        return true;
    }
}
