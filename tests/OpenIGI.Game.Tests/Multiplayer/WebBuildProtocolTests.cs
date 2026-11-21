using System.Text.Json;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>
/// The browser build's word on the protocol it plays (apps/OpenIGI.Web/wwwroot/build.json), which the website's sessions page
/// reads to send a player to a browser build that can join a session. It is a file the page fetches before any of the game
/// loads, so it is written out by hand; this keeps it the protocol's own.
/// </summary>
public sealed class WebBuildProtocolTests
{
    [Fact]
    public void The_browser_build_says_the_protocol_it_plays()
    {
        string path = Path.Combine(RepositoryRoot(), "apps", "OpenIGI.Web", "wwwroot", "build.json");
        using JsonDocument build = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(MultiplayerProtocol.Version, build.RootElement.GetProperty("net").GetInt32());
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests run outside the repository.");
    }
}
