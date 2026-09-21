using OpenIGI.Core.Configuration;
using OpenIGI.Rendering;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Text;

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

    /// <summary>The JetBrains Mono regular face the developer tools are set in.</summary>
    /// <remarks>
    /// <para>
    /// A monospaced face is not a style choice for the console, it is what makes it work. Every column
    /// in the variable browser and the log — value, default, flags, timestamp, category — can be placed
    /// on an integer multiple of one character advance measured once per size, instead of measuring
    /// every row's text on every frame. Numbers in a live watch list also stop shifting sideways as
    /// their digits change, which is the difference between reading a value and chasing it.
    /// </para>
    /// <para>
    /// Deliberately a different family from the shell's Saira: the developer tools are a separate
    /// voice from the game's own interface, and should not be mistaken for it.
    /// </para>
    /// </remarks>
    public const string Mono = "JetBrainsMono-Regular.ttf";

    /// <summary>The JetBrains Mono medium face used for developer-tool headings.</summary>
    public const string MonoMedium = "JetBrainsMono-Medium.ttf";

    /// <summary>The JetBrains Mono bold face used where a developer tool needs real emphasis.</summary>
    public const string MonoBold = "JetBrainsMono-Bold.ttf";

    /// <summary>
    /// The pixel height the developer tools' mono atlases are rasterised at.
    /// </summary>
    /// <remarks>
    /// The console draws at roughly 12–20 pixels, and a coverage atlas wants a source close to the
    /// sizes it is drawn at. The shell's 48 is far too large a source here: minifying it more than
    /// threefold thins the stems and the text greys out. Distance fields are the wrong tool for the
    /// same range — they soften exactly where a console needs to stay crisp.
    /// </remarks>
    public const float MonoPixelHeight = 32f;

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
