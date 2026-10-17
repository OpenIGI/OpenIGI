using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>The hit grunt of <c>0x4637C0</c> — the death helper's sibling, and the one that is correct.</summary>
public sealed class HumanHitSoundTests
{
    [Fact]
    public void TheHitDrawIsOneBasedAndSoNamesOnlyThingsThatExist()
    {
        // 0x4637C0 formats `n + 1` from the same unsigned draw 0x4638A0 formats bare. That is the
        // clearest evidence the death helper's zero-based name is a slip: the same programmer wrote
        // the +1 here.
        HashSet<string> drawn = Draw(HumanVariantSound.AiHitPrefix, HumanVariantSound.AiHitVariants);

        Assert.Equal(
            ["ai_hit_1", "ai_hit_2", "ai_hit_3"],
            drawn.OrderBy(name => name, StringComparer.Ordinal));

        Assert.DoesNotContain("ai_hit_0", drawn);
    }

    [Fact]
    public void ThePlayerSetIsFourAndAlsoOneBased()
    {
        Assert.Equal(
            ["player_hit_1", "player_hit_2", "player_hit_3", "player_hit_4"],
            Draw(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheCooldownIsOneToTwoSeconds()
    {
        // 30 - trunc(rand01() * -30.0), so 30..60 ticks. Without it a soldier caught in a burst would
        // grunt on every round that landed.
        IgiRandom random = new();
        for (int i = 0; i < 500; i++)
        {
            Assert.InRange(
                HumanVariantSound.HitCooldownTicks(random),
                HumanVariantSound.HitCooldownMinimumTicks,
                HumanVariantSound.HitCooldownMinimumTicks + HumanVariantSound.HitCooldownSpanTicks);
        }
    }

    [Fact]
    public void ACountOfZeroIsRefusedRatherThanDividedBy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HumanVariantSound.ChooseHit("x_", 0, new IgiRandom()));
    }

    private static HashSet<string> Draw(string prefix, int count)
    {
        IgiRandom random = new();
        HashSet<string> drawn = new(StringComparer.Ordinal);
        for (int i = 0; i < 2000; i++)
        {
            drawn.Add(HumanVariantSound.ChooseHit(prefix, count, random));
        }

        return drawn;
    }
}
