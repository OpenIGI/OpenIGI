using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Flow;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Vehicles;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Modding.Testing;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>An I.G.I. 2 mission loaded headless brings its level up as the game does, a mod's systems among the game's.</summary>
public sealed class Igi2MissionFixtureTests
{
    [RequiresTitleDataFact("igi2")]
    public void Mission_one_stands_its_player_with_its_level_and_soldiers_brought_up()
    {
        Igi2Mission mission = MissionFixture.Igi2(11);

        Igi2TitlePlayer player = Assert.IsType<Igi2TitlePlayer>(mission.Stand());

        Assert.NotNull(player.Level);
        Assert.NotNull(player.Ai);
        Assert.NotEmpty(player.Soldiers!.Bodies);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_mods_level_system_runs_among_the_games_as_the_mission_is_stood()
    {
        MarkingMod mod = new();
        Igi2Mission mission = MissionFixture.Igi2(11, ModHarness.Of(mod, titles: ["igi2"]));

        mission.Stand();

        Assert.True(mod.Ran);
    }

    /// <summary>
    /// Mission 12's trains carry what rides them: the switch on the player's train, and the guards standing on the graph that hangs
    /// from the guard train. Both stayed where the level put them, in the air, as the trains drove off.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_twelves_trains_carry_their_switch_and_their_guards()
    {
        Igi2Mission mission = MissionFixture.Igi2(12);
        Igi2TitlePlayer player = Assert.IsType<Igi2TitlePlayer>(mission.Stand());
        GameTask root = player.Level!.Root;
        Igi2TrainTask playerTrain = root.Descendants().OfType<Igi2TrainTask>().Single(static t => t.Id == 3989);
        Igi2TrainTask guardTrain = root.Descendants().OfType<Igi2TrainTask>().Single(static t => t.Id == 450);
        Igi2SwitchTask button = root.Descendants().OfType<Igi2SwitchTask>().Single(static t => t.Id == 163);
        Igi2HumanSoldier guard = root.Descendants().OfType<Igi2HumanSoldierTask>().Single(static s => s.Id == 418).Body!;

        // The script's go: EditVariable_164 starts the player's train, whose AnimTask starts the guards'.
        root.Descendants().OfType<EditVariableTask>().Single(static v => v.Id == 164).Value = 1;
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(default);
        }

        Vector3d trainStart = playerTrain.CurrentPosition, guardTrainStart = guardTrain.CurrentPosition;
        double switchOffset = Distance(playerTrain.CurrentPosition, button.CurrentPosition);
        double guardOffset = Distance(guardTrain.CurrentPosition, guard.Position);
        for (int tick = 0; tick < 600; tick++)
        {
            player.Tick(default);
        }

        Assert.True(Distance(trainStart, playerTrain.CurrentPosition) > 4096 * 50, "the player's train runs");
        Assert.True(Distance(guardTrainStart, guardTrain.CurrentPosition) > 4096 * 50, "the guards' train runs");
        Assert.InRange(Distance(playerTrain.CurrentPosition, button.CurrentPosition), switchOffset - 64, switchOffset + 64);
        Assert.InRange(Distance(guardTrain.CurrentPosition, guard.Position), guardOffset - 1024, guardOffset + 1024);
    }

    /// <summary>
    /// A blown-up object in mission 14 goes off as retail's does: fireballs, the explosion's smoke puffs in the level's particles
    /// (none were made), and the fire its container holds lighting once it has exploded.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_exploding_object_throws_fire_and_smoke_and_lights_its_fire()
    {
        Igi2Mission mission = MissionFixture.Igi2(14);
        Igi2TitlePlayer player = Assert.IsType<Igi2TitlePlayer>(mission.Stand());
        GameTask root = player.Level!.Root;
        Igi2ExplodeObjectTask tank = root.Descendants().OfType<Igi2ExplodeObjectTask>().Single(static e => e.Id == 4004);
        Game.World.Particles.Particle2DModule particles = player.Particles!;
        int smokeBefore = particles.Systems.Count(static s => s.Look.Sprite == Game.World.ExplosionSmoke.Sprite);
        int fireBefore = particles.Systems.Where(static s => s.Look.Sprite == "fireball.spr" && s.IsInWorld).Sum(static s => s.Count);

        tank.Explosive.WeaponHit(100000f);
        player.Tick(default);
        Assert.NotEmpty(player.Explosions!.FireBalls);

        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(default);
        }

        Assert.True(particles.Systems.Count(static s => s.Look.Sprite == Game.World.ExplosionSmoke.Sprite) > smokeBefore, "the explosion smokes");
        Assert.True(particles.Systems.Where(static s => s.Look.Sprite == "fireball.spr" && s.IsInWorld).Sum(static s => s.Count) > fireBefore, "its fire burns");
    }

    private static double Distance(Vector3d a, Vector3d b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    // A mod that notes its system ran, just before the level's AI.
    private sealed class MarkingMod : IOpenIgiMod
    {
        public bool Ran { get; private set; }

        public void Configure(IModBuilder builder) =>
            builder.LevelSystems<Igi2LevelBuild>().InsertBefore(Igi2LevelSystems.Ai, "Test.Mark", _ => Ran = true);
    }
}
