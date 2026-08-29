using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// One light a level appears to have, proposed by reading where its emissive geometry stands.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a proposal, not a declaration.</b> I.G.I. declares no point lights at all, so nothing in
/// the level data corresponds to this type — it is the conclusion of two inferences stacked on each
/// other. <see cref="EmissiveTextureSurvey"/> infers which <em>textures</em> the artists treated as
/// sources, from how bright they painted the lightmaps around them; <see cref="LightFixtureExtractor"/>
/// then infers a <em>light</em> from every place such a texture is drawn. Both steps produce a list for
/// someone to look at rather than an answer to be trusted, and a fixture that turns out to be a
/// reflective sign rather than a lamp is a normal outcome, not a bug.
/// </para>
/// <para>
/// <b>Everything but <see cref="Position"/> is soft.</b> The centroid of a lamp's triangles is a fact
/// about the geometry and is as exact as the transform that produced it. <see cref="Radius"/> and
/// <see cref="Intensity"/> are not facts about anything: they are what a plausible light of this size
/// and colour would do, from a formula with a tunable constant in it. They exist so the list can be
/// loaded and looked at, and they are expected to be edited afterwards.
/// </para>
/// <para>
/// A default-constructed <see cref="LightFixture"/> is the zero instance — no texture, no area, at the
/// world origin — and never something the extractor produces.
/// </para>
/// </remarks>
/// <param name="Texture">
/// The emitter texture that contributed most of this fixture's area, as the pack names it. A cluster
/// can draw with more than one emitter texture, so this identifies the dominant one rather than the
/// only one.
/// </param>
/// <param name="Position">The centroid of the cluster's triangles, in world space and in metres.</param>
/// <param name="Radius">
/// How far the light is proposed to reach, in metres. Derived from <see cref="Extent"/> and the
/// colour's luminance — see <see cref="LightFixtureExtractor.DefaultRadiusScale"/> for the derivation
/// and for how arbitrary the constant in it is.
/// </param>
/// <param name="Colour">
/// The light's colour, linear and normalised so the brightest channel is around one. Supplied to the
/// extractor per texture; white when the caller had nothing better.
/// </param>
/// <param name="Area">
/// The total surface area of the cluster's triangles, in square metres. The emitting area, which is
/// what makes a strip light stronger than a bulb.
/// </param>
/// <param name="Extent">
/// The radius in metres of the smallest sphere about <see cref="Position"/> containing every vertex of
/// the cluster — the fixture's own size, as distinct from the size of the region it lights.
/// </param>
/// <param name="TriangleCount">
/// How many emitter triangles were merged into this fixture. Carried for review: a fixture built from
/// one stray triangle deserves more suspicion than one built from a whole lamp housing.
/// </param>
/// <param name="Direction">
/// The way the fixture faces, as a unit vector in world space, or zero when it does not face anywhere
/// in particular.
/// <para>
/// A lamp is not a bare bulb hanging in a void: it is a housing with an opening, and it throws its
/// light out of that opening and nowhere else. A ceiling tube lights the floor under it and leaves the
/// slab it is bolted to dark. Treating every fixture as an omnidirectional point is what puts a bright
/// disc on the ceiling above every lamp in the level — light appearing on the one surface that is
/// physically behind the emitter — and it is visible from anywhere in the room.
/// </para>
/// <para>
/// So this is the area-weighted average of the fixture's own face normals, flipped downward: it is
/// where the geometry looks, measured from the geometry. Zero is honest and not a failure — a lamp
/// modelled as a closed box has normals that cancel, and a fixture that cannot say which way it faces
/// is better lit as a point than as a spot pointing at a guess.
/// </para>
/// </param>
/// <param name="Tangent">
/// The fixture's long axis, perpendicular to <paramref name="Direction"/>, or zero when it does not
/// face anywhere and so has no plane to lie in.
/// <para>
/// This and the two half-sizes are what make the fixture a RECTANGLE rather than a point with a cone
/// bolted on. A fluorescent batten is a metre and a half long and a hand wide, and almost nothing about
/// how it lights a room follows from its centre: the falloff across a wall beside it, the shape of the
/// shadow it throws, and how the light wraps past the terminator are all properties of its extent. A
/// point source gets every one of them wrong, and no amount of tuning a cone recovers them.
/// </para>
/// </param>
/// <param name="HalfWidth">Half the fixture's size across <paramref name="Tangent"/>, in metres.</param>
/// <param name="HalfHeight">Half the fixture's size along <paramref name="Tangent"/>, in metres.</param>
/// <param name="Radiance">
/// How brightly the fixture's own surface burns — the SAME number that makes its geometry glow.
/// <para>
/// A lamp's surface radiance is one fact about it, and it answers both questions: how bright the
/// fitting looks, and how much light it puts into the room. Carrying two unrelated numbers for those is
/// why they disagreed, and it is why a ceiling of blazing battens lit nothing underneath them.
/// </para>
/// <para>
/// It replaces <see cref="Intensity"/> for lighting, and that is a units change as much as a tidying.
/// Intensity is area times luminance — a POWER, which was right against a falloff of one over distance
/// squared and is wrong against a real solid angle: the shape's own size is already in the integral, so
/// multiplying by the area again counts it twice. Radiance is what an area light wants, and a bigger
/// fitting still gives more light, through subtending more sky.
/// </para>
/// </param>
public readonly record struct LightFixture(
    string Texture,
    Vector3d Position,
    float Radius,
    Vector3f Colour,
    float Area,
    float Extent,
    int TriangleCount,
    Vector3f Direction = default,
    Vector3f Tangent = default,
    float HalfWidth = 0f,
    float HalfHeight = 0f,
    float Radiance = 0f)
{
    /// <summary>Gets a value indicating whether this fixture has a rectangle to be lit as.</summary>
    /// <remarks>
    /// It needs a plane and two sizes. A cluster whose faces cancel — a lamp modelled as a closed box —
    /// has neither, and is lit as a sphere of its own extent instead: the same integral with the
    /// rectangle collapsed to a point, which is what a source with no orientation actually is.
    /// </remarks>
    public bool IsRectangular =>
        IsDirectional
        && (Tangent.X * Tangent.X) + (Tangent.Y * Tangent.Y) + (Tangent.Z * Tangent.Z) > 0.25f
        && HalfWidth > 0.001f
        && HalfHeight > 0.001f;

    /// <summary>Gets a value indicating whether this fixture throws its light one way.</summary>
    /// <remarks>
    /// A direction of no length is the extractor saying it could not tell, which is a normal outcome;
    /// the renderer reads that as an omnidirectional source rather than as a spot aimed at nothing.
    /// </remarks>
    public bool IsDirectional =>
        (Direction.X * Direction.X) + (Direction.Y * Direction.Y) + (Direction.Z * Direction.Z) > 0.25f;

    /// <summary>Gets the colour's luminance, on the same scale as its brightest channel.</summary>
    /// <remarks>
    /// Rec. 709 weights, the same ones <see cref="EmissiveTextureSurvey"/> measures lightmap pages
    /// with, so that a colour called bright here and a lightmap called bright there mean the same
    /// thing.
    /// </remarks>
    public float Luminance =>
        (0.2126f * Colour.X) + (0.7152f * Colour.Y) + (0.0722f * Colour.Z);

    /// <summary>Gets how much light the fixture is proposed to emit, in relative units.</summary>
    /// <remarks>
    /// <para>
    /// The emitting area weighted by the colour's luminance, which is the power a Lambertian emitter of
    /// unit radiance would put out. It is deliberately relative: the retail data records no radiance
    /// anywhere, so there is no absolute value to recover, and the only honest thing to report is the
    /// ratio between one proposed fixture and another. A renderer scales the whole set by one number it
    /// picks by eye.
    /// </para>
    /// <para>
    /// Derived rather than stored, so that it cannot drift out of step with the area and colour it is
    /// computed from.
    /// </para>
    /// </remarks>
    public float Intensity => Area * Luminance;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(string.IsNullOrEmpty(Texture) ? "<none>" : Texture)} at {Position} "
        + $"r={Radius:0.##}m from {TriangleCount} triangle(s)";
}
