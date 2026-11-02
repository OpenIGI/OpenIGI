using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;

namespace OpenIGI.Editor;

/// <summary>
/// The editor's faces: Barlow for the interface (the shell's own body face, so the editor and the game read as
/// one product), Barlow Medium for emphasis, JetBrains Mono for code — each rasterised per size on demand.
/// </summary>
internal sealed class EditorFonts : IEditorFonts, IDisposable
{
    private const string StrongFile = "Barlow-Medium.ttf";

    private readonly SizedFontSet _interface;
    private readonly SizedFontSet _strong;
    private readonly SizedFontSet _mono;

    /// <summary>Initializes a new instance of the <see cref="EditorFonts"/> class.</summary>
    /// <param name="device">The device the glyph atlases live on.</param>
    public EditorFonts(IGraphicsDevice device)
    {
        _interface = new SizedFontSet(device, IgiTypography.BodyFile);
        _strong = new SizedFontSet(device, UiFontLibrary.Exists(StrongFile) ? StrongFile : IgiTypography.BodyFile);
        _mono = new SizedFontSet(device, UiFontLibrary.Mono);
    }

    /// <inheritdoc/>
    public UiFont Font(EditorFontFace face, float sizePx) => face switch
    {
        EditorFontFace.InterfaceStrong => _strong.At(sizePx),
        EditorFontFace.Mono => _mono.At(sizePx),
        _ => _interface.At(sizePx),
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        _interface.Dispose();
        _strong.Dispose();
        _mono.Dispose();
    }
}
