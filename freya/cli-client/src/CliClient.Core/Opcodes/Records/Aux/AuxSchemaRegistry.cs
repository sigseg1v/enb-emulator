// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>
/// Candidate top-level Aux schemas (with serialisation flavour and a GameID
/// gate) tried by AuxDataRecord. Opcode 0x001B carries no subtype tag, so we
/// try each candidate's flag-walk and keep the one that consumes the payload
/// exactly (string-plausibility breaks ties). AuxPlayerIndex hard-codes its id
/// slot to 0; entity structures (ship) carry a real id; the manufacturing
/// index carries the terminal/avatar id.
/// </summary>
public static class AuxSchemaRegistry
{
    public enum IdGate { Any, Zero, NonZero }

    public readonly record struct Candidate(AuxSchema Schema, bool Extended, IdGate Gate);

    public static readonly IReadOnlyList<Candidate> Candidates = new[]
    {
        new Candidate(AuxSchemas.PlayerIndex,        false, IdGate.Zero),
        new Candidate(AuxSchemas.PlayerIndex,        true,  IdGate.Zero),
        new Candidate(AuxSchemas.ManufacturingIndex, false, IdGate.NonZero),
        new Candidate(AuxSchemas.ManufacturingIndex, true,  IdGate.NonZero),
        new Candidate(AuxSchemas.ShipIndex,          false, IdGate.NonZero),
        new Candidate(AuxSchemas.ShipIndex,          true,  IdGate.NonZero),
        new Candidate(AuxSchemas.Harvestable,        false, IdGate.NonZero),
    };

    public static bool GateMatches(IdGate gate, uint gameId) => gate switch
    {
        IdGate.Zero    => gameId == 0,
        IdGate.NonZero => gameId != 0,
        _              => true,
    };
}
