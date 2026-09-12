using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// A directory of upscaled diffuse textures, substituted for the game's own as they load.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I.'s art is 1,363 textures between 8x8 and 128x128. That is the largest remaining gap between this
/// renderer and a modern one, and it is the one gap no amount of lighting closes — a 64-pixel brick wall
/// is a 64-pixel brick wall under any illumination model. The <c>texture-forge</c> tool enlarges them and
/// writes the result as PNGs named after the textures they replace; this finds them.
/// </para>
/// <para>
/// <b>Absent is a supported state, not an error.</b> An installation that has never run the tool has no
/// pack, and it renders exactly as it did before any of this existed. That is what lets the pack be an
/// optional asset rather than a shipping requirement, and it is why a missing directory is a warning and
/// an unreadable file is a fall back to the original rather than an exception.
/// </para>
/// <para>
/// <b>And it can only ever be optional.</b> The game's art with a filter over it is still Innerloop's, so
/// an upscaled texture is not something this project can carry — it has to be built from the player's own
/// installation, into a directory the player names. The same reasoning that makes
/// <see cref="MaterialDetailPack"/> a per-user cache makes this a per-user pack.
/// </para>
/// <para>
/// <b>This produces PIXELS, not textures.</b> It deliberately stops at the decoded image and hands it
/// back, because what happens next — classifying a cut-out, widening its alpha, building the mip chain,
/// correcting that chain's coverage — is load-bearing for every fence, grille and canopy in the game and
/// must be identical whether the pixels came from a <c>.tex</c> or from here. There is exactly one copy of
/// that sequence and this is not it.
/// </para>
/// <para>
/// Thread-safe for reading. Nothing here touches the device, so it runs on the decode workers.
/// </para>
/// </remarks>
public sealed class HdTexturePack
{
    private const string LogCategory = "Texture";

    private readonly string? _root;
    private readonly IIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="HdTexturePack"/> class.</summary>
    /// <param name="directory">Where the upscaled textures are, or empty or missing for no pack at all.</param>
    /// <param name="logger">Where to report what was found and what would not load.</param>
    public HdTexturePack(string? directory, IIgiLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string resolved = Path.GetFullPath(directory);

        if (!Directory.Exists(resolved))
        {
            _logger.Warning(LogCategory, $"No upscaled textures at '{resolved}'; using the game's own.");
            return;
        }

        _root = resolved;
        _logger.Information(LogCategory, $"Upscaled textures from '{resolved}'.");
    }

    /// <summary>Gets a value indicating whether a pack was found to read from.</summary>
    public bool IsAvailable => _root is not null;

    /// <summary>Gets how many textures have been served from the pack.</summary>
    public int Replaced => _replaced;

    /// <summary>Gets how many were asked for and are not in it.</summary>
    public int Missing => _missing;

    private int _replaced;
    private int _missing;

    /// <summary>Reports how much of what a level asked for the pack actually had.</summary>
    /// <param name="what">What was being loaded, for the message.</param>
    /// <remarks>
    /// <para>
    /// <b>The number that answers "the remaster looks the same as the original".</b> A pack covers the
    /// textures somebody ran the tool over, and this game's levels do not share an art set evenly -- so
    /// one mission can be almost wholly replaced while the next is almost wholly untouched, and nothing
    /// on screen says which. Without a count that reads as the feature not working rather than as the
    /// feature having nothing to work on.
    /// </para>
    /// <para>
    /// Per level rather than cumulative, and the counters reset, because the question is always about
    /// the level being looked at.
    /// </para>
    /// </remarks>
    public void ReportCoverage(string what)
    {
        if (_root is null)
        {
            return;
        }

        int asked = _replaced + _missing;

        if (asked > 0)
        {
            _logger.Information(
                LogCategory,
                $"Upscaled {_replaced} of {asked} texture(s) for {what} "
                    + $"({_replaced * 100 / asked}%); {_missing} not in the pack.");
        }

        _replaced = 0;
        _missing = 0;
    }

    /// <summary>Reads the replacement for a texture, if the pack has one.</summary>
    /// <param name="name">The texture's name, without the resource prefix or extension.</param>
    /// <param name="pixels">The decoded replacement, or empty when there is none.</param>
    /// <param name="width">The replacement's width in pixels, or zero.</param>
    /// <param name="height">The replacement's height in pixels, or zero.</param>
    /// <returns><see langword="true"/> when a replacement was read.</returns>
    /// <remarks>
    /// A texture the pack does not have is <see langword="false"/> and silence — most of a level's textures
    /// will be in the pack or none of them will, and a per-texture log either way is noise. A file that is
    /// there but will not decode is <see langword="false"/> and a warning, because that one is a fault in
    /// something the player built and expects to be using.
    /// </remarks>
    public bool TryRead(string name, out Bgra32[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        if (_root is null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        string path = Path.Combine(_root, name + ".png");

        if (!File.Exists(path))
        {
            _missing++;
            return false;
        }

        try
        {
            (pixels, width, height) = PngReader.Read(path);

            if (width > 0 && height > 0)
            {
                _replaced++;
                return true;
            }

            _missing++;
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException)
        {
            _logger.Warning(LogCategory, $"Upscaled '{name}' did not decode: {error.Message}");
            _missing++;
            pixels = [];
            width = 0;
            height = 0;
            return false;
        }
    }
}
