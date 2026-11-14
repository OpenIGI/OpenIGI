using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Hosting;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// Each game is played on its own mission: a game that runs its level behind its player takes none of another game's
/// mission systems, and a switch of game takes up the other's mission and gives the first back unchanged.
/// </summary>
/// <remarks>
/// Run it in a process of its own (<c>--filter FullyQualifiedName~TitleSwitchTests</c>). A host left standing in the menu
/// disturbs what a later host in the same process draws -- with or without a switch of game; the cause is not found -- so a
/// trace run after it in one process fails at its first frame.
/// </remarks>
[Collection(ParityGroup.Name)]
[Trait("Process", "Own")]
public sealed class TitleSwitchTests
{
    private const int Leg = 60;

    [RequiresTitleDataFact("igi2")]
    public void A_switch_of_game_takes_up_that_games_own_mission_and_back()
    {
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        using RecordingGraphicsDevice device = new();
        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);
        HostOptions options = HostOptions.Parse(
            ["-lockstep", "-nosave", "-titlecycle", Leg.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

        GameHost host = new(
            new HeadlessPlatformHost(),
            new HeadlessGameWindow(),
            device,
            NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            NullLogger.Instance);
        host.Start();
        try
        {
            IHostedMission first = host.HostedForTesting;
            Assert.IsNotType<TitleMission>(first);

            // One leg on, the other game is up, on a mission of its own.
            Run(host, Leg + (Leg / 2));
            IHostedMission second = host.HostedForTesting;
            Assert.NotSame(first, second);
            Assert.IsType<TitleMission>(second);

            // And back: the first game's mission is the one it had.
            Run(host, Leg);
            Assert.Same(first, host.HostedForTesting);

        }
        finally
        {
            host.Stop();
        }
    }

    private static void Run(GameHost host, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Assert.True(host.Step(), "the host stopped");
        }
    }
}
