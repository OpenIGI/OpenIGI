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
        (Capture capture, ClientSession client, Core.Numerics.Vector3d? start) = Play(4000, onFrame: null);
        Assert.NotNull(capture.Game);
        Igi1NetworkPlayer partner = Assert.Single(capture.Game.World.NetworkPlayers);
        double walked = Math.Abs(partner.Player.Body.Position.X - start!.Value.X) + Math.Abs(partner.Player.Body.Position.Y - start.Value.Y);
        Assert.True(walked > 2048, $"the partner walked {walked} units; missed {partner.Commands.Missed}");
        Assert.Equal(1, client.EntityId);

        // The frame is told to draw the partner where it stands.
        Game.Igi1.Ai.PartnerBody shown = Assert.Single(capture.Game.World.PartnerBodies!());
        Assert.Equal(partner.Player.Body.Position, shown.Feet);

        // Walking on its own clips, and holding what the level arms its player with.
        Assert.True(shown.Animation >= 0, $"the partner's legs are on clip {shown.Animation}");
        Assert.False(string.IsNullOrEmpty(shown.Weapon?.GunModel), "the partner holds no weapon");
    }

    [RequiresGameDataFact]
    public void The_hosts_player_dies_the_partner_plays_on_and_the_host_is_brought_back()
    {
        const int KillFrame = 600;
        int? killedAt = null;
        float yawAtDeath = 0f;
        double turnedWhileDown = 0;
        bool levelSawDeath = false;
        bool deadSeen = false;

        // The client turns once the host's player is down: its partner must turn with it, on its own commands.
        (Capture capture, _, _) = Play(
            4000,
            (game, frame) =>
            {
                if (game.World.NetworkPlayers.Count == 0)
                {
                    return;
                }

                Igi1NetworkPlayer partner = game.World.NetworkPlayers[0];
                if (killedAt is null && frame > KillFrame && game.World.Player is { } host)
                {
                    game.Harm.BeginPlayerDeath(host.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
                    killedAt = frame;
                    yawAtDeath = partner.Player.Body.Yaw;
                }

                if (killedAt is not null && game.World.IsPlayerDead)
                {
                    deadSeen = true;
                    turnedWhileDown = Math.Max(turnedWhileDown, Math.Abs(partner.Player.Body.Yaw - yawAtDeath));
                    levelSawDeath |= game.World.LevelRoot?.Tree.FromId(0) is HumanPlayerTask { IsDead: true };
                }
            },
            turn: frame => frame > KillFrame ? Math.Min(1f, (frame - KillFrame) * 0.02f) : 0f);

        Assert.NotNull(capture.Game);
        Assert.NotNull(killedAt);
        Assert.True(deadSeen, "the host's player never lay dead");
        Assert.True(turnedWhileDown > 0.5, $"the partner turned {turnedWhileDown} radians while the host's player was down");
        Assert.False(levelSawDeath, "the level saw its player dead with a partner alive");
        Assert.False(capture.Game.World.IsPlayerDead, "the host's player was not brought back");
        Assert.False(capture.Game.World.PlayerHealth.IsDead);
    }

    // Mission 1 hosted headless with one client walking forwards, for the frames given; onFrame sees each frame once the game is up.
    private static (Capture Capture, ClientSession Client, Core.Numerics.Vector3d? Start) Play(int frames, Action<Igi1Gameplay, int>? onFrame, Func<int, float>? turn = null)
    {
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        HostOptions options = HostOptions.Parse(["-mission", "1", "-skipcutscene", "-lockstep", "-frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
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
        for (int frame = 0; frame < frames && host.Step(); frame++)
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

            Igi1Command.Pack(walk, (capture.Game.World.Player?.Body.Yaw ?? 0f) + (turn?.Invoke(frame) ?? 0f), 0f, forwards);
            client.Receive(now);
            client.SendCommand(forwards, now);
            client.Flush(now);
            if (start is null && capture.Game.World.NetworkPlayers.Count > 0)
            {
                start = capture.Game.World.NetworkPlayers[0].Player.Body.Position;
            }

            onFrame?.Invoke(capture.Game, frame);
        }

        host.Stop();
        return (capture, client, start);
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
