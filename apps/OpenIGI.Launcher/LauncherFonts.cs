using OpenIGI.Core.Configuration;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;

namespace OpenIGI.Launcher;

/// <summary>Loads the design's faces for the launcher: atlas metrics for layout, the TrueType file for drawing.</summary>
/// <remarks>
/// The launcher draws vector text through Skia and never samples an atlas, but it lays text out with the
/// same atlas metrics the game does — that is what makes a label the same width in both. So each face is
/// built (or read from the shared cache) as a coverage atlas at the size the game asks for, kept only for
/// its metrics, and paired with its TrueType bytes. A coverage build gives the same advances and ascent
/// as the distance-field one the game draws its titles from, and is much quicker to make.
/// </remarks>
internal static class LauncherFonts
{
    public static string FontsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "fonts");

    public static string CacheDirectory => Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "fonts");

    public static IgiTypography Load() => IgiTypography.Load(Load);

    private static UiFont Load(IgiFontRequest request)
    {
        string path = Path.Combine(FontsDirectory, request.FileName);
        byte[] data = File.ReadAllBytes(path);
        FontAtlasData atlas = FontAtlasCache.LoadOrBuild(CacheDirectory, data, request.PixelHeight, FontAtlasKind.Coverage, FontAtlasBuilder.Extended);
        return UiFont.ForMeasurement(atlas, data);
    }
}
