// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using Xunit;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// xUnit collection-fixture binding: any test class decorated with
/// <c>[Collection(ServerCollection.Name)]</c> shares a single
/// <see cref="ServerFixture"/> instance for the whole test run. The
/// docker stack starts once, not once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ServerCollection : ICollectionFixture<ServerFixture>
{
    public const string Name = "ServerCollection";
}
