using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>How a level's authored sun is bound to the day/night cycle.</summary>
/// <remarks>
/// The cycle assumes a level authors its sun at noon. A title whose levels do not -- I.G.I. 2, whose night
/// missions paint the sun nearly black and leave it UP -- has to say so, or the cycle sweeps a scene that
/// is already dark down into nothing and the clock does not work on it at all.
/// </remarks>
public sealed class DayNightBindingTests
{
    // location1/level1's own sun: a third of the way up the sky, painted very nearly black, at the
    // constant noon every campaign level of the sequel writes.
    private static readonly DirectionalLightSettings Authored = new(
        Direction: new Vector3f(-0.8414711f, 0f, 0.5403020f),
        Colour: new Vector3f(0.096154f, 0.153846f, 0.25f),
        Ambient: Vector3f.Zero,
        Beta: 0f,
        Gamma: 0f,
        TimeOfDay: 12f,
        Intensity: 1f,
        AffectsTerrain: true,
        AffectsObjects: true);

    /// <summary>A title that says nothing gets the sun it wrote, untouched.</summary>
    /// <remarks>
    /// Project I.G.I., and the whole of its deferred regression picture, rests on this being an identity.
    /// </remarks>
    [Fact]
    public void A_title_that_says_nothing_is_not_touched()
    {
        Assert.Equal(Authored, DayNightBinding.Bind(Authored, null));
    }

    /// <summary>A daytime level keeps everything but its fill, which the title knows better.</summary>
    /// <remarks>
    /// Nineteen of the sequel's twenty-four leave the <c>Dirlight</c> back colour black and put the real
    /// ambient on <c>GlobalLight</c>. Bound without this the cycle would cross to black at noon.
    /// </remarks>
    [Fact]
    public void A_daytime_level_keeps_its_sun_and_takes_the_titles_fill()
    {
        Vector3f fill = new(0.28f, 0.29f, 0.30f);
        DirectionalLightSettings bound = DayNightBinding.Bind(Authored, new DeferredLighting(fill, 0f));

        Assert.Equal(Authored.Direction, bound.Direction);
        Assert.Equal(Authored.Colour, bound.Colour);
        Assert.Equal(Authored.TimeOfDay, bound.TimeOfDay);
        Assert.Equal(fill, bound.Ambient);
    }

    /// <summary>A night level is turned round, so the cycle's moon hands its direction back.</summary>
    /// <remarks>
    /// The arc fits its peak as <c>referenceElevation / sin(referencePhase)</c> and clamps it positive. At
    /// a night hour that quotient is negative for an above-horizon sun and the clamp rejects it -- which is
    /// why a night level cannot simply be given a night hour. Negating the direction cancels both signs,
    /// the fit lands on the authored elevation, and the moon path flips it back.
    /// </remarks>
    [Fact]
    public void A_night_level_is_handed_over_anti_solar_at_its_reference_hour()
    {
        Vector3f day = new(0.92f, 0.79f, 0.58f);
        Vector3f dayFill = new(0.28f, 0.29f, 0.29f);
        DeferredLighting lit = new(
            new Vector3f(0.057692f, 0.1f, 0.18f), 0.85f, new AuthoredNight(24f, day, dayFill));

        DirectionalLightSettings bound = DayNightBinding.Bind(Authored, lit);

        Assert.Equal(-Authored.Direction, bound.Direction);
        Assert.Equal(24f, bound.TimeOfDay);

        // And it sweeps UP to a daytime it never authored, rather than down from one.
        Assert.Equal(day, bound.Colour);
        Assert.Equal(dayFill, bound.Ambient);
    }

    /// <summary>The turned-round sun is the one the arc can actually be fitted through.</summary>
    /// <remarks>
    /// The property the whole handover rests on, checked against the fit's own arithmetic rather than
    /// against the cycle: the peak elevation a night reference hour implies must come out POSITIVE, which
    /// is what the clamp demands and what the authored direction fails.
    /// </remarks>
    [Fact]
    public void The_arc_can_be_fitted_through_the_turned_round_sun_and_not_the_authored_one()
    {
        const float referenceHour = 24f;
        float referenceSine = MathF.Sin(((referenceHour - 6f) / 12f) * MathF.PI);

        float asAuthored = MathF.Asin(Authored.Direction.Z) / referenceSine;
        Assert.True(asAuthored < 0f, "the authored sun already fits at a night hour, so the turn-round is not needed.");

        DirectionalLightSettings bound = DayNightBinding.Bind(
            Authored,
            new DeferredLighting(Vector3f.Zero, 0.85f, new AuthoredNight(referenceHour, Vector3f.One, Vector3f.One)));

        float asBound = MathF.Asin(bound.Direction.Z) / referenceSine;
        Assert.True(asBound > 0f, "the turned-round sun still does not fit, so the moon will never get it back.");
        Assert.Equal(MathF.Asin(Authored.Direction.Z), asBound, 5);
    }
}
