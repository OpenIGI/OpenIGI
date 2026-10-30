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
        Assert.Equal("Anya", shown.Name);

        // Walking on its own clips, and holding what the level arms its player with.
        Assert.True(shown.Animation >= 0, $"the partner's legs are on clip {shown.Animation}");
        Assert.False(string.IsNullOrEmpty(shown.Weapon?.GunModel), "the partner holds no weapon");
    }

    [RequiresGameDataFact]
    public void The_hosts_player_dies_the_partner_plays_on_and_revives_the_host_by_standing_beside_it()
    {
        const int KillFrame = 600;
        const int AloneFrames = 600;
        int? killedAt = null;
        int? besideFrom = null;
        int? revivedAt = null;
        bool downWhileAlone = false;
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

                if (killedAt is not null && game.World.IsPlayerDead && game.World.Player is { } body)
                {
                    deadSeen = true;
                    turnedWhileDown = Math.Max(turnedWhileDown, Math.Abs(partner.Player.Body.Yaw - yawAtDeath));
                    levelSawDeath |= game.World.LevelRoot?.Tree.FromId(0) is HumanPlayerTask { IsDead: true };

                    // Kept well away first, then stood a metre from the body: only that brings the host back.
                    bool alone = frame < killedAt + AloneFrames;
                    double off = alone ? 20 * 4096 : 4096;
                    partner.Player.Body.Position = body.Body.Position + new Core.Numerics.Vector3d(off, 0, 0);
                    if (alone && frame == killedAt + AloneFrames - 1)
                    {
                        downWhileAlone = true;
                    }

                    besideFrom ??= alone ? null : frame;
                }

                if (killedAt is not null && besideFrom is not null && revivedAt is null && !game.World.IsPlayerDead)
                {
                    revivedAt = frame;
                }
            },
            turn: frame => frame > KillFrame ? Math.Min(1f, (frame - KillFrame) * 0.02f) : 0f);

        Assert.NotNull(capture.Game);
        Assert.NotNull(killedAt);
        Assert.True(deadSeen, "the host's player never lay dead");
        Assert.True(turnedWhileDown > 0.5, $"the partner turned {turnedWhileDown} radians while the host's player was down");
        Assert.False(levelSawDeath, "the level saw its player dead with a partner alive");
        Assert.True(downWhileAlone, "the host's player came back with nobody beside it");
        Assert.NotNull(revivedAt);
        Assert.True(
            revivedAt - besideFrom >= Igi1ReplicatedWorld.ReviveTicks - 2,
            $"revived {revivedAt - besideFrom} frames after the partner arrived; a revival takes {Igi1ReplicatedWorld.ReviveTicks}");
        Assert.False(capture.Game.World.IsPlayerDead, "the host's player was not brought back");
        Assert.False(capture.Game.World.PlayerHealth.IsDead);
    }

    [RequiresGameDataFact]
    public void A_round_that_meets_a_partner_is_taken_by_the_players_own_hit_handler()
    {
        const int HitFrame = 700;
        float before = 0f;
        float after = 0f;
        Core.Numerics.Vector3f shove = default;
        List<ReplicatedSense> felt = [];
        (Capture capture, _, _) = Play(
            900,
            (game, frame) =>
            {
                if (frame != HitFrame || game.World.NetworkPlayers.Count == 0 || game.World.PlayerEvents is not { } events)
                {
                    return;
                }

                // A round from ten metres in front of the partner, through its chest.
                Igi1NetworkPlayer partner = game.World.NetworkPlayers[0];
                Core.Numerics.Vector3d chest = partner.Player.Body.Position + new Core.Numerics.Vector3d(0, 0, 1.3 * 4096);
                Core.Numerics.Vector3d from = chest + new Core.Numerics.Vector3d(10 * 4096, 0, 0);
                before = partner.Health.RemainingFraction;
                Core.Numerics.Vector3f shoveBefore = partner.Player.Body.HitShove;
                events.TakeRound(
                    partner,
                    new Game.Igi1.Ai.Igi1WeaponHit(
                        new HumanHit(chest, 10 * 4096, HumanDamage.FrontZone, 0f, 0f), from, chest, 0.2f, Game.Weapons.WeaponCategory.Gun, null, FromPlayer: false));
                after = partner.Health.RemainingFraction;
                shove = partner.Player.Body.HitShove - shoveBefore;
                partner.RemoteSenses.TakeInto(felt);
            });

        Assert.NotNull(capture.Game);
        Assert.True(after < before, $"the partner took no damage ({before:P0} -> {after:P0})");

        // The handler's shove, along the round's travel: towards -X.
        Assert.True(shove.X < 0f && Math.Abs(shove.Y) < 1e-3f, $"the partner was shoved by {shove}");

        // What it felt, in the handler's order, for its client: the grunt, the shake, the red from the side it came in.
        Assert.Equal(3, felt.Count);
        Assert.All(felt, sense => Assert.Equal(1, sense.Entity));
        Assert.True(felt[2].Vector.X < 0f, $"the flash came from {felt[2].Vector}");
    }

    [RequiresGameDataFact]
    public void A_partners_shots_are_shown_to_the_others()
    {
        bool fired = false;
        int shots = 0;
        double bestAlignment = -2;
        int lines = 0;
        (Capture capture, _, _) = Play(
            1200,
            (game, frame) =>
            {
                if (game.World.NetworkPlayers.Count == 0 || game.World.PartnerBodies is not { } bodies)
                {
                    return;
                }

                fired |= bodies().Any(static b => b.Fired);
                Igi1NetworkPlayer partner = game.World.NetworkPlayers[0];
                shots = partner.Player.Weapons?.ShotSequence ?? 0;

                // Its shots leave lines along where it looks (the camera's forward is its look rotation's third row).
                Core.Numerics.Matrix3f look = partner.Player.Camera.Orientation;
                Core.Numerics.Vector3d forward = new(look.M31, look.M32, look.M33);
                foreach (Game.Igi1.Weapons.GunShotTracer line in game.World.Soldiers?.GunShotTracers ?? [])
                {
                    if (ReferenceEquals(line.Shooter, partner))
                    {
                        lines++;
                        Core.Numerics.Vector3d along = line.End - line.Start;
                        bestAlignment = Math.Max(bestAlignment, ((along.X * forward.X) + (along.Y * forward.Y) + (along.Z * forward.Z)) / along.Length);
                    }
                }
            },
            fire: frame => frame > 700 && frame % 20 < 10,
            press: frame => frame is >= 640 and < 644 ? PlayerInputSlot.WeaponCategory2 : null);

        Assert.NotNull(capture.Game);
        Assert.True(shots > 0, "the partner never fired");
        Assert.True(fired, $"the partner fired {shots} rounds but its body never said so");
        Assert.True(lines > 0, "the partner's shots left no line");
        Assert.True(bestAlignment > 0.95, $"the partner's line points {bestAlignment} along its look");
    }

    // Mission 1 hosted headless with one client walking forwards, for the frames given; onFrame sees each frame once the game is up.
    private static (Capture Capture, ClientSession Client, Core.Numerics.Vector3d? Start) Play(int frames, Action<Igi1Gameplay, int>? onFrame, Func<int, float>? turn = null, Func<int, bool>? fire = null, Func<int, PlayerInputSlot?>? press = null)
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

            walk[(int)PlayerInputSlot.Fire] = fire?.Invoke(frame) == true ? 1f : 0f;
            PlayerInputSlot? pressed = press?.Invoke(frame);
            if (pressed is { } key)
            {
                walk[(int)key] = 1f;
            }

            Igi1Command.Pack(walk, (capture.Game.World.Player?.Body.Yaw ?? 0f) + (turn?.Invoke(frame) ?? 0f), 0f, forwards);
            if (pressed is { } released)
            {
                walk[(int)released] = 0f;
            }
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
