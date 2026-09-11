using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation;

/// <summary>
/// What a title whose levels author their own light hands the dynamic renderer, in place of what it
/// would otherwise derive from where the sun is in the sky.
/// </summary>
/// <param name="Ambient">
/// The flat fill the level authors, which is the sequel's <c>GlobalLight</c> object ambient rather
/// than the <c>Dirlight</c>'s back colour. Nineteen of the sequel's twenty-four levels leave that back
/// colour at black, so the ambient the dynamic path used was nothing at all.
/// </param>
/// <param name="Nightfall">
/// How dark the level is meant to read, nought for full day and one for full night. Drives the
/// exposure, and nothing else.
/// </param>
/// <remarks>
/// <para>
/// <b>Why a title needs this at all.</b> Project I.G.I. authors a sun as a position in the sky, and
/// the dynamic path treats it as one: it decomposes the authored direction into an elevation and a
/// bearing, sweeps it around an arc for the time of day, fades its colour out as it sets, and reads
/// the picture's darkness off how far below the horizon it has gone. All of that is sound for a game
/// whose levels are outdoors at a stated hour.
/// </para>
/// <para>
/// <b>I.G.I. 2 does not author a sun.</b> It authors a lighting DIRECTION and puts the darkness in the
/// colour. Measured over all twenty-four of its levels: the first mission's sun stands a third of the
/// way up the sky and is very nearly black (0.10, 0.15, 0.25) because the mission is at night, while
/// <c>location1/LEVEL6</c>'s points almost straight DOWN and is very nearly white. Fed to a model that
/// reads darkness off elevation, those two come out exactly backwards — the night mission is lit as
/// noon, and four bright levels are put out entirely because their sun is "below the horizon".
/// </para>
/// <para>
/// So a title that answers this is saying: my sun is where I put it, my ambient is what I wrote, and
/// how dark the scene is does not follow from either. A title that answers nothing keeps the sky model
/// exactly as it was, which is what Project I.G.I. does and what its regression captures depend on.
/// </para>
/// <para>
/// <b>What this deliberately does NOT carry is a direction.</b> Five of the sequel's levels author a
/// sun below the horizon, four of them bright — <c>location1/LEVEL6</c>'s points almost straight down
/// at nearly full white. That costs retail nothing, because retail bakes: its directional light reaches
/// only the objects that have no lightmap, so where it points barely shows. Tried both ways against a
/// hillside in <c>location1/LEVEL7</c>: used as written it lights the ground from underneath and comes
/// out flat, and mirrored above the horizon it is near vertical and comes out flat as well. The sweep's
/// clamped arc happens to rake nicely, which is not a reason to believe it — but neither is an
/// aesthetic preference a reason to change it, so the direction is left exactly as it was and this
/// carries only the two things the level measurably does say.
/// </para>
/// </remarks>
/// <param name="Night">
/// Where the level sits on the day/night cycle, for one it authors as a NIGHT. Null for a level whose
/// authored lighting is already a daytime one, which is every level Project I.G.I. ships and nineteen of
/// the sequel's twenty-four.
/// </param>
/// <param name="CompleteBake">
/// <para>
/// Whether the level's shipped bakes are a WHOLE lighting solution, direct light included.
/// </para>
/// <para>
/// The hybrid model adds a live sun on top of the bakes, because its premise is that a bake is an
/// AMBIENT term -- the bounce and the fill, with the key light left to be computed. That is a statement
/// about the bakes, and it is not true of every game's.
/// </para>
/// <para>
/// I.G.I. 2's are whole: the ground's third pass is a full lighting solution and a lightmapped object
/// carries one too, which is why the fixed-function sun reaches only the objects that have NO bake.
/// Adding a sun over them counts the key light twice, and on its night missions it does so at a scale
/// that swamps the bake -- the directional there is the level's own night sun, some thirty times the moon
/// the cycle invents for a level without one. Measured on mission 1, hybrid came out at 3.5 times
/// vanilla's ground with the night exposure on and 6.9 with it off, against a baked model at 0.18.
/// </para>
/// <para>
/// A title that says its bakes are whole has the hybrid model's added sun withheld, so hybrid reproduces
/// the bake rather than doubling it. Project I.G.I. says nothing and keeps the sun it always had.
/// </para>
/// </param>
public readonly record struct DeferredLighting(
    Vector3f Ambient,
    float Nightfall,
    AuthoredNight? Night = null,
    bool CompleteBake = false);

/// <summary>Where a level whose authored lighting IS a night sits on the day/night cycle.</summary>
/// <param name="ReferenceHour">The hour at which the cycle reproduces the level's authored lighting.</param>
/// <param name="DaylightColour">The sun to sweep UP to, which a night level does not author.</param>
/// <param name="DaylightAmbient">The fill to sweep up to, likewise.</param>
/// <remarks>
/// <para>
/// <b>The cycle assumes a level authors its sun at noon</b> and sweeps down from there: it fits an arc
/// through the authored elevation at the authored hour, fades the sun out as that arc sets, hangs a moon
/// on the anti-solar point and crosses the fill to a night blue. Handed a level that authors a NIGHT, all
/// of that is applied to a scene that is already dark -- the sun is faded from nearly black to black, and
/// an authored night fill is crossed towards another one.
/// </para>
/// <para>
/// I.G.I. 2's night levels are exactly that, and they are worse than that: they leave the sun a third of
/// the way UP the sky and paint it (0.10, 0.15, 0.25), so nothing about their elevation says night and the
/// arc cannot be made to say it either -- an arc through an above-horizon elevation at a night hour has no
/// solution, and the fit's own clamp rejects it.
/// </para>
/// <para>
/// So a level answering this is bound to the cycle by its NIGHT end instead of its day one. Its authored
/// sun becomes the cycle's moon and its authored fill the cycle's night fill, reached at
/// <paramref name="ReferenceHour"/> -- where the picture is then exactly what its artists made. The day
/// end is what it never authored and has to be supplied: sweeping to noon lights it with
/// <paramref name="DaylightColour"/> and <paramref name="DaylightAmbient"/>.
/// </para>
/// </remarks>
public readonly record struct AuthoredNight(
    float ReferenceHour,
    Vector3f DaylightColour,
    Vector3f DaylightAmbient);

/// <summary>Turns a level's authored sun into the one the day/night cycle can sweep.</summary>
/// <remarks>
/// Static and pure, and here rather than in the host, because it is the whole of what a title's answer
/// MEANS to the cycle -- and because it is the piece worth testing: everything downstream of it is the
/// cycle doing what it has always done.
/// </remarks>
public static class DayNightBinding
{
    /// <summary>Binds the level's sun to the cycle.</summary>
    /// <param name="authored">The sun exactly as the level wrote it.</param>
    /// <param name="lighting">What the title says about the level, or null for a title that says nothing.</param>
    /// <returns>The sun to hand the cycle.</returns>
    /// <remarks>
    /// <para>
    /// The cycle's contract is that a level authors its sun at NOON and it sweeps down from there. Project
    /// I.G.I. honours that and comes back unchanged. I.G.I. 2 does not, in two ways, and both have to be
    /// answered here or the cycle is meaningless on it.
    /// </para>
    /// <para>
    /// First, its FILL. The sequel leaves the <c>Dirlight</c>'s back colour black in nineteen of its
    /// twenty-four levels and puts the real ambient on <c>GlobalLight</c> instead, so the cycle would
    /// cross to black at noon. The title's answer replaces it, and the cycle then has a real daytime fill
    /// to reach.
    /// </para>
    /// <para>
    /// Second, its NIGHT levels. They paint the sun nearly black and leave it a third of the way UP the
    /// sky, so the arc cannot be fitted to say night -- and must not be asked to, because what they author
    /// IS the night. Such a level is turned round: the cycle is handed the ANTI-solar direction at
    /// midnight, where its own moon flips it back and returns the authored direction exactly, with the
    /// authored colour and fill supplied separately as the night to cross to. What it sweeps UP to is the
    /// daytime the level never wrote, which the title supplies from the levels that did.
    /// </para>
    /// <para>
    /// <b>Why the turn-round works.</b> The arc fits its peak elevation as
    /// <c>referenceElevation / sin(referencePhase)</c> and clamps it positive. Handed the authored
    /// elevation at a night hour that quotient is negative and the clamp rejects it, which is the
    /// dead end. Handed the NEGATED elevation at the same hour both signs cancel, the fit lands on the
    /// authored elevation, and at that hour the arc reproduces it pointing the other way -- which is
    /// exactly what the moon path then flips back.
    /// </para>
    /// </remarks>
    public static DirectionalLightSettings Bind(DirectionalLightSettings authored, DeferredLighting? lighting)
    {
        if (lighting is not { } lit)
        {
            return authored;
        }

        if (lit.Night is not { } night)
        {
            return authored with { Ambient = lit.Ambient };
        }

        return authored with
        {
            Direction = -authored.Direction,
            TimeOfDay = night.ReferenceHour,
            Colour = night.DaylightColour,
            Ambient = night.DaylightAmbient,
        };
    }
}
