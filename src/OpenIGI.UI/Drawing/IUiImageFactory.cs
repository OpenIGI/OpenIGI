using OpenIGI.Core.Graphics;

namespace OpenIGI.UI.Drawing;

/// <summary>Creates and releases the images a renderer samples.</summary>
/// <remarks>
/// Separate from <see cref="IUiRenderer"/> because images outlive frames: a font atlas or the rounded
/// corner sprite is made once at start-up, long before any frame is drawn, and released when the
/// owning object is disposed.
/// </remarks>
public interface IUiImageFactory
{
    /// <summary>Creates an image from straight-alpha pixels, row by row from the top.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixels">The pixels; exactly <paramref name="width"/> × <paramref name="height"/> of them.</param>
    /// <returns>The new image.</returns>
    UiImage CreateImage(int width, int height, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Releases an image created by <see cref="CreateImage"/>.</summary>
    /// <param name="image">The image to release; <see cref="UiImage.None"/> is ignored.</param>
    void DestroyImage(UiImage image);
}
