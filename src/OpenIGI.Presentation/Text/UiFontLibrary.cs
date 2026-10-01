using OpenIGI.Core.Configuration;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Text;

/// <summary>
/// Loads the design's faces into GPU-resident <see cref="UiFont"/>s, cached to disk so the
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
    /// <summary>The Barlow regular face used for body text.</summary>
    public const string Body = IgiTypography.BodyFile;

    /// <summary>The Barlow Condensed bold face used for headings and titles.</summary>
    public const string Heading = IgiTypography.DisplayFile;

    /// <summary>The Barlow Condensed semibold face used for labels and letterspaced capitals.</summary>
    /// <remarks>
    /// The design sets every tab, button, value and HUD label in it, uppercase and tracked. The tactical
    /// computer sets nearly all of its chrome in it.
    /// </remarks>
    public const string Emphasis = IgiTypography.HeadingFile;

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
    /// The same face the design uses for its eyebrows and key caps, so the developer tools read as part of
    /// the same product while keeping the fixed pitch their columns depend on.
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

    /// <summary>Gets a value indicating whether every face the design asks for is present to load.</summary>
    public static bool TypographyAvailable => IgiTypography.Requests.All(request => Exists(request.FileName));

    /// <summary>Gets the design's four faces on a device, loaded on first use and shared by every surface after that.</summary>
    /// <param name="device">The device the atlases are uploaded to.</param>
    /// <returns>The typography. Callers share it and must not dispose it; it lives as long as the device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">A font file is missing.</exception>
    /// <remarks>
    /// The menus, the HUD, the tactical computer, the message feed and the diagnostics overlay all set text
    /// in the same faces, so they take them from here rather than each uploading its own copy of every
    /// atlas. Keyed on the device, so a switch to another graphics backend uploads afresh for the new one.
    /// </remarks>
    public static IgiTypography Typography(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return SharedTypography.GetValue(device, d => IgiTypography.Load(request => Load(d, request.FileName, request.PixelHeight, request.Kind)));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IGraphicsDevice, IgiTypography> SharedTypography = [];

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
