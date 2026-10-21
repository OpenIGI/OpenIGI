using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>The surface cache's card atlas: opening it, drawing cards into its pages, and the textures it leaves.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ICardCaptureDevice
{
    /// <summary>Opens the surface cache atlas for capture.</summary>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <returns><see langword="true"/> when the atlas is ready and cards may be drawn into it.</returns>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="IScenePassDevice.BeginSceneCapture"/>, which describes a frame. This describes an atlas
    /// that outlives every frame: each model is drawn into it a handful of times at load, through an
    /// orthographic camera per card, and read for the rest of the level.
    /// </para>
    /// <para>
    /// Clears the whole atlas, including the gutters between pages, which no card ever draws into and a
    /// linear fetch near a page edge reaches.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A capture is already open, or a dimension is not positive.</exception>
    bool BeginCardCapture(int width, int height);

    /// <summary>Restricts the following draws to one card's page of the atlas.</summary>
    /// <param name="x">The page's left edge in texels.</param>
    /// <param name="y">The page's bottom edge in texels.</param>
    /// <param name="width">The page's width in texels.</param>
    /// <param name="height">The page's height in texels.</param>
    /// <remarks>
    /// Scissors as well as sets the viewport. Geometry outside the card's orthographic box is clipped in
    /// depth but not sideways, so without the scissor a model wider than its card bleeds into whichever
    /// page was packed beside it.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">No card capture is open.</exception>
    void SetCardPage(int x, int y, int width, int height);

    /// <summary>Closes the card capture.</summary>
    /// <exception cref="GraphicsDeviceException">No card capture is open.</exception>
    void EndCardCapture();

    /// <summary>Reads one of the card atlas's targets back into memory.</summary>
    /// <param name="attachment">Which target: 0 albedo, 1 normal, 2 material, 3 depth.</param>
    /// <param name="destination">Receives the texels, row-major and top row first.</param>
    /// <remarks>
    /// <b>For looking at the atlas, and that is not a luxury.</b> A capture is the kind of thing only an
    /// eye can judge: wrong winding shows as models captured inside-out, a wrong card rotation as
    /// mirrored pages, a missing scissor as pages bleeding into each other — and all three produce a
    /// perfectly plausible atlas full of perfectly plausible surfaces.
    /// <para>
    /// Synchronising, so it belongs at load or behind a developer switch rather than in a frame.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">There is no atlas, or the span is too small.</exception>
    void ReadCardAtlas(int attachment, Span<Bgra32> destination);

    /// <summary>Gets the atlas texture holding each card's surface colour.</summary>
    TextureHandle CardAlbedo { get; }

    /// <summary>Gets the atlas texture holding each card's model-space normal.</summary>
    TextureHandle CardNormal { get; }

    /// <summary>Gets the atlas texture holding each card's roughness, metallic and emissive.</summary>
    TextureHandle CardMaterial { get; }

    /// <summary>Gets the atlas texture holding how far behind its card each surface sits.</summary>
    /// <remarks>
    /// A fraction of the card's own depth. Without it a card records only where on its plane a surface
    /// lies, and the plane is the model's bounding box face — nowhere near the surface for anything but
    /// a flat panel.
    /// </remarks>
    TextureHandle CardDepth { get; }
}
