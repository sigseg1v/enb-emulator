// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Opcodes;

namespace N7.CliClient.Session;

/// <summary>
/// Tunables for <see cref="HealthGuard"/>. Defaults are intentionally
/// conservative — the goal is "trip before we DoS the server", not
/// "maximise throughput".
/// </summary>
public sealed class HealthGuardOptions
{
    /// <summary>
    /// Sliding 1-second window: if more than this many packets are
    /// observed in either direction within 1s, the guard trips. Real
    /// EnB traffic rarely exceeds ~200 packets/s/direction; the default
    /// is 500 so normal play never trips but a runaway loop does.
    /// </summary>
    public int MaxPacketsPerSecond { get; init; } = 500;

    /// <summary>
    /// Default per-request response timeout. Workflows can override per
    /// call to <see cref="HealthGuard.BeginExpectResponse"/>.
    /// </summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opcodes the server only ever sends on error. Receiving any of
    /// these trips the guard immediately. Callers register concrete
    /// error opcodes via <see cref="HealthGuard.RegisterErrorOpcode"/>
    /// or by adding them here. Empty by default — the EnB server
    /// doesn't have a well-known error-opcode set yet.
    /// </summary>
    public IReadOnlyCollection<OpcodeId> ErrorOpcodes { get; init; } = Array.Empty<OpcodeId>();
}
