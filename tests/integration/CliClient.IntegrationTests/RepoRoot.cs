// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// Locates the repository root by walking up from the test assembly
/// directory looking for the docker-compose.yml that ServerFixture
/// drives. The walk stops at the filesystem root and throws if the
/// marker file is never found — that means the test project was moved
/// out of the repo (or someone deleted docker-compose.yml).
/// </summary>
public static class RepoRoot
{
    private static readonly Lazy<string> _path = new(Find);

    public static string Path => _path.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "docker-compose.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (docker-compose.yml) above " +
            $"'{AppContext.BaseDirectory}'. Was the test project moved?");
    }
}
