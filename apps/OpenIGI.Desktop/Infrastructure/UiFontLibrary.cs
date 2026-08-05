using OpenIGI.Core.Configuration;
using OpenIGI.Rendering;
using OpenIGI.UI.Text;

namespace OpenIGI.Desktop;

/// <summary>
/// Loads the shell's Saira faces into GPU-resident <see cref="UiFont"/>s, cached to disk so the
/// <c>stb_truetype</c> rasterisation is paid once.
/// </summary>
/// <remarks>
/// The fonts ship in <c>Assets/fonts</c> under the OFL and are copied beside the executable by the
/// desktop project. The atlas cache lives under the per-user data directory rather than the install
/// folder, which may be read-only. Both the developer console and the R11 shell build their fonts
/// through here so there is one place that knows where the faces are and at what size they are baked.
/// </remarks>
public static class UiFontLibrary
{
    /// <summary>The Saira regular face used for body text.</summary>
    public const string Body = "Saira-Regular.ttf";

    /// <summary>The Saira condensed face used for headings and titles.</summary>
    public const string Heading = "SairaCondensed-SemiBold.ttf";

    /// <summary>The Saira semibold face used for labels and letterspaced capitals.</summary>
    /// <remarks>
    /// A heavier weight of the body face rather than a different family, so a label reads as emphasis
    /// rather than as a second voice. The tactical computer sets nearly all of its chrome in it.
    /// </remarks>
    public const string Emphasis = "Saira-SemiBold.ttf";

    /// <summary>Gets the directory the font files are read from.</summary>
    public static string FontsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "fonts");

    /// <summary>Gets the directory rasterised atlases are cached in.</summary>
    public static string CacheDirectory => Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "fonts");

    /// <summary>Determines whether a font file is present to load.</summary>
    /// <param name="fileName">The font file name.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    public static bool Exists(string fileName) => File.Exists(Path.Combine(FontsDirectory, fileName));

    /// <summary>Loads and uploads a font, rasterising and caching its atlas as needed.</summary>
    /// <param name="device">The device to upload the atlas to.</param>
    /// <param name="fileName">The font file name in <see cref="FontsDirectory"/>.</param>
    /// <param name="pixelHeight">The pixel height to rasterise the atlas at.</param>
    /// <param name="kind">Whether to build a coverage or a signed-distance-field atlas.</param>
    /// <returns>The uploaded font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The font file is missing.</exception>
    public static UiFont Load(
        IGraphicsDevice device,
        string fileName,
        float pixelHeight,
        FontAtlasKind kind = FontAtlasKind.Coverage)
    {
        ArgumentNullException.ThrowIfNull(device);

        string path = Path.Combine(FontsDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Font '{fileName}' was not found in {FontsDirectory}.", path);
        }

        byte[] data = File.ReadAllBytes(path);
        FontAtlasData atlas = FontAtlasCache.LoadOrBuild(
            CacheDirectory, data, pixelHeight, kind, FontAtlasBuilder.Extended);
        return UiFont.Upload(device, atlas);
    }
}
