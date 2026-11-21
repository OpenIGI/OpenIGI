namespace OpenIGI.Presentation;

/// <summary>
/// Where a title's own font comes from: the status display's, which the remastered computer and the session's overlays set their
/// text in so they read as the original's.
/// </summary>
public interface ITitleFontSource : IDisposable
{
    /// <summary>Gets the title's font, loaded on its first use; <see langword="null"/> where the installation has none.</summary>
    Rendering.BitmapTextRenderer? Font { get; }
}
