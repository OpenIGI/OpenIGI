namespace OpenIGI.Rendering;

/// <summary>
/// Where the level's roofs are, as one texel per square of ground: how to turn a world position into a
/// texel of the shelter map, and a packed texel back into the height of the surface standing over it.
/// </summary>
/// <remarks>
/// <para>
/// Standing water has to know whether the sky can reach a surface — a warehouse floor does not pool
/// with water while its own roof keeps the rain off it — and that question is asked per pixel, in the
/// lighting shader, which knows nothing about the level. It used to be answered by handing the shader a
/// handful of building bounding boxes and looping over them. That was wrong twice over: a building
/// standing at an angle to the world axes fills barely a third of its own box, so the box dried the
/// apron in front of it, and any structure the level had not labelled a building sheltered nothing at
/// all, so its floor rained indoors.
/// </para>
/// <para>
/// So the answer arrives as a picture instead: a top-down raster of the height of the solid world,
/// built once when the mission loads. A pixel converts its world XY to a texel, fetches the height
/// stored there, and is sheltered when that height stands over it — one fetch and a comparison, in
/// place of a loop, and right everywhere rather than right near a labelled box.
/// </para>
/// <para>
/// <b>World metres, not camera-relative.</b> Everything else the wetness carries is rebased on the
/// camera because the levels sit tens of kilometres from the world origin. This is not, because the map
/// is a fixed property of the level and rebasing it would mean re-uploading a transform every frame for
/// no gain: a world coordinate of tens of kilometres still resolves to a millimetre in a float, which is
/// four hundred times finer than one texel.
/// </para>
/// </remarks>
/// <param name="OriginX">The world X, in metres, of the low corner of texel zero.</param>
/// <param name="OriginY">The world Y, in metres, of the low corner of texel zero.</param>
/// <param name="CellMetres">How much ground one texel covers, in metres.</param>
/// <param name="Width">How many texels the map holds along world X.</param>
/// <param name="Height">How many texels the map holds along world Y.</param>
/// <param name="LowestZ">The world height, in metres, the packed value counts up from.</param>
/// <param name="HeightScale">How many metres one step of the packed value stands for.</param>
/// <param name="HeadroomMetres">
/// How far over a surface the roof must stand before the surface counts as sheltered. The ground is in
/// the map like everything else, so without this every square of ground would shelter itself.
/// </param>
/// <param name="ReachMetres">
/// How far over a surface a roof may be and still be its roof. A level's footprint can hold two areas
/// at wildly different altitudes, and one top-down raster cannot tell a ceiling from a mountainside a
/// kilometre above it.
/// </param>
public readonly record struct ShelterMapTransform(
    float OriginX,
    float OriginY,
    float CellMetres,
    int Width,
    int Height,
    float LowestZ,
    float HeightScale,
    float HeadroomMetres,
    float ReachMetres)
{
    /// <summary>A level with no map, under which nothing is ever sheltered.</summary>
    public static ShelterMapTransform None { get; }

    /// <summary>Gets whether a map was submitted at all.</summary>
    public bool IsPresent => Width > 0 && Height > 0 && CellMetres > 0f;
}
