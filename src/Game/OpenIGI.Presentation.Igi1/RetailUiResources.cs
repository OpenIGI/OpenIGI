using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// The retail UI's sprites and fonts, read by name out of one or more <c>.res</c> archives and uploaded
/// the first time they are asked for.
/// </summary>
/// <remarks>
/// <para>
/// The HUD, the menus and the map computer each look their art up the same way: search the archives in
/// order for a body entry with the name, decode it, upload it, and remember the answer -- a miss as well
/// as a hit, so an absent sprite is looked for once rather than every frame. Names are matched without
/// regard to case, as the original's own loader does.
/// </para>
/// <para>
/// Where the three differ the difference is an option rather than a copy: whether a name is matched by
/// its stem (the menus' task parameters name bare files) and whether a sprite that will not decode is
/// logged and skipped or allowed to throw. See <see cref="RetailUiResourceOptions"/>.
/// </para>
/// </remarks>
internal sealed class RetailUiResources : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly RetailUiResourceOptions _options;
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapTextRenderer?> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IresArchive?> _archives = [];

    /// <summary>Initializes a new instance of the <see cref="RetailUiResources"/> class.</summary>
    /// <param name="device">The device sprites are uploaded to.</param>
    /// <param name="options">How names are matched and how a bad sprite is reported.</param>
    /// <param name="logger">Where an undecodable sprite is reported, when the options catch it.</param>
    public RetailUiResources(IGraphicsDevice device, RetailUiResourceOptions options, IOpenIgiLogger? logger = null)
    {
        _device = device;
        _options = options;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Adds an archive to the end of the search order.</summary>
    /// <param name="archive">The archive, or <see langword="null"/> for a slot that holds nothing.</param>
    public void AddArchive(IresArchive? archive) => _archives.Add(archive);

    /// <summary>Replaces the search order with these archives, first to last.</summary>
    /// <param name="archives">The archives; a <see langword="null"/> slot is skipped.</param>
    /// <remarks>What is already cached stays cached, hits and misses alike.</remarks>
    public void SetArchives(params IresArchive?[] archives)
    {
        _archives.Clear();
        _archives.AddRange(archives);
    }

    /// <summary>Gets a sprite by name from the first archive that holds it.</summary>
    /// <param name="name">The sprite's file name.</param>
    /// <returns>The uploaded sprite, or <see langword="null"/> when no archive holds a readable one.</returns>
    public SpriteResource? Sprite(string name) =>
        Load(name, name, restrictTo: null, restricted: false, _options.FailureSubject);

    /// <summary>Gets a sprite by name from one archive only, cached under its own key.</summary>
    /// <param name="archive">The only archive searched.</param>
    /// <param name="name">The sprite's file name.</param>
    /// <param name="cacheKey">
    /// The key the answer is cached under, kept apart from <see cref="Sprite(string)"/>'s so that a
    /// name two archives both hold resolves to the one asked for.
    /// </param>
    /// <param name="failureSubject">What a decode failure calls the sprite, in place of the options' own.</param>
    /// <returns>The uploaded sprite, or <see langword="null"/>.</returns>
    public SpriteResource? SpriteFrom(IresArchive? archive, string name, string cacheKey, string failureSubject) =>
        Load(name, cacheKey, archive, restricted: true, failureSubject);

    /// <summary>Gets a font by name: the sprite of that name, when it is a font.</summary>
    /// <param name="name">The font's file name.</param>
    /// <returns>The font, or <see langword="null"/> when no archive holds one by that name.</returns>
    public BitmapTextRenderer? Font(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_fonts.TryGetValue(name, out BitmapTextRenderer? cached))
        {
            return cached;
        }

        BitmapTextRenderer? font = Sprite(name) is { IsFont: true } sprite ? new BitmapTextRenderer(sprite) : null;
        _fonts[name] = font;
        return font;
    }

    /// <summary>Releases every uploaded sprite and forgets every answer.</summary>
    public void Dispose()
    {
        foreach (SpriteResource? sprite in _sprites.Values)
        {
            sprite?.Dispose();
        }

        _sprites.Clear();
        _fonts.Clear();
    }

    private SpriteResource? Load(
        string name,
        string cacheKey,
        IresArchive? restrictTo,
        bool restricted,
        string failureSubject)
    {
        // An unset task parameter names nothing; the original tolerates it and draws nothing.
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_sprites.TryGetValue(cacheKey, out SpriteResource? cached))
        {
            return cached;
        }

        IresEntry? entry = null;
        if (restricted)
        {
            entry = Find(restrictTo, name);
        }
        else
        {
            foreach (IresArchive? archive in _archives)
            {
                entry = Find(archive, name);
                if (entry is not null)
                {
                    break;
                }
            }
        }

        SpriteResource? resource = null;
        if (entry is not null)
        {
            // The menus decode under the entry's own name, because the parameter that asked may have
            // named only the stem; the HUD and the computer decode under the name they asked for.
            string decodeName = _options.MatchStem ? entry.Name : name;
            if (_options.LogCategory is not { } category)
            {
                resource = SpriteResource.Upload(_device, SpriteLoader.Load(entry.Data, decodeName));
            }
            else
            {
                try
                {
                    SpriteAsset asset = SpriteLoader.Load(entry.Data, decodeName);
                    resource = SpriteResource.Upload(_device, asset);
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                {
                    _logger.Warning(category, $"Could not read {failureSubject}'{name}': {ex.Message}");
                }
            }
        }

        _sprites[cacheKey] = resource;
        return resource;
    }

    private IresEntry? Find(IresArchive? archive, string name)
    {
        if (archive is null)
        {
            return null;
        }

        if (_options.MatchStem)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            return archive.Entries.FirstOrDefault(
                e => e.Kind == IresEntryKind.Body
                    && Path.GetFileNameWithoutExtension(e.Name).Equals(stem, StringComparison.OrdinalIgnoreCase));
        }

        return archive.Entries.FirstOrDefault(
            e => e.Kind == IresEntryKind.Body
                && Path.GetFileName(e.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Where the retail UI's three sprite lookups differ; see <see cref="RetailUiResources"/>.</summary>
internal sealed record RetailUiResourceOptions
{
    /// <summary>Gets whether a name matches an entry by its stem, ignoring both extensions.</summary>
    /// <remarks>
    /// The menus' task parameters name a bare file, so the archive is searched by stem and the entry is
    /// decoded under its own name. The HUD and the computer match the whole file name.
    /// </remarks>
    public bool MatchStem { get; init; }

    /// <summary>Gets the log category an undecodable sprite is reported under.</summary>
    /// <remarks>
    /// <see langword="null"/> lets the decode failure propagate to the caller, as the menus always have;
    /// otherwise the sprite is logged, cached as missing, and drawn as nothing.
    /// </remarks>
    public string? LogCategory { get; init; }

    /// <summary>Gets what a decode failure calls a sprite, immediately before its quoted name.</summary>
    /// <remarks>Ends in a space when it is not empty: <c>"the HUD sprite "</c> reads "the HUD sprite 'x'".</remarks>
    public string FailureSubject { get; init; } = string.Empty;
}
