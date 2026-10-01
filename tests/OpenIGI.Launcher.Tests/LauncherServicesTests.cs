using System.Net;
using System.Net.Http.Headers;
using OpenIGI.Core.Titles;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class LauncherServicesTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("-windowed", new[] { "-windowed" })]
    [InlineData("  -windowed   -skipintro ", new[] { "-windowed", "-skipintro" })]
    [InlineData("-mods \"C:\\My Mods\" -x", new[] { "-mods", "C:\\My Mods", "-x" })]
    [InlineData("-name \"\"", new[] { "-name", "" })]
    public void Launch_arguments_split_like_a_shell(string line, string[] expected) =>
        Assert.Equal(expected, GameStarter.Split(line));

    [Fact]
    public void The_game_is_told_its_title_and_folder_before_the_players_own_arguments() =>
        Assert.Equal(
            ["-title", "igi2", "-game", "D:/IGI2/pc", "-windowed"],
            GameStarter.Arguments(GameTitle.Igi2, "D:/IGI2/pc", "-windowed"));

    [Fact]
    public void A_folder_is_an_installation_only_with_the_titles_signature_file()
    {
        string dir = Directory.CreateTempSubdirectory("openigi-retail-").FullName;
        try
        {
            Assert.False(GameStarter.IsInstallation(GameTitle.Igi1, dir));
            File.WriteAllText(Path.Combine(dir, GameTitle.Igi1.SignatureFile), string.Empty);
            Assert.True(GameStarter.IsInstallation(GameTitle.Igi1, dir));
            Assert.True(GameStarter.IsInstallation(GameTitle.Igi1, $"\"{dir}\""));
            Assert.False(GameStarter.IsInstallation(GameTitle.Igi2, dir));
            Assert.Equal(dir, GameStarter.Locate(GameTitle.Igi1, dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Self_update_moves_the_running_files_aside_and_copies_the_new_ones_in()
    {
        string root = Directory.CreateTempSubdirectory("openigi-selfupdate-").FullName;
        try
        {
            string running = Directory.CreateDirectory(Path.Combine(root, "running")).FullName;
            string staged = Directory.CreateDirectory(Path.Combine(root, "staged")).FullName;
            File.WriteAllText(Path.Combine(running, "OpenIGI.Launcher.exe"), "old");
            File.WriteAllText(Path.Combine(running, "kept.dll"), "kept");
            File.WriteAllText(Path.Combine(staged, "OpenIGI.Launcher.exe"), "new");
            Directory.CreateDirectory(Path.Combine(staged, "Assets"));
            File.WriteAllText(Path.Combine(staged, "Assets", "backdrop.webp"), "art");

            LauncherUpdater.Apply(staged, running);

            Assert.Equal("new", File.ReadAllText(Path.Combine(running, "OpenIGI.Launcher.exe")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(running, "OpenIGI.Launcher.exe.old")));
            Assert.Equal("art", File.ReadAllText(Path.Combine(running, "Assets", "backdrop.webp")));
            Assert.Equal("kept", File.ReadAllText(Path.Combine(running, "kept.dll")));

            // A second update in a row replaces the first one's leftovers rather than failing on them.
            File.WriteAllText(Path.Combine(staged, "OpenIGI.Launcher.exe"), "newer");
            LauncherUpdater.Apply(staged, running);
            Assert.Equal("newer", File.ReadAllText(Path.Combine(running, "OpenIGI.Launcher.exe")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(running, "OpenIGI.Launcher.exe.old")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_a_damaged_file_falls_back_to_defaults()
    {
        string dir = Directory.CreateTempSubdirectory("openigi-settings-").FullName;
        try
        {
            string path = Path.Combine(dir, "nested", "settings.json");
            LauncherSettings settings = new() { Channel = ReleaseChannel.Beta, LaunchArguments = "-windowed", CloseOnLaunch = true };
            settings.GameFolders["igi1"] = "D:/IGI";
            settings.Save(path);

            LauncherSettings read = LauncherSettings.Load(path);
            Assert.Equal(ReleaseChannel.Beta, read.Channel);
            Assert.Equal("-windowed", read.LaunchArguments);
            Assert.True(read.CloseOnLaunch);
            Assert.Equal("D:/IGI", read.GameFolders["igi1"]);
            Assert.Contains("\"beta\"", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, "{ broken");
            Assert.Equal(ReleaseChannel.Stable, LauncherSettings.Load(path).Channel);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task The_feed_is_asked_conditionally_and_an_unchanged_answer_is_the_last_one()
    {
        ChannelFeed feed = new(ReleaseChannel.Beta, null, []);
        int served = 0;
        FakeServer server = new()
        {
            Override = request =>
            {
                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "\"t1\""))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                served++;
                HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson.Write(feed)) };
                response.Headers.ETag = new EntityTagHeaderValue("\"t1\"");
                return response;
            },
        };

        ReleaseFeedClient client = new(new HttpClient(server));
        Uri service = new("https://api.test/");
        ChannelFeed first = await client.FetchAsync(service, ReleaseChannel.Beta, "0.1.0", CancellationToken.None);
        ChannelFeed second = await client.FetchAsync(service, ReleaseChannel.Beta, "0.1.0", CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, served);
        Assert.Equal("https://api.test/v1/channels/beta?since=0.1.0", server.Requests[0].RequestUri!.ToString());

        // A different question is never answered from the last one's tag.
        await client.FetchAsync(service, ReleaseChannel.Stable, "0.1.0", CancellationToken.None);
        Assert.Empty(server.Requests[2].Headers.IfNoneMatch);
    }
}
