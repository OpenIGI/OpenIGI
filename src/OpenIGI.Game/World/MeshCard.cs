using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Which of the six axis directions a mesh card faces.</summary>
/// <remarks>
/// <para>
/// A card faces outwards, the way a surface normal does, and the orthographic capture that fills it
/// therefore looks the <em>other</em> way: a <see cref="PositiveZ"/> card sits above the model and its
/// camera looks down.
/// </para>
/// <para>
/// <b>The numbering is load-bearing and must not be tidied into something more readable.</b> Opposite
/// directions differ in the low bit alone, so <c>(int)axis ^ 1</c> is the direction facing the other
/// way and <c>(int)axis / 2</c> is the axis it lies on. <see cref="MeshCardBaker"/> uses both, and so
/// will the shader-side table that indexes the six cards of a model by direction.
/// </para>
/// </remarks>
public enum MeshCardAxis
{
    /// <summary>Faces +X. The capture looks along −X.</summary>
    PositiveX = 0,

    /// <summary>Faces −X. The capture looks along +X.</summary>
    NegativeX = 1,

    /// <summary>Faces +Y. The capture looks along −Y.</summary>
    PositiveY = 2,

    /// <summary>Faces −Y. The capture looks along +Y.</summary>
    NegativeY = 3,

    /// <summary>Faces +Z. The capture looks along −Z.</summary>
    PositiveZ = 4,

    /// <summary>Faces −Z. The capture looks along +Z.</summary>
    NegativeZ = 5,
}

/// <summary>
/// One oriented rectangle a model is approximated by: an orthographic view of it along one axis, and
/// the page of the surface cache that view is captured into.
/// </summary>
/// <param name="Axis">Which of the six directions this card faces.</param>
/// <param name="Centre">
/// The centre of the rectangle, in the model's own space. It lies on the face of the model's bounding
/// box that faces <paramref name="Axis"/>, so it is where the capture's near plane sits.
/// </param>
/// <param name="HalfWidth">Half the rectangle's extent along <see cref="Right"/>.</param>
/// <param name="HalfHeight">Half the rectangle's extent along <see cref="Up"/>.</param>
/// <param name="Depth">
/// How far the model extends behind the card. The capture's orthographic range runs from
/// <paramref name="Centre"/> to <see cref="FarCentre"/>, so this is its far plane.
/// </param>
/// <param name="ResolutionU">How many texels the card gets along <see cref="Right"/>.</param>
/// <param name="ResolutionV">How many texels the card gets along <see cref="Up"/>.</param>
/// <param name="AreaShare">
/// The fraction of the model's surface that faces this card's axis, in the range 0 to 1. The shares of
/// a model's six directions sum to one.
/// </param>
/// <remarks>
/// <para>
/// The surface cache answers a question a triangle mesh cannot answer cheaply: <em>a ray landed here —
/// what does this surface look like and how is it lit?</em> Answering it by finding the triangle and
/// its material means a mesh traversal per ray. Answering it from a card means one projection and one
/// texture fetch, because the model's albedo, normal and depth have already been rendered flat through
/// these six views and left in an atlas.
/// </para>
/// <para>
/// Six axis-aligned views rather than a fitted set of arbitrarily oriented ones. Fitting would cover a
/// slanted roof with fewer texels, and would cost the lookup its cheapness — an arbitrary card has to
/// be found before it can be sampled, whereas which of six axes a hit normal most faces is three
/// comparisons. This game's architecture is boxes, huts, crates and gantries, which the axis-aligned
/// six fit almost exactly.
/// </para>
/// <para>
/// <b>A default-constructed <see cref="MeshCard"/> is the zero instance</b> — a degenerate card facing
/// +X with no extent and no texels — not a sensible default. They come from
/// <see cref="MeshCardBaker"/>.
/// </para>
/// </remarks>
public readonly record struct MeshCard(
    MeshCardAxis Axis,
    Vector3f Centre,
    float HalfWidth,
    float HalfHeight,
    float Depth,
    int ResolutionU,
    int ResolutionV,
    float AreaShare)
{
    /// <summary>Gets the unit vector this card faces.</summary>
    public Vector3f Direction => DirectionOf(Axis);

    /// <summary>Gets the in-plane axis the card's texel columns run along.</summary>
    public Vector3f Right => RightOf(Axis);

    /// <summary>Gets the in-plane axis the card's texel rows run along.</summary>
    public Vector3f Up => UpOf(Axis);

    /// <summary>Gets the rectangle's full extent along <see cref="Right"/>.</summary>
    public float Width => HalfWidth * 2f;

    /// <summary>Gets the rectangle's full extent along <see cref="Up"/>.</summary>
    public float Height => HalfHeight * 2f;

    /// <summary>Gets how many texels of the atlas this card occupies.</summary>
    public int TexelCount => ResolutionU * ResolutionV;

    /// <summary>Gets the centre of the capture's far plane, <see cref="Depth"/> behind the card.</summary>
    public Vector3f FarCentre => Centre - (Direction * Depth);

    /// <summary>Gets how much world space one texel of this card covers along <see cref="Right"/>.</summary>
    /// <remarks>
    /// The figure that decides whether a card is worth its atlas space. Two cards of the same texel
    /// count describe wildly different amounts of detail if one is a door and the other a hangar wall.
    /// </remarks>
    public float TexelSizeU => ResolutionU > 0 ? Width / ResolutionU : 0f;

    /// <summary>Gets how much world space one texel of this card covers along <see cref="Up"/>.</summary>
    public float TexelSizeV => ResolutionV > 0 ? Height / ResolutionV : 0f;

    /// <summary>Returns the unit vector an axis faces.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The unit vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="axis"/> is not one of the six.</exception>
    public static Vector3f DirectionOf(MeshCardAxis axis) => axis switch
    {
        MeshCardAxis.PositiveX => new Vector3f(1f, 0f, 0f),
        MeshCardAxis.NegativeX => new Vector3f(-1f, 0f, 0f),
        MeshCardAxis.PositiveY => new Vector3f(0f, 1f, 0f),
        MeshCardAxis.NegativeY => new Vector3f(0f, -1f, 0f),
        MeshCardAxis.PositiveZ => new Vector3f(0f, 0f, 1f),
        MeshCardAxis.NegativeZ => new Vector3f(0f, 0f, -1f),
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Not one of the six card axes."),
    };

    /// <summary>Returns the in-plane axis an axis's texel columns run along.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The unit vector.</returns>
    /// <remarks>
    /// Paired with <see cref="UpOf"/> so that <c>Cross(Right, Up) == Direction</c> for all six. That
    /// makes each card's basis right-handed with the card's own face towards the viewer, which is what
    /// a capture rendered through it needs if the atlas page is not to come out mirrored — and a
    /// mirrored page is a fault that survives inspection, because most of this game's surfaces are
    /// tiling textures that look the same either way round.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="axis"/> is not one of the six.</exception>
    public static Vector3f RightOf(MeshCardAxis axis) => axis switch
    {
        MeshCardAxis.PositiveX => new Vector3f(0f, 1f, 0f),
        MeshCardAxis.NegativeX => new Vector3f(0f, -1f, 0f),
        MeshCardAxis.PositiveY => new Vector3f(0f, 0f, 1f),
        MeshCardAxis.NegativeY => new Vector3f(0f, 0f, -1f),
        MeshCardAxis.PositiveZ => new Vector3f(1f, 0f, 0f),
        MeshCardAxis.NegativeZ => new Vector3f(-1f, 0f, 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Not one of the six card axes."),
    };

    /// <summary>Returns the in-plane axis an axis's texel rows run along.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The unit vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="axis"/> is not one of the six.</exception>
    public static Vector3f UpOf(MeshCardAxis axis) => axis switch
    {
        MeshCardAxis.PositiveX or MeshCardAxis.NegativeX => new Vector3f(0f, 0f, 1f),
        MeshCardAxis.PositiveY or MeshCardAxis.NegativeY => new Vector3f(1f, 0f, 0f),
        MeshCardAxis.PositiveZ or MeshCardAxis.NegativeZ => new Vector3f(0f, 1f, 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Not one of the six card axes."),
    };

    /// <summary>Returns the direction facing the other way.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>Its opposite.</returns>
    /// <remarks>
    /// The pairing is the low bit of the enum's numbering rather than a switch, which is the reason
    /// that numbering is fixed.
    /// </remarks>
    public static MeshCardAxis Opposite(MeshCardAxis axis) => (MeshCardAxis)((int)axis ^ 1);

    /// <summary>Returns the card's direction, size and resolution.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() =>
        $"{Axis} {Width:0.##}x{Height:0.##}x{Depth:0.##} @ {ResolutionU}x{ResolutionV} ({AreaShare:P0})";
}
