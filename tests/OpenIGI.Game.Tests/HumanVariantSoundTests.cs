using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The variant draw of <c>0x4638A0</c>, and the off-by-one that makes one draw in every set name a
/// definition the game never shipped.
/// </summary>
public sealed class HumanVariantSoundTests
{
    [Fact]
    public void TheFaithfulDrawIsZeroBasedAndSoNamesOneThingThatDoesNotExist()
    {
        // 0x4B47C0 is four instructions ending in an unsigned `div`, so the result is [0, count-1] --
        // while the definitions are numbered from one. Reproducing that is what -vanilla asks for.
        HashSet<string> drawn = Draw(HumanVariantSound.AiDeathPrefix, 4, faithful: true);

        Assert.Equal(
            ["ai_death_0", "ai_death_1", "ai_death_2", "ai_death_3"],
            drawn.OrderBy(name => name, StringComparer.Ordinal));

        // ai_death_0 has no definition, so a quarter of soldier deaths are silent; and the fourth
        // variant the game ships can never be drawn at all.
        Assert.DoesNotContain("ai_death_4", drawn);
    }

    [Fact]
    public void TheCorrectedDrawIsTheOneTheSiblingHelperAlreadyMakes()
    {
        // 0x4637C0 formats `n + 1` from the same draw. This is that, and nothing more.
        HashSet<string> drawn = Draw(HumanVariantSound.AiDeathPrefix, 4, faithful: false);

        Assert.Equal(
            ["ai_death_1", "ai_death_2", "ai_death_3", "ai_death_4"],
            drawn.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ThePlayerSetIsShiftedTheSameWay()
    {
        Assert.Equal(
            ["player_death_0", "player_death_1", "player_death_2"],
            Draw(HumanVariantSound.PlayerDeathPrefix, 3, faithful: true)
                .OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            ["player_death_1", "player_death_2", "player_death_3"],
            Draw(HumanVariantSound.PlayerDeathPrefix, 3, faithful: false)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ACountOfZeroIsRefusedRatherThanDividedBy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HumanVariantSound.Choose("x_", 0, new IgiRandom(), faithful: true));
    }

    private static HashSet<string> Draw(string prefix, int count, bool faithful)
    {
        IgiRandom random = new();
        HashSet<string> drawn = new(StringComparer.Ordinal);

        // Enough draws that every variant is reached; the generator is the engine's own, so this is
        // deterministic rather than flaky.
        for (int i = 0; i < 2000; i++)
        {
            drawn.Add(HumanVariantSound.Choose(prefix, count, random, faithful));
        }

        return drawn;
    }
}
