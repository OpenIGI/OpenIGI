using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Tests;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Measures how each game's own player moves, for comparing the feel: scratch research, not a regression test.</summary>
public sealed class MovementFeelResearch(ITestOutputHelper output)
{
    private static readonly TitlePlayerInput Idle = new(0f, 0f, default, default, 0.5f, false);

    [RequiresTitleDataFact("igi2")]
    public void Measure_both_games()
    {
        StringBuilder report = new();
        report.AppendLine("move,game,ticksTo90,topMps,stopUnits,stopTicks");
        foreach ((string name, PlayerAction keys) in new[]
        {
            ("run forward", PlayerAction.MoveUp), ("run back", PlayerAction.MoveDown), ("strafe right", PlayerAction.MoveRight),
            ("run diagonal", PlayerAction.MoveUp | PlayerAction.MoveRight),
        })
        {
            report.AppendLine(Gait(name, "igi1", Igi1(), keys));
            report.AppendLine(Gait(name, "igi2", Igi2(), keys));
        }

        report.AppendLine();
        report.AppendLine("jump,game,apexMetres,airTicks,distanceMetres,mpsAt5TicksAfterLanding,mpsAt20TicksAfterLanding");
        report.AppendLine(Jump("igi1", Igi1()));
        report.AppendLine(Jump("igi2", Igi2()));

        report.AppendLine();
        report.AppendLine("igi2 tuning: " + Tuning.Value);
        Igi2.Weapons.Igi2Inventory? inventory = Igi2Gameplay.Arm(
            Session.Value.Session.Services, Igi2Level.Find("location1", "level1").Root, Session.Value.Require<Igi2PlayerTuning>())?.Inventory;
        if (inventory is not null)
        {
            foreach (Igi2.Weapons.Igi2WeaponItem w in inventory.Weapons.Where(static w => w.IsActive))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"weapon {w.WeaponType.WeaponId}: slow-down {w.WeaponType.MovementSpeedModifier}");
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"mission 1 loadout slows the player by {inventory.SummedMovementSlowDown:P0} (capped at 50%)");
        }

        output.WriteLine(report.ToString());
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "movement-feel.txt"), report.ToString());
    }

    private static string Gait(string name, string game, IMover mover, PlayerAction keys)
    {
        for (int i = 0; i < 10; i++)
        {
            mover.Tick(0);
        }

        List<double> speeds = [];
        for (int i = 0; i < 60; i++)
        {
            speeds.Add(mover.Tick(keys));
        }

        double top = speeds.Skip(40).Average();
        int to90 = speeds.FindIndex(s => s >= top * 0.9) + 1;
        Vector3d start = mover.Feet;
        int stopTicks = 0;
        for (int i = 0; i < 60; i++)
        {
            double s = mover.Tick(0);
            if (s > 0.05)
            {
                stopTicks = i + 1;
            }
        }

        double stop = Across(mover.Feet - start);
        return string.Create(CultureInfo.InvariantCulture, $"{name},{game},{to90},{top:F2},{stop:F0},{stopTicks}");
    }

    private static string Jump(string game, IMover mover)
    {
        for (int i = 0; i < 10; i++)
        {
            mover.Tick(0);
        }

        for (int i = 0; i < 40; i++)
        {
            mover.Tick(PlayerAction.MoveUp);
        }

        Vector3d start = mover.Feet;
        double ground = start.Z, apex = start.Z;
        mover.Tick(PlayerAction.MoveUp | PlayerAction.Jump);
        int air = 1;
        while (!mover.Grounded && air < 200)
        {
            mover.Tick(PlayerAction.MoveUp);
            apex = Math.Max(apex, mover.Feet.Z);
            air++;
        }

        double distance = Across(mover.Feet - start) / WorldUnits.PerMetre;
        List<double> after = [];
        for (int i = 0; i < 25; i++)
        {
            after.Add(mover.Tick(PlayerAction.MoveUp));
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"forward jump,{game},{(apex - ground) / WorldUnits.PerMetre:F2},{air},{distance:F2},{after[4]:F2},{after[19]:F2}");
    }

    private static double Across(Vector3d d) => Math.Sqrt((d.X * d.X) + (d.Y * d.Y));

    private static Igi1Mover Igi1()
    {
        PlayerCamera rig = new();
        rig.UseAnimations(BoneAnimationSet.Read(File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        rig.UseParameters(RetailPlayerParameters.Value);
        rig.UseWalls(ObjectCollisionIndex.Build([]));
        rig.StandAfresh(default, 0f, 0f);
        return new Igi1Mover(rig);
    }

    private static Igi2Mover Igi2()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            Mission1.Value, Tuning.Value, point.Position, point.Gamma, AiDifficulty.Easy, Clips.Value, level.ModelFor(point.Model)?.BoneHierarchy);
        return new Igi2Mover(player);
    }

    private static readonly Lazy<Igi2Session> Session = new(Igi2Session.Start);

    private static readonly Lazy<Igi2HumanPlayerGeneral> Tuning = new(static () => Session.Value.Require<Igi2PlayerTuning>().General);

    private static readonly Lazy<Igi2CollisionWorld> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        return Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
    });

    private static readonly Lazy<Igi2AnimationLibrary> Clips = new(static () =>
    {
        (List<LoadedPack> packs, Engine.Resources.ResourceRegistry resources) = Igi2LevelScene.Packs(Igi2Level.Find("location1", "level1"));
        return Igi2AnimationLibrary.Load(packs, resources);
    });

    private interface IMover
    {
        Vector3d Feet { get; }

        bool Grounded { get; }

        double Tick(PlayerAction held);
    }

    private sealed class Igi1Mover(PlayerCamera rig) : IMover
    {
        private readonly Flat _ground = new();

        public Vector3d Feet => rig.PositionUnits;

        public bool Grounded => rig.IsGrounded;

        public double Tick(PlayerAction held)
        {
            Vector3d before = Feet;
            rig.Tick(held, _ground);
            return Across(Feet - before) * 30.0 / WorldUnits.PerMetre;
        }
    }

    private sealed class Igi2Mover(Igi2TitlePlayer player) : IMover
    {
        public Vector3d Feet => player.Position;

        public bool Grounded => (player.Human.Signal & Igi2PlayerSignal.Grounded) != 0;

        public double Tick(PlayerAction held)
        {
            Vector3d before = Feet;
            player.Tick(Idle with { Held = held, Pressed = held & PlayerAction.Jump });
            return Across(Feet - before) * 30.0 / WorldUnits.PerMetre;
        }
    }

    private sealed class Flat : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            ground = new TerrainGround(0.0, 0.0, 0.0, 1.0);
            return true;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = default;
            return false;
        }
    }
}
