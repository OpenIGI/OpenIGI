using OpenIGI.Core.Numerics;

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
public readonly record struct DeferredLighting(Vector3f Ambient, float Nightfall);
