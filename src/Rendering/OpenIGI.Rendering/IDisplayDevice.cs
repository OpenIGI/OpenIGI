using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>The display half of the renderer seam: the mode, the frame's clear and present, and the frame read back or copied.</summary>
/// <remarks>The members mirror the original's <c>Display*</c> calls -- <see cref="SetMode"/> is <c>DisplaySetMode</c>, <see cref="SwapBuffers"/> is <c>DisplaySwapBuffers</c>, <see cref="SetViewport"/> is <c>ClipWindowPrimitive</c> and <see cref="BlitPixels"/> is <c>BlitPrimitive</c>. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IDisplayDevice
{
    /// <summary>Gets what this device supports. Mirrors <c>GetGFXCaps</c>.</summary>
    GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets the mode the device is currently set to.</summary>
    DisplayMode CurrentMode { get; }

    /// <summary>Gets or sets a value indicating whether presentation waits for the vertical blank. Mirrors <c>DisplayVSync</c>.</summary>
    bool VSyncEnabled { get; set; }

    /// <summary>Sets the back-buffer size and colour depth. Mirrors <c>DisplaySetMode</c>.</summary>
    /// <param name="mode">The mode to apply.</param>
    /// <exception cref="GraphicsDeviceException">The mode could not be applied.</exception>
    void SetMode(in DisplayMode mode);

    /// <summary>Clears the colour and depth buffers. Mirrors <c>DisplayClear</c>.</summary>
    /// <param name="color">The colour to fill with. The alpha channel is ignored, as it is in the original.</param>
    /// <remarks>The original always clears colour and depth together and always clears depth to <c>1.0</c>.</remarks>
    void Clear(Bgra32 color);

    /// <summary>Clears only the depth buffer to its far value.</summary>
    /// <remarks>
    /// This is for a foreground 3D layer which must retain its own depth ordering without comparing
    /// against an already-rendered scene. It does not change the colour buffer or the active depth state.
    /// </remarks>
    void ClearDepth();

    /// <summary>Presents the back buffer and opens the next scene. Mirrors <c>DisplaySwapBuffers</c>.</summary>
    /// <remarks>See the frame-order note on <see cref="IGraphicsDevice"/>: this presents first, then begins.</remarks>
    void SwapBuffers();

    /// <summary>Restricts rendering to a sub-rectangle of the render target. Mirrors <c>ClipWindowPrimitive</c>.</summary>
    /// <param name="viewport">The region and depth range.</param>
    void SetViewport(in Viewport viewport);

    /// <summary>Copies a CPU pixel buffer straight to the render target. Mirrors <c>BlitPrimitive</c>.</summary>
    /// <param name="x">The destination left edge in pixels.</param>
    /// <param name="y">The destination top edge in pixels.</param>
    /// <param name="width">The source width in pixels.</param>
    /// <param name="height">The source height in pixels.</param>
    /// <param name="pixels">The source pixels, row-major, <paramref name="width"/> per row.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    /// <remarks>Used for splash screens and movie frames, where the source is produced on the CPU.</remarks>
    void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Reads the render target back into a pixel buffer. Mirrors <c>SnapshotPrimitive</c>.</summary>
    /// <param name="width">The width to read in pixels.</param>
    /// <param name="height">The height to read in pixels.</param>
    /// <param name="destination">Receives the pixels, row-major and top row first.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    /// <remarks>
    /// Rows arrive top-first regardless of what the backend's own framebuffer orientation is, so a
    /// capture can be written straight out as an image.
    /// </remarks>
    void CaptureFrame(int width, int height, Span<Bgra32> destination);

    /// <summary>Copies the render target into a texture, without going through system memory.</summary>
    /// <param name="texture">The destination texture; it must be at least <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <param name="width">The width to copy in pixels.</param>
    /// <param name="height">The height to copy in pixels.</param>
    /// <remarks>
    /// <see cref="CaptureFrame"/>'s counterpart for the case where the frame is wanted <em>as a texture</em>
    /// rather than as an image: it stays on the GPU, so it costs a copy instead of a read back plus an
    /// upload. What it is for is a cross-dissolve — holding the last frame of the outgoing picture so it can
    /// be drawn over the incoming one and faded off, which is the only way to blend two of something the
    /// renderer only keeps one of.
    /// </remarks>
    void CopyFrameToTexture(TextureHandle texture, int width, int height);

    /// <summary>Draws the loading backdrop over the whole frame: a held picture defocused and graded, or a procedural surface.</summary>
    /// <param name="texture">The held picture, as captured by <see cref="CopyFrameToTexture"/>; ignored when the settings say there is none.</param>
    /// <param name="settings">How far out of focus, how graded, how faded, and the rest.</param>
    /// <remarks>
    /// A fullscreen pass rather than sprites, because a defocus and a grade are per-pixel work no number
    /// of tinted quads can imitate. Blended over the frame, so the reveal can fade it off the live level.
    /// Drawn outside any sprite batch.
    /// </remarks>
    void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings);

    /// <summary>Blurs the frame drawn so far into a few levels of increasing blur, for the interface's frosted glass.</summary>
    /// <returns>The levels, valid until the next call or the end of the frame; empty on a device that cannot.</returns>
    /// <remarks>
    /// <para>
    /// Called once a frame, after the scene and before the interface: the menu shell draws its backdrop and
    /// every glass panel by sampling these in screen space. Like <see cref="CopyFrameToTexture"/> it closes
    /// the pass in flight and the next draw reopens it on top, so nothing drawn is lost.
    /// </para>
    /// <para>
    /// The levels hold linear light, the form an sRGB image's sampler hands the 2D pass, so they draw
    /// through the ordinary sprite path with no special shading.
    /// </para>
    /// </remarks>
    BackdropBlur BlurFrameForBackdrop() => BackdropBlur.None;
}
