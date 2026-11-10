using System.Reflection;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// Everything the played mission holds is either put back by a match's new round (<see cref="ILevelReset"/>) or named here,
/// with why it needs nothing: a part added later fails this until it is one or the other, so it cannot be forgotten.
/// </summary>
public sealed class LevelResetCoverageTests
{
    // The parts a round puts back: each must take ILevelReset (MissionState.LevelParts / MissionWorld.LevelParts list them).
    private static readonly HashSet<string> Reset =
    [
        nameof(MissionState.Doors),
        nameof(MissionState.Elevators),
        nameof(MissionState.Interactions),
        nameof(MissionState.GunPickups),
        nameof(MissionState.GenericPickups),
        nameof(MissionState.AlarmSpeakers),
        nameof(MissionState.Destructibles),
        nameof(MissionState.SecurityCameras),
        nameof(MissionState.RotatingObjects),
        nameof(MissionState.Objectives),
    ];

    // The rest, and why a round needs nothing of them.
    private static readonly Dictionary<string, string> Exempt = new()
    {
        // Data the level was loaded with: play reads it and never changes it.
        [nameof(MissionState.AmmoTypes)] = "data",
        [nameof(MissionState.WeaponTypes)] = "data",
        [nameof(MissionState.Materials)] = "data",
        [nameof(MissionState.MagicObjects)] = "data",
        [nameof(MissionState.VehiclePhysics)] = "data",
        [nameof(MissionState.Terrain)] = "data",
        [nameof(MissionState.Ground)] = "data",
        [nameof(MissionState.LevelRoot)] = "data: an arena keeps every task (pickups keep theirs when taken)",
        [nameof(MissionWorld.AiConfig)] = "data",
        [nameof(MissionWorld.AnimationTriggers)] = "data",
        [nameof(MissionWorld.MineFieldExpressions)] = "reads the variables, holds none",
        [nameof(MissionState.Spins)] = "IGI 2's",
        [nameof(MissionState.Solids)] = "moved by the parts that own its entries, as they go back",
        [nameof(MissionState.Bodies)] = "wiring: who can be shot",
        [nameof(MissionState.Joined)] = "wiring: who can be seen and blasted",
        [nameof(MissionWorld.GuardRounds)] = "resolves a guard's shot as it is fired; nothing in flight",
        [nameof(MissionState.Collision)] = "a view of Solids and the terrain",
        [nameof(MissionState.CutsceneFrameShown)] = "a view of Cutscenes",
        [nameof(MissionState.VehicleRoundOwner)] = "set and cleared around one cast",
        [nameof(MissionWorld.PlayerStandIn)] = "a view of PlayerTask",
        [nameof(MissionWorld.Partners)] = "a view of NetworkPlayers / ShownPartners",

        // Put back by the title's reset itself (Igi1Gameplay.ResetToLevelStart, MissionWorld.LevelParts).
        [nameof(MissionWorld.RetailWorld)] = "MissionWorld.LevelParts: its panes of glass",
        [nameof(MissionWorld.MineFields)] = "MissionWorld.LevelParts: their waits",
        [nameof(MissionWorld.ShownProjectiles)] = "cleared by Igi1Gameplay.ResetToLevelStart",
        [nameof(MissionWorld.VehicleMissiles)] = "cleared by Igi1Gameplay.ResetToLevelStart",
        [nameof(MissionWorld.ViewTakeover)] = "cleared by Igi1Gameplay.ResetToLevelStart, with each player's ControlTaken",
        [nameof(MissionWorld.OfferedPrompt)] = "asked for again on every tick the player updates",
        [nameof(MissionWorld.SavesUsed)] = "the campaign's save games, which a match has none of",
        [nameof(MissionWorld.GameLoads)] = "the campaign's save games, which a match has none of",

        // Held still in an arena, so a round finds them as the last did.
        [nameof(MissionState.Cutscenes)] = "held in an arena (CutsceneRuntime.Held)",
        [nameof(MissionWorld.Soldiers)] = "an arena has none (ArenaLevel.RemoveSoldiers)",
        [nameof(MissionWorld.SoldierPresenter)] = "an arena has no soldiers to show",

        // The players: the match puts each back at a spawn, restocked.
        [nameof(MissionState.PlayerHealth)] = "the match's respawn",
        [nameof(MissionWorld.Player)] = "the match's respawn; its grenades, rockets and marks cleared by the title",
        [nameof(MissionWorld.PlayerTask)] = "the match's respawn",
        [nameof(MissionWorld.PlayerEvents)] = "wiring",
        [nameof(MissionWorld.NetworkPlayers)] = "the match's respawn; their projectiles and marks cleared by the title",
        [nameof(MissionWorld.ShownPartners)] = "the snapshots' to show",
        [nameof(MissionState.DamageFlash)] = "the local screen's, over in a moment",
        [nameof(MissionWorld.FlashbangFlash)] = "the local screen's, over in a moment",

        // Running on whatever the round is: sounds and effects that follow the level's state, the streams of chance.
        [nameof(MissionWorld.Ambience)] = "follows where the listener stands",
        [nameof(MissionWorld.SmokePlumes)] = "level emitters that follow their expressions",
        [nameof(MissionWorld.Computer)] = "the map screen, closed by the respawn",
        [nameof(MissionWorld.MissileStream)] = "a stream of chance",
        [nameof(MissionState.ShotRandom)] = "a stream of chance",
        [nameof(MissionState.SoundRandom)] = "a stream of chance",
    };

    [Fact]
    public void Every_part_of_the_played_mission_is_reset_by_a_round_or_named_as_needing_nothing()
    {
        string[] unlisted = [.. Parts().Where(p => !Reset.Contains(p.Name) && !Exempt.ContainsKey(p.Name)).Select(p => p.Name).Order()];
        Assert.True(unlisted.Length == 0, $"Not reset by a round, nor named as needing nothing: {string.Join(", ", unlisted)}.");

        string[] gone = [.. Reset.Concat(Exempt.Keys).Where(name => Parts().All(p => p.Name != name)).Order()];
        Assert.True(gone.Length == 0, $"Named here but no longer held: {string.Join(", ", gone)}.");
    }

    [Fact]
    public void Each_part_a_round_resets_takes_the_reset()
    {
        foreach (PropertyInfo part in Parts().Where(p => Reset.Contains(p.Name)))
        {
            Assert.True(typeof(ILevelReset).IsAssignableFrom(part.PropertyType), $"{part.Name} ({part.PropertyType.Name}) does not take ILevelReset.");
        }
    }

    // Every object the mission holds: values (flags, counters, positions) and callbacks are the parts' own business.
    private static IEnumerable<PropertyInfo> Parts() =>
        typeof(MissionWorld).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => !p.PropertyType.IsValueType
                && p.PropertyType != typeof(string)
                && !typeof(Delegate).IsAssignableFrom(p.PropertyType)
                && !IsCallbackList(p.PropertyType));

    private static bool IsCallbackList(Type type) => type.IsGenericType && type.GetGenericArguments().Any(static a => typeof(Delegate).IsAssignableFrom(a));
}
