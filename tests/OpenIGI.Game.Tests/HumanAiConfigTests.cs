using OpenIGI.Game.Ai;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The combat-configuration table's own rules — the shape, the defaults and the conversions, none of
/// which need the retail data.
/// </summary>
public sealed class HumanAiConfigTests
{
    /// <summary>An unloaded registry is already complete, as the original's is.</summary>
    /// <remarks>
    /// <c>sub_406DF0</c> fills every empty slot with a default item before anything can read one, so
    /// there is no such thing as an unconfigured <c>(type, difficulty)</c> pair.
    /// </remarks>
    [Fact]
    public void TheTableIsCompleteBeforeAnythingIsLoaded()
    {
        HumanAiConfigRegistry config = new();

        Assert.Equal(0, config.DeclaredRowCount);
        Assert.Equal(87, config.DefaultedRowCount);

        foreach (AiType type in Enum.GetValues<AiType>())
        {
            foreach (AiDifficulty difficulty in Enum.GetValues<AiDifficulty>())
            {
                Assert.False(config.IsDeclared(type, difficulty));
                Assert.NotNull(config.Item(type, difficulty));
                Assert.NotNull(config.Parameters(type, difficulty));
            }
        }
    }

    /// <summary>A defaulted row names its own slot.</summary>
    [Fact]
    public void ADefaultedRowNamesItsOwnSlot()
    {
        HumanAiConfigRegistry config = new();
        HumanAiConfigItemTask row = config.Item(AiType.SpetnazGuardSpas, AiDifficulty.Hard);

        Assert.Equal("AITYPE_SPETNAZ_GUARD_SPAS", row.AiTypeName);
        Assert.Equal("GD_3", row.DifficultyName);
        Assert.Equal(AiType.SpetnazGuardSpas, row.ResolvedType);
        Assert.Equal(AiDifficulty.Hard, row.ResolvedDifficulty);
    }

    /// <summary>The defaults are the constructor's, field for field.</summary>
    /// <remarks>Transcribed from <c>sub_406B70</c>'s thirty-two stores.</remarks>
    [Fact]
    public void TheDefaultsAreTheConstructorsOwn()
    {
        HumanAiConfigItemTask row = new();

        Assert.Equal(1.0f, row.DamageScale, 5);
        Assert.Equal(0.33f, row.RecoilThreshold, 5);
        Assert.Equal(0.33f, row.InitialMovementDelaySeconds, 5);
        Assert.Equal(1.0f, row.MovementDelaySeconds, 5);
        Assert.Equal(10.0f, row.TargetingTimeoutSeconds, 5);
        Assert.Equal(15.0f, row.TrackingDistanceMetres, 5);
        Assert.Equal(15.0f, row.LieDownMinimumDistanceMetres, 5);
        Assert.Equal(0.3f, row.EvasiveActionProbability, 5);
        Assert.Equal(0.5f, row.EvasiveStandRollOrLieDownProbability, 5);
        Assert.Equal(0.5f, row.EvasiveKneelRollOrLieDownProbability, 5);
        Assert.Equal(0.5f, row.EvasiveStandRollSideProbability, 5);
        Assert.Equal(0.5f, row.EvasiveKneelRollSideProbability, 5);
        Assert.Equal(1.0f, row.HitProbabilityAtMinimumRange, 5);
        Assert.Equal(0.25f, row.HitProbabilityAtMaximumRange, 5);
        Assert.Equal(30, row.MaximumRoundsPerSequence);
        Assert.Equal(0.2f, row.AimPrecision, 5);
        Assert.Equal(0.1f, row.MinimumClipFraction, 5);
        Assert.Equal(0.2f, row.RandomClipFraction, 5);
        Assert.Equal(1.5f, row.MinimumFiringDelaySeconds, 5);
        Assert.Equal(1.0f, row.RandomFiringDelaySeconds, 5);
        Assert.Equal(0.1f, row.MinimumShotDelaySeconds, 5);
        Assert.Equal(0.0f, row.RandomShotDelaySeconds, 5);
        Assert.Equal(0.33f, row.GrenadeThrowProbability, 5);
        Assert.Equal(0.1f, row.GrenadeThrowWhenReloadingProbability, 5);
        Assert.Equal(10.0f, row.GrenadeMinimumDistanceMetres, 5);
        Assert.Equal(60.0f, row.GrenadeMaximumDistanceMetres, 5);
        Assert.Equal(0.5f, row.GrenadeMinimumFlyTimeSeconds, 5);
        Assert.Equal(0.5f, row.GrenadeRandomFlyTimeSeconds, 5);
        Assert.Equal(0.3f, row.GrenadeMinimumExplodeTimeSeconds, 5);
        Assert.Equal(0.7f, row.GrenadeRandomExplodeTimeSeconds, 5);
        Assert.Equal(0.4f, row.CloseCombatHitProbability, 5);
        Assert.Equal(0.5f, row.CloseCombatDamage, 5);
    }

    /// <summary>Seconds become ticks by truncation, not by rounding.</summary>
    /// <remarks>
    /// The original stores the product with a truncating conversion, which is the same rule the rest of
    /// the port follows: 0.33 s is nine ticks and 0.25 s is seven.
    /// </remarks>
    [Theory]
    [InlineData(0.33f, 9)]
    [InlineData(0.25f, 7)]
    [InlineData(1.0f, 30)]
    [InlineData(0.0f, 0)]
    [InlineData(20.0f, 600)]
    public void SecondsBecomeTicksByTruncation(float seconds, int ticks) =>
        Assert.Equal(ticks, AiCombatParameters.ToTicks(seconds));

    /// <summary>The three difficulty keys round-trip through their <c>GD_n</c> names.</summary>
    [Fact]
    public void TheDifficultyNamesRoundTrip()
    {
        Assert.Equal("GD_1", HumanAiConfigRegistry.NameOf(AiDifficulty.Easy));
        Assert.Equal("GD_2", HumanAiConfigRegistry.NameOf(AiDifficulty.Normal));
        Assert.Equal("GD_3", HumanAiConfigRegistry.NameOf(AiDifficulty.Hard));

        Assert.Equal(AiDifficulty.Easy, HumanAiConfigRegistry.ParseDifficulty("GD_1"));
        Assert.Equal(AiDifficulty.Normal, HumanAiConfigRegistry.ParseDifficulty("GD_2"));
        Assert.Equal(AiDifficulty.Hard, HumanAiConfigRegistry.ParseDifficulty("GD_3"));
        Assert.Null(HumanAiConfigRegistry.ParseDifficulty("GD_4"));
        Assert.Null(HumanAiConfigRegistry.ParseDifficulty(null));
    }
}
