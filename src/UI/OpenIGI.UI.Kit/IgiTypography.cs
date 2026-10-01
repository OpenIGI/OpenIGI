using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit;

/// <summary>One face the design asks for: which file, at what atlas size, rasterised how.</summary>
/// <param name="FileName">The TrueType file, as shipped in <c>Assets/fonts</c>.</param>
/// <param name="PixelHeight">The pixel height the atlas is rasterised at.</param>
/// <param name="Kind">Coverage for faces drawn near their atlas size, a distance field for faces drawn large and small.</param>
public readonly record struct IgiFontRequest(string FileName, float PixelHeight, FontAtlasKind Kind);

/// <summary>
/// The design's four faces, loaded once and shared by every surface that sets text: the menus, the HUD,
/// the tactical computer, the loading card, the developer console and the launcher.
/// </summary>
/// <remarks>
/// <para>
/// <b>Barlow</b> for prose and captions; <b>Barlow Condensed SemiBold</b> for the uppercase tracked labels
/// on every tab, button and value; <b>Barlow Condensed Bold</b> for titles, from a 26-unit logo up to a
/// 168-unit mission name; <b>JetBrains Mono</b> for eyebrows, key caps and numbers. All four are under
/// the SIL Open Font License and ship in <c>Assets/fonts</c>.
/// </para>
/// <para>
/// The condensed faces are distance fields because they are drawn across the widest range of sizes — a
/// coverage atlas minified from 64 to 14 pixels shimmers — while body and mono text stay coverage atlases
/// drawn close to the size they were rasterised at, which is what keeps small text crisp.
/// </para>
/// <para>
/// How a request becomes a <see cref="UiFont"/> is the caller's: the game uploads an atlas to its device,
/// the launcher keeps the TrueType bytes for its vector renderer. Either way the metrics come from the
/// same atlas build, so a line breaks in the same place in both.
/// </para>
/// </remarks>
public sealed class IgiTypography : IDisposable
{
    /// <summary>The body face.</summary>
    public const string BodyFile = "Barlow-Regular.ttf";

    /// <summary>The label face: tabs, buttons, values.</summary>
    public const string HeadingFile = "BarlowCondensed-SemiBold.ttf";

    /// <summary>The title face.</summary>
    public const string DisplayFile = "BarlowCondensed-Bold.ttf";

    /// <summary>The monospaced face: eyebrows, key caps, numbers.</summary>
    public const string MonoFile = "JetBrainsMono-Regular.ttf";

    private readonly bool _ownsFonts;

    private IgiTypography(UiFont body, UiFont heading, UiFont display, UiFont mono, bool ownsFonts)
    {
        Body = body;
        Heading = heading;
        Display = display;
        Mono = mono;
        _ownsFonts = ownsFonts;
    }

    /// <summary>Gets the request for the body face.</summary>
    public static IgiFontRequest BodyRequest => new(BodyFile, 48f, FontAtlasKind.Coverage);

    /// <summary>Gets the request for the label face.</summary>
    public static IgiFontRequest HeadingRequest => new(HeadingFile, 64f, FontAtlasKind.SignedDistanceField);

    /// <summary>Gets the request for the title face.</summary>
    public static IgiFontRequest DisplayRequest => new(DisplayFile, 64f, FontAtlasKind.SignedDistanceField);

    /// <summary>Gets the request for the monospaced face.</summary>
    public static IgiFontRequest MonoRequest => new(MonoFile, 32f, FontAtlasKind.Coverage);

    /// <summary>Gets every face the design asks for.</summary>
    public static IReadOnlyList<IgiFontRequest> Requests { get; } = [BodyRequest, HeadingRequest, DisplayRequest, MonoRequest];

    /// <summary>Gets the body face.</summary>
    public UiFont Body { get; }

    /// <summary>Gets the condensed label face.</summary>
    public UiFont Heading { get; }

    /// <summary>Gets the condensed title face.</summary>
    public UiFont Display { get; }

    /// <summary>Gets the monospaced face.</summary>
    public UiFont Mono { get; }

    /// <summary>Loads the four faces through a caller's loader.</summary>
    /// <param name="load">Turns a request into a font; called once per face.</param>
    /// <returns>The typography, owning the fonts it loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="load"/> is <see langword="null"/>.</exception>
    public static IgiTypography Load(Func<IgiFontRequest, UiFont> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return new IgiTypography(load(BodyRequest), load(HeadingRequest), load(DisplayRequest), load(MonoRequest), ownsFonts: true);
    }

    /// <summary>Wraps faces loaded elsewhere, which the typography does not dispose.</summary>
    /// <param name="body">The body face.</param>
    /// <param name="heading">The label face, or <see langword="null"/> for the body face.</param>
    /// <param name="display">The title face, or <see langword="null"/> for the label face.</param>
    /// <param name="mono">The monospaced face, or <see langword="null"/> for the body face.</param>
    /// <returns>The typography.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public static IgiTypography From(UiFont body, UiFont? heading = null, UiFont? display = null, UiFont? mono = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        UiFont label = heading ?? body;
        return new IgiTypography(body, label, display ?? label, mono ?? body, ownsFonts: false);
    }

    /// <summary>Builds a theme over these faces with the design's palette and metrics.</summary>
    /// <returns>The theme.</returns>
    public UiTheme CreateTheme() => new(Body, Heading, Mono, Display);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_ownsFonts)
        {
            return;
        }

        Body.Dispose();
        Heading.Dispose();
        Display.Dispose();
        Mono.Dispose();
    }
}
