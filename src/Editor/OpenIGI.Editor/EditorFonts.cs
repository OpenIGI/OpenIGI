using System.Globalization;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;

namespace OpenIGI.Editor;

/// <summary>
/// The editor's faces, those of the scene editors it is modelled on: Inter for the interface and Inter SemiBold for
/// emphasis (a face drawn for screens at small sizes, where the game's condensed Barlow is cramped), JetBrains Mono for
/// code, Material Icons for icons — each rasterised per size on demand. Barlow stands in when Inter is missing.
/// </summary>
internal sealed class EditorFonts : IEditorFonts, IDisposable
{
    private const string InterfaceFile = "Inter-Regular.ttf";
    private const string StrongFile = "Inter-SemiBold.ttf";
    private const string FallbackStrongFile = "Barlow-Medium.ttf";
    private const string IconFile = "MaterialIcons-Regular.ttf";
    private const string IconNames = "MaterialIcons-Regular.codepoints";

    private readonly SizedFontSet _interface;
    private readonly SizedFontSet _strong;
    private readonly SizedFontSet _mono;
    private readonly SizedFontSet? _icons;
    private readonly Dictionary<string, int> _iconCodepoints = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="EditorFonts"/> class.</summary>
    /// <param name="device">The device the glyph atlases live on.</param>
    public EditorFonts(IGraphicsDevice device)
    {
        _interface = new SizedFontSet(device, UiFontLibrary.Exists(InterfaceFile) ? InterfaceFile : IgiTypography.BodyFile);
        _strong = new SizedFontSet(
            device,
            UiFontLibrary.Exists(StrongFile) ? StrongFile : UiFontLibrary.Exists(FallbackStrongFile) ? FallbackStrongFile : IgiTypography.BodyFile);
        _mono = new SizedFontSet(device, UiFontLibrary.Mono);

        // The icon face and the list naming its glyphs, shipped together; without them icons fall back to drawn shapes.
        string names = Path.Combine(UiFontLibrary.FontsDirectory, IconNames);
        if (UiFontLibrary.Exists(IconFile) && File.Exists(names))
        {
            foreach (string line in File.ReadLines(names))
            {
                int space = line.IndexOf(' ');
                if (space > 0
                    && int.TryParse(line.AsSpan(space + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codepoint)
                    && codepoint <= char.MaxValue)
                {
                    _iconCodepoints.TryAdd(line[..space], codepoint);
                }
            }

            // Only the icons the editor names, so an atlas is a few dozen glyphs at any size.
            HashSet<string> used = [.. EditorIcons.All];
            foreach (string unused in _iconCodepoints.Keys.Where(name => !used.Contains(name)).ToList())
            {
                _iconCodepoints.Remove(unused);
            }

            _icons = new SizedFontSet(device, IconFile, [.. _iconCodepoints.Values.Distinct().Order()]);
        }
    }

    /// <inheritdoc/>
    public UiFont Font(EditorFontFace face, float sizePx) => face switch
    {
        EditorFontFace.InterfaceStrong => _strong.At(sizePx),
        EditorFontFace.Mono => _mono.At(sizePx),
        _ => _interface.At(sizePx),
    };

    /// <inheritdoc/>
    public UiFont? IconFont(float sizePx) => _icons?.At(sizePx);

    /// <inheritdoc/>
    public int IconCodepoint(string name) => _iconCodepoints.TryGetValue(name, out int codepoint) ? codepoint : -1;

    /// <inheritdoc/>
    public void Dispose()
    {
        _interface.Dispose();
        _strong.Dispose();
        _mono.Dispose();
        _icons?.Dispose();
    }
}
