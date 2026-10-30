using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. mission 1 played together, in the real host run headless: a client joins and walks its partner.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopSessionTests
{
    [RequiresGameDataFact]
    public void A_client_joins_mission_1_and_walks_its_partner_beside_the_hosts_player()
    {
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        HostOptions options = HostOptions.Parse(["-mission", "1", "-skipcutscene", "-lockstep", "-frames", "4000"]);
        GameHost host = new(new HeadlessPlatformHost(), new HeadlessGameWindow(), device, NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance, AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, files, options, root, NullLogger.Instance);

        LoopbackNetwork network = new();
        using HostedSession session = new(network.Open(26001),
            new HostedSessionOptions("coop", 4, string.Empty, "coop", "1", "test", IncludesHost: true), "igi1", 30, NullLogger.Instance);
        Capture capture = new(session);
        host.TickObserver = capture;
        ClientSession client = new(new NetClient(network.Open(), MultiplayerProtocol.IdFor("igi1")), Igi1Command.ChannelCount);

        float[] forwards = new float[Igi1Command.ChannelCount];
        PlayerInputFrame walk = default;
        walk[(int)PlayerInputSlot.MoveUp] = 1f;
        double now = 0;
        bool connected = false;
        Core.Numerics.Vector3d? start = null;
        host.Start();
        for (int frame = 0; frame < 4000 && host.Step(); frame++)
        {
            if (capture.Game is null)
            {
                continue;
            }

            now += 1.0 / 120;
            if (!connected)
            {
                client.Connect(session.LocalAddress, new JoinRequest("Anya", "test", string.Empty), now);
                connected = true;
            }

            Igi1Command.Pack(walk, capture.Game.World.Player?.Body.Yaw ?? 0f, 0f, forwards);
            client.Receive(now);
            client.SendCommand(forwards, now);
            client.Flush(now);
            if (start is null && capture.Game.World.NetworkPlayers.Count > 0)
            {
                start = capture.Game.World.NetworkPlayers[0].Player.Body.Position;
            }
        }

        host.Stop();
        Assert.NotNull(capture.Game);
        Igi1NetworkPlayer partner = Assert.Single(capture.Game.World.NetworkPlayers);
        double walked = Math.Abs(partner.Player.Body.Position.X - start!.Value.X) + Math.Abs(partner.Player.Body.Position.Y - start.Value.Y);
        Assert.True(walked > 2048, $"the partner walked {walked} units; missed {partner.Commands.Missed}");
        Assert.Equal(1, client.EntityId);

        // The frame is told to draw the partner where it stands.
        Game.Igi1.Ai.PartnerBody shown = Assert.Single(capture.Game.World.PartnerBodies!());
        Assert.Equal(partner.Player.Body.Position, shown.Feet);
    }

    private sealed class Capture(HostedSession session) : IHostTickObserver
    {
        public Igi1Gameplay? Game { get; private set; }

        public void BeforeTitleTick(object title)
        {
            Game ??= title as Igi1Gameplay;
            session.BeforeTitleTick(title);
        }

        public void AfterTitleTick(object title) => session.AfterTitleTick(title);
    }
}
