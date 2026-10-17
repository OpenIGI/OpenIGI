using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail combat table — <c>common/ai/settings.qsc</c>, read the way <c>sub_406DF0</c> reads it.
/// </summary>
public sealed class RetailHumanAiConfigTests
{
    /// <summary>The shipped script fills all 87 slots — twenty-nine types times three difficulties.</summary>
    /// <remarks>
    /// The engine defaults exist for a script that leaves a hole; retail leaves none, so nothing the game
    /// runs on comes from the constructor.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryTypeAndDifficultyIsDeclared()
    {
        HumanAiConfigRegistry config = Load();

        Assert.Equal(87, config.DeclaredRowCount);
        Assert.Equal(0, config.DefaultedRowCount);

        foreach (AiType type in Enum.GetValues<AiType>())
        {
            foreach (AiDifficulty difficulty in Enum.GetValues<AiDifficulty>())
            {
                Assert.True(
                    config.IsDeclared(type, difficulty),
                    $"{AiScriptApi.TypeName(type)} / {HumanAiConfigRegistry.NameOf(difficulty)} was defaulted");
            }
        }
    }

    /// <summary>Every row names itself back, so the key really is <c>(AI Type, Difficulty)</c>.</summary>
    [RequiresGameDataFact]
    public void EveryRowNamesTheSlotItLandedIn()
    {
        HumanAiConfigRegistry config = Load();

        foreach (AiType type in Enum.GetValues<AiType>())
        {
            foreach (AiDifficulty difficulty in Enum.GetValues<AiDifficulty>())
            {
                HumanAiConfigItemTask row = config.Item(type, difficulty);
                Assert.Equal(type, row.ResolvedType);
                Assert.Equal(difficulty, row.ResolvedDifficulty);
            }
        }
    }

    /// <summary>Every value the script ships is inside the range its registrar declares.</summary>
    /// <remarks>
    /// The sixth argument of each <c>sub_4F1A80</c> call is a <c>{min, max}</c> pair, not a default —
    /// the four helpers give 0…1, 0…2, 0…100 and 0…10, and the one <c>Int32</c> gives 1…60. If the
    /// mapping of parameters to helpers were wrong, retail data would fall outside its own bounds.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryValueIsInsideItsDeclaredRange()
    {
        HumanAiConfigRegistry config = Load();

        foreach (AiType type in Enum.GetValues<AiType>())
        {
            foreach (AiDifficulty difficulty in Enum.GetValues<AiDifficulty>())
            {
                HumanAiConfigItemTask r = config.Item(type, difficulty);
                string key = $"{AiScriptApi.TypeName(type)}/{HumanAiConfigRegistry.NameOf(difficulty)}";

                InRange(key, "Damage scale", r.DamageScale, 0f, 10f);
                InRange(key, "Recoil threshold", r.RecoilThreshold, 0f, 10f);

                InRange(key, "initial movement delay", r.InitialMovementDelaySeconds, 0f, 2f);
                InRange(key, "movement delay", r.MovementDelaySeconds, 0f, 2f);
                InRange(key, "firing minimum delay", r.MinimumFiringDelaySeconds, 0f, 2f);
                InRange(key, "firing random delay", r.RandomFiringDelaySeconds, 0f, 2f);
                InRange(key, "minimum shot delay", r.MinimumShotDelaySeconds, 0f, 2f);
                InRange(key, "random shot delay", r.RandomShotDelaySeconds, 0f, 2f);

                InRange(key, "targeting timeout", r.TargetingTimeoutSeconds, 0f, 100f);
                InRange(key, "tracking distance", r.TrackingDistanceMetres, 0f, 100f);
                InRange(key, "liedown minimum distance", r.LieDownMinimumDistanceMetres, 0f, 100f);
                InRange(key, "grenade minimum distance", r.GrenadeMinimumDistanceMetres, 0f, 100f);
                InRange(key, "grenade maximum distance", r.GrenadeMaximumDistanceMetres, 0f, 100f);

                InRange(key, "evasive action", r.EvasiveActionProbability, 0f, 1f);
                InRange(key, "evasive stand roll", r.EvasiveStandRollOrLieDownProbability, 0f, 1f);
                InRange(key, "evasive kneel roll", r.EvasiveKneelRollOrLieDownProbability, 0f, 1f);
                InRange(key, "evasive stand side", r.EvasiveStandRollSideProbability, 0f, 1f);
                InRange(key, "evasive kneel side", r.EvasiveKneelRollSideProbability, 0f, 1f);
                InRange(key, "hit probability at minimum range", r.HitProbabilityAtMinimumRange, 0f, 1f);
                InRange(key, "hit probability at maximum range", r.HitProbabilityAtMaximumRange, 0f, 1f);
                InRange(key, "aim precision", r.AimPrecision, 0f, 1f);
                InRange(key, "minimum clip percentage", r.MinimumClipFraction, 0f, 1f);
                InRange(key, "random clip percentage", r.RandomClipFraction, 0f, 1f);
                InRange(key, "grenade throw", r.GrenadeThrowProbability, 0f, 1f);
                InRange(key, "grenade throw on reload", r.GrenadeThrowWhenReloadingProbability, 0f, 1f);
                InRange(key, "close combat hit probability", r.CloseCombatHitProbability, 0f, 1f);

                InRange(key, "grenade minimum fly time", r.GrenadeMinimumFlyTimeSeconds, 0f, 10f);
                InRange(key, "grenade random fly time", r.GrenadeRandomFlyTimeSeconds, 0f, 10f);
                InRange(key, "grenade minimum explode time", r.GrenadeMinimumExplodeTimeSeconds, 0f, 10f);
                InRange(key, "grenade random explode time", r.GrenadeRandomExplodeTimeSeconds, 0f, 10f);
                InRange(key, "close combat damage", r.CloseCombatDamage, 0f, 10f);

                Assert.InRange(r.MaximumRoundsPerSequence, 1, 60);
            }
        }
    }

    /// <summary>
    /// A guard gets tougher and far more accurate as the difficulty rises — the read that identifies
    /// "Damage scale" as maximum health.
    /// </summary>
    /// <remarks>
    /// It is a scale on nothing; it is the number the accumulated damage is compared against. Nothing
    /// else in the row moves the right way for the name.
    /// </remarks>
    [RequiresGameDataFact]
    public void DifficultyMakesAGuardTougherAndMoreAccurate()
    {
        HumanAiConfigRegistry config = Load();

        Assert.Equal(0.3f, config.Parameters(AiType.GuardAk, AiDifficulty.Easy).MaximumHealth, 5);
        Assert.Equal(0.5f, config.Parameters(AiType.GuardAk, AiDifficulty.Normal).MaximumHealth, 5);
        Assert.Equal(0.8f, config.Parameters(AiType.GuardAk, AiDifficulty.Hard).MaximumHealth, 5);

        Assert.Equal(0.1f, config.Parameters(AiType.GuardAk, AiDifficulty.Easy).HitProbabilityAtMaximumRange, 5);
        Assert.Equal(0.3f, config.Parameters(AiType.GuardAk, AiDifficulty.Normal).HitProbabilityAtMaximumRange, 5);
        Assert.Equal(0.5f, config.Parameters(AiType.GuardAk, AiDifficulty.Hard).HitProbabilityAtMaximumRange, 5);
    }

    /// <summary>
    /// The two characters the player has to keep alive get <i>weaker</i> as the difficulty rises, which
    /// is only sensible if "Damage scale" is health.
    /// </summary>
    [RequiresGameDataFact]
    public void TheEscortedCharactersGetFrailerOnHigherDifficulties()
    {
        HumanAiConfigRegistry config = Load();

        foreach (AiType type in new[] { AiType.Anya, AiType.Priboi })
        {
            float easy = config.Parameters(type, AiDifficulty.Easy).MaximumHealth;
            float normal = config.Parameters(type, AiDifficulty.Normal).MaximumHealth;
            float hard = config.Parameters(type, AiDifficulty.Hard).MaximumHealth;

            Assert.True(easy > normal, $"{type}: {easy} should exceed {normal}");
            Assert.True(normal > hard, $"{type}: {normal} should exceed {hard}");
        }
    }

    /// <summary>Six of the thirty-two numeric columns never vary across the whole table.</summary>
    /// <remarks>
    /// Worth pinning because it is the shape of the data: the grenade throw probabilities and distances
    /// were authored once and never tuned, so a behaviour that reads them behaves the same everywhere.
    /// </remarks>
    [RequiresGameDataFact]
    public void SixColumnsAreConstantAcrossEveryRow()
    {
        HumanAiConfigRegistry config = Load();

        foreach (AiType type in Enum.GetValues<AiType>())
        {
            foreach (AiDifficulty difficulty in Enum.GetValues<AiDifficulty>())
            {
                HumanAiConfigItemTask r = config.Item(type, difficulty);
                Assert.Equal(0.5f, r.EvasiveKneelRollSideProbability, 5);
                Assert.Equal(0f, r.RandomShotDelaySeconds, 5);
                Assert.Equal(0.33f, r.GrenadeThrowProbability, 5);
                Assert.Equal(0.1f, r.GrenadeThrowWhenReloadingProbability, 5);
                Assert.Equal(10f, r.GrenadeMinimumDistanceMetres, 5);
                Assert.Equal(60f, r.GrenadeMaximumDistanceMetres, 5);
            }
        }
    }

    /// <summary>The seconds-to-ticks and metres-to-units conversions land where the original puts them.</summary>
    [RequiresGameDataFact]
    public void TheConversionsAreAppliedOnTheWayIn()
    {
        HumanAiConfigRegistry config = Load();
        AiCombatParameters guard = config.Parameters(AiType.GuardAk, AiDifficulty.Normal);

        // 15 s targeting timeout -> 450 ticks.
        Assert.Equal(450, guard.TargetingTimeoutTicks);

        // 0.33 s initial movement delay -> 9 ticks, TRUNCATED: 0.33 * 30 = 9.9.
        Assert.Equal(9, guard.InitialMovementDelayTicks);
        Assert.Equal(30, guard.MovementDelayTicks);

        // 20 m tracking distance, 15 m lie-down distance, in world units.
        Assert.Equal(20.0 * 4096.0, guard.TrackingDistanceInUnits, 6);
        Assert.Equal(15.0 * 4096.0, guard.LieDownMinimumDistanceInUnits, 6);

        // 10 m and 60 m grenade distances, in world units.
        Assert.Equal(10f * 4096f, guard.GrenadeMinimumDistanceInUnits, 3);
        Assert.Equal(60f * 4096f, guard.GrenadeMaximumDistanceInUnits, 3);
    }

    private static void InRange(string key, string what, float value, float low, float high)
    {
        Assert.True(
            value >= low && value <= high,
            $"{key}: {what} is {value}, outside [{low}, {high}]");
    }

    private static HumanAiConfigRegistry Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        HumanAiConfigRegistry.RegisterTypes(loop.Types);

        HumanAiConfigRegistry config = new();
        config.Load(resources, loop.Natives, loop.Tasks);
        return config;
    }
}
