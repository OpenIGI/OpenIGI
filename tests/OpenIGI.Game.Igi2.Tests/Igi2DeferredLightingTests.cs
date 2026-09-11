using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>What an I.G.I. 2 level tells the dynamic renderer about its own light.</summary>
/// <remarks>
/// <para>
/// The sequel does not author a sun. It authors a lighting DIRECTION and puts the darkness in the
/// colour, which is the opposite of what a model that reads darkness off elevation assumes. These pin
/// the measurement that settled it, because the whole seam rests on it: if the corpus ever stopped
/// splitting cleanly into dark suns and bright ones, the band would be a guess rather than a reading.
/// </para>
/// </remarks>
public sealed class Igi2DeferredLightingTests
{
    /// <summary>The sequel's darkness is in its sun's colour, and not in its height.</summary>
    /// <remarks>
    /// Two levels make the case on their own. The first mission is at night with its sun a third of the
    /// way UP the sky; <c>location1/LEVEL6</c> is bright with its sun pointing almost straight DOWN. A
    /// model that reads one off the other gets both exactly backwards.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_darkness_is_in_the_suns_colour_and_not_in_its_height()
    {
        int dark = 0;
        int bright = 0;
        int belowHorizonAndBright = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Assert.True(DirectionalLightSettings.TryRead(level.Root, out DirectionalLightSettings sun));
            float luminance = Luminance(sun.Colour);

            // The gap the band brackets: nothing in the corpus sits between these two.
            Assert.False(
                luminance is > Igi2PresentationProvider.DayLuminance and < 0.55f,
                $"'{level.Directory}' authors a sun at {luminance:F2}, inside the gap the night band assumes.");

            if (luminance < Igi2PresentationProvider.DayLuminance)
            {
                dark++;
            }
            else
            {
                bright++;
                belowHorizonAndBright += sun.Direction.Z < -0.1f ? 1 : 0;
            }
        }

        Assert.Equal(5, dark);
        Assert.Equal(19, bright);

        // Four bright levels whose sun is below the horizon. The sweep fades a sun out as it sets, so
        // every one of these would have gone dark.
        Assert.Equal(4, belowHorizonAndBright);
    }

    /// <summary>The night mission reads as night and the bright ones read as day.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_night_mission_reads_as_night_and_the_bright_ones_as_day()
    {
        Igi2Level night = Level("location1/level1");
        DeferredLighting lit = Read(night);

        Assert.InRange(lit.Nightfall, 0.75f, 1f);

        // And its fill is the cold blue the level wrote, not a warm daylight one.
        Assert.True(lit.Ambient.Z > lit.Ambient.X, "the night level's fill is authored cold, and is not.");

        foreach (string bright in new[] { "location1/LEVEL6", "location2/LEVEL4", "location3/level1" })
        {
            DeferredLighting day = Read(Level(bright));
            Assert.Equal(0f, day.Nightfall, 3);
        }
    }

    /// <summary>The fill is the level's own, and not the sun's back colour.</summary>
    /// <remarks>
    /// Nineteen of the twenty-four leave the <c>Dirlight</c>'s back colour at black, so a dynamic path
    /// taking its ambient from there filled with nothing at all. The <c>GlobalLight</c> object ambient
    /// is what the fixed-function path already shines on an unlightmapped object.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_fill_is_the_levels_own_and_not_the_suns_back_colour()
    {
        int blackBackColour = 0;
        int litFill = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Assert.True(DirectionalLightSettings.TryRead(level.Root, out DirectionalLightSettings sun));
            blackBackColour += Luminance(sun.Ambient) <= 0.001f ? 1 : 0;

            DeferredLighting lit = Read(level);
            litFill += Luminance(lit.Ambient) > 0.001f ? 1 : 0;
        }

        Assert.Equal(19, blackBackColour);
        Assert.Equal(24, litFill);
    }

    /// <summary>Project I.G.I. answers nothing, so its sky model is untouched.</summary>
    /// <remarks>
    /// The seam's default, asserted rather than assumed, because everything that keeps the first game's
    /// picture stable rests on it: the sweep, the moon and the elevation-derived exposure all stay.
    /// </remarks>
    [Fact]
    public void A_title_that_authors_no_light_of_its_own_answers_nothing()
    {
        ITitlePresentationProvider silent = new SilentProvider();
        Assert.Null(silent.ReadDeferredLighting(new LevelTask()));
    }

    private static float Luminance(Vector3f colour) =>
        (colour.X * 0.2126f) + (colour.Y * 0.7152f) + (colour.Z * 0.0722f);

    private static DeferredLighting Read(Igi2Level level) =>
        Assert.IsType<DeferredLighting>(Igi2PresentationProvider.Instance.ReadDeferredLighting(level.Root));

    private static Igi2Level Level(string suffix) => Igi2Level.All().First(
        l => l.Directory.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private sealed class SilentProvider : ITitlePresentationProvider
    {
        public IWorldPresenter? CreateWorld(in WorldPresentationRequest request) => null;
    }
}
