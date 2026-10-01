using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Reduces a model to the handful of oriented rectangles the surface cache captures it through.
/// </summary>
/// <remarks>
/// <para>
/// Every model gets up to six cards, one per axis direction, and each is an orthographic view of the
/// whole model along that axis. A later stage renders albedo, normal and depth through each view into
/// an atlas page; the lighting then runs over those pages rather than over the level's triangles, and a
/// ray that lands anywhere on the model reads its answer out of whichever page faces the way it hit.
/// </para>
/// <para>
/// <b>The card's rectangle is the model's bounding box projected onto the axis's plane, not a tight fit
/// around the triangles that face that way.</b> The tight fit is the obvious improvement and it is
/// wrong: what the capture renders is the <em>whole model</em> seen from that direction, including
/// every triangle that faces slightly away and every one behind them. Cropping the rectangle to the
/// contributing triangles crops the render, and the surface that falls outside it has no entry in the
/// cache at all — which reads, at trace time, as a hole in the geometry rather than as a slightly worse
/// approximation. A later reader tempted to shrink these rectangles should read this paragraph twice.
/// </para>
/// <para>
/// <b>A triangle counts towards both directions of the axis it faces.</b> I.G.I.'s models are
/// single-sided art from 2000 — a floor panel is one quad, a wall has no back, and the inside of a hut
/// is the outside's other face. A ray hitting that floor from beneath is a perfectly ordinary event in
/// a bounced-light solution, and if only the direction the normal points had accumulated area then the
/// panel would have a card above it and nothing below, so the ray would find no surface to read. So the
/// area is counted twice, once each way, and a flat panel comes out with a card above and below and
/// none on its four edges — which is exactly the set that describes it.
/// </para>
/// <para>
/// <b>The normal is the triangle's own, from its winding, not the interpolated vertex normal.</b> Vertex
/// normals in this game's art are smoothed across corners, so the three vertices of a cube's corner
/// triangle point in three different diagonal directions and none of them points at the face the
/// triangle actually lies in. Smoothing is a shading convenience; which way a surface faces is geometry.
/// </para>
/// </remarks>
public static class MeshCardBaker
{
    /// <summary>The fewest texels a card is given on either in-plane axis.</summary>
    /// <remarks>
    /// Below this the capture stops describing the surface and starts describing its average colour.
    /// Eight is cheap enough that giving it to a card that barely deserves one costs nothing.
    /// </remarks>
    public const int MinimumResolution = 8;

    /// <summary>The most texels a card is given on either in-plane axis.</summary>
    /// <remarks>
    /// The cap exists because a single card's cost is quadratic in it and because this game's largest
    /// models are hangar walls whose texture is a tiling sheet — a page four times the size resolves
    /// nothing further, it just repeats the same detail at four times the bandwidth.
    /// </remarks>
    public const int MaximumResolution = 128;

    /// <summary>How many texels one world unit of a card's surface is given by default.</summary>
    /// <remarks>
    /// Calibrated against what these models are, the same way the distance field's resolution was: a
    /// crate is about a metre across and a hut about eight, so this gives the crate the floor of eight
    /// texels and the hut sixty-four. The surface cache is a source of <em>indirect</em> light, which
    /// is low-frequency by nature — bounced light needs to know that a wall is red, not where its
    /// rivets are.
    /// </remarks>
    public const float DefaultTexelsPerUnit = 8f;

    /// <summary>The share of a model's surface a direction needs before it is worth a card.</summary>
    /// <remarks>
    /// Two percent. A model's six shares sum to one, so a direction that describes nothing at all sits
    /// at zero and even a cube's least-represented face sits at a sixth; the threshold only has to
    /// separate "a genuine face of this model" from "the four one-quad edges of a floor panel". Set it
    /// much higher and the top edge of a wall — a real surface that real light lands on — stops being
    /// captured.
    /// </remarks>
    public const float DefaultMinimumAreaShare = 0.02f;

    /// <summary>The shallowest orthographic range a card is given, in world units.</summary>
    /// <remarks>
    /// A single quad has no thickness along the axis it faces, and an orthographic volume whose near
    /// and far planes coincide has no depth range to rasterise into — the capture would come out empty
    /// or, worse, undefined. A centimetre is far below anything this game models and comfortably
    /// contains a flat panel.
    /// </remarks>
    private const float MinimumDepth = 0.01f;

    /// <summary>How many axis directions a model can be described by.</summary>
    private const int DirectionCount = 6;

    /// <summary>Bakes the cards describing a model.</summary>
    /// <param name="geometry">The model's drawable geometry.</param>
    /// <param name="texelsPerUnit">How many texels one world unit of card surface is given.</param>
    /// <param name="minimumAreaShare">The share of the surface a direction needs before it earns a card.</param>
    /// <returns>
    /// The cards, in axis order. Empty when the model has no triangles; otherwise between one and six.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="texelsPerUnit"/> is not positive.</exception>
    public static IReadOnlyList<MeshCard> Bake(
        MeshGeometry geometry,
        float texelsPerUnit = DefaultTexelsPerUnit,
        float minimumAreaShare = DefaultMinimumAreaShare)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return Bake(geometry.Vertices, geometry.Indices, texelsPerUnit, minimumAreaShare);
    }

    /// <summary>Bakes the cards describing a model.</summary>
    /// <param name="vertices">The model's vertices.</param>
    /// <param name="indices">Its triangle indices, three per triangle.</param>
    /// <param name="texelsPerUnit">How many texels one world unit of card surface is given.</param>
    /// <param name="minimumAreaShare">The share of the surface a direction needs before it earns a card.</param>
    /// <returns>
    /// The cards, in axis order. Empty when the model has no triangles; otherwise between one and six.
    /// </returns>
    /// <remarks>
    /// A model with no triangles gets no cards, because there is no surface to describe and an empty
    /// atlas page is worse than none — it reads as a black surface rather than as an absent one. A model
    /// whose triangles are all degenerate does get one, because something occupies that space and the
    /// cache having no entry for it is how geometry goes missing from a bounce.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="texelsPerUnit"/> is not positive.</exception>
    public static IReadOnlyList<MeshCard> Bake(
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices,
        float texelsPerUnit = DefaultTexelsPerUnit,
        float minimumAreaShare = DefaultMinimumAreaShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(texelsPerUnit);

        int triangles = indices.Length / 3;

        if (triangles == 0 || vertices.Length == 0)
        {
            return [];
        }

        minimumAreaShare = Math.Clamp(minimumAreaShare, 0f, 1f);

        // Every vertex, not only the ones the triangles reference. The distance field baker bounds the
        // same model the same way, and the two structures describe the same object to the same tracer:
        // a card whose box disagreed with the field's would put the captured surface a little to one
        // side of where the trace reports the hit.
        Vector3f min = new(float.MaxValue);
        Vector3f max = new(float.MinValue);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3f p = vertices[i].Position;
            min = new Vector3f(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new Vector3f(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
        }

        // No margin around it, deliberately, and this is where it differs from the distance field. A
        // field needs empty space outside the model for a trace to decelerate in; a card is a picture
        // of the model's surface, and margin on it is atlas space spent on nothing.
        Vector3f size = new(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        Vector3f centre = new(
            (min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);

        Span<float> facing = stackalloc float[DirectionCount];
        float total = 0f;

        for (int t = 0; t < triangles; t++)
        {
            Vector3f a = vertices[indices[t * 3]].Position;
            Vector3f b = vertices[indices[(t * 3) + 1]].Position;
            Vector3f c = vertices[indices[(t * 3) + 2]].Position;

            // The cross product carries both quantities at once: its length is twice the area and its
            // direction is the normal. Normalising first and measuring the area separately would be the
            // same arithmetic done twice, and would need a zero-length guard in two places instead of
            // one.
            Vector3f cross = Vector3f.Cross(b - a, c - a);
            float length = cross.Length;

            if (length <= 0f)
            {
                // Degenerate: a zero-area triangle has no direction to contribute to. This game's models
                // contain them -- collapsed quads left behind by the original's own tooling.
                continue;
            }

            float area = length * 0.5f;

            // Which of the six the normal most faces is the largest dot product with a unit axis, and
            // since the axes are the unit axes the dot products are just the components. Taking the
            // largest magnitude finds the axis; its sign finds which of the axis's two directions.
            float ax = MathF.Abs(cross.X);
            float ay = MathF.Abs(cross.Y);
            float az = MathF.Abs(cross.Z);

            int direction;

            if (ax >= ay && ax >= az)
            {
                direction = cross.X >= 0f ? (int)MeshCardAxis.PositiveX : (int)MeshCardAxis.NegativeX;
            }
            else if (ay >= az)
            {
                direction = cross.Y >= 0f ? (int)MeshCardAxis.PositiveY : (int)MeshCardAxis.NegativeY;
            }
            else
            {
                direction = cross.Z >= 0f ? (int)MeshCardAxis.PositiveZ : (int)MeshCardAxis.NegativeZ;
            }

            // And to the direction facing the other way, for the reason recorded on the class: these
            // surfaces are single-sided, so the back of this triangle is a surface light lands on too.
            // The `^ 1` is the enum's numbering, which pairs opposites in the low bit.
            facing[direction] += area;
            facing[direction ^ 1] += area;
            total += area;
        }

        List<MeshCard> cards = [];

        // Counted twice, so normalising by twice the area is what makes the six shares sum to one.
        float divisor = total * 2f;

        if (divisor > 0f)
        {
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                float share = facing[direction] / divisor;

                if (share >= minimumAreaShare)
                {
                    cards.Add(Card((MeshCardAxis)direction, centre, size, share, texelsPerUnit));
                }
            }
        }

        if (cards.Count == 0)
        {
            // Nothing measurable faces anywhere -- every triangle was degenerate. Something still stands
            // here, so it gets one card rather than none, and the direction chosen is the one the box is
            // thinnest along: that is the largest rectangle the model admits and the shallowest capture,
            // which for the collapsed sliver this case actually describes is the right view of it.
            MeshCardAxis fallback = size.X <= size.Y
                ? (size.X <= size.Z ? MeshCardAxis.PositiveX : MeshCardAxis.PositiveZ)
                : (size.Y <= size.Z ? MeshCardAxis.PositiveY : MeshCardAxis.PositiveZ);

            // A share of one because it is the whole of what this model has, however little that is.
            // Reporting zero would invite the caller that drops cards describing nothing to drop the
            // only card the model has, which is the outcome the fallback exists to prevent.
            cards.Add(Card(fallback, centre, size, 1f, texelsPerUnit));
        }

        return cards;
    }

    /// <summary>Builds one card from the model's bounding box.</summary>
    /// <param name="axis">The direction it faces.</param>
    /// <param name="centre">The centre of the model's bounding box.</param>
    /// <param name="size">The box's full extent on each axis.</param>
    /// <param name="share">The share of the surface facing this way.</param>
    /// <param name="texelsPerUnit">How many texels one world unit of card surface is given.</param>
    /// <returns>The card.</returns>
    private static MeshCard Card(
        MeshCardAxis axis, Vector3f centre, Vector3f size, float share, float texelsPerUnit)
    {
        Vector3f direction = MeshCard.DirectionOf(axis);
        Vector3f right = MeshCard.RightOf(axis);
        Vector3f up = MeshCard.UpOf(axis);

        // The basis vectors are signed unit axes, so an absolute dot with the box's extent selects the
        // one component that matters without a switch over the six.
        float width = AbsDot(right, size);
        float height = AbsDot(up, size);
        float depth = AbsDot(direction, size);

        // Slid from the centre of the box out to the face that looks this way, which is where the
        // capture's near plane belongs: everything the card describes is then behind it, at a depth
        // between zero and the box's extent along the axis.
        Vector3f face = centre + (direction * (depth * 0.5f));

        return new MeshCard(
            axis,
            face,
            width * 0.5f,
            height * 0.5f,
            MathF.Max(depth, MinimumDepth),
            Resolution(width, texelsPerUnit),
            Resolution(height, texelsPerUnit),
            share);
    }

    /// <summary>How many texels a card's in-plane extent is worth.</summary>
    /// <param name="worldSize">The extent, in world units.</param>
    /// <param name="texelsPerUnit">How many texels one world unit is given.</param>
    /// <returns>The texel count, within the permitted bounds.</returns>
    /// <remarks>
    /// Proportional, then clamped, and specifically <em>not</em> rounded up to a power of two. Cards
    /// are rarely square — a door is twice as tall as it is wide — and rounding both axes up would cost
    /// up to four times the atlas space to buy an allocator a convenience it does not need, since the
    /// pages are packed once at load and never resized.
    /// </remarks>
    private static int Resolution(float worldSize, float texelsPerUnit)
    {
        int texels = (int)MathF.Ceiling(worldSize * texelsPerUnit);

        return Math.Clamp(texels, MinimumResolution, MaximumResolution);
    }

    /// <summary>The dot product of a signed unit axis with an extent, without its sign.</summary>
    /// <param name="axis">The signed unit axis.</param>
    /// <param name="extent">The extent.</param>
    /// <returns>The extent's component along that axis.</returns>
    private static float AbsDot(Vector3f axis, Vector3f extent) =>
        MathF.Abs((axis.X * extent.X) + (axis.Y * extent.Y) + (axis.Z * extent.Z));
}
