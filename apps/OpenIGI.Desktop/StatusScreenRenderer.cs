using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws the in-game HUD: the crosshair, and the weapon readout in the bottom right.</summary>
/// <remarks>
/// <para>
/// The original splits this in two, and so does this. <c>StatusScreen</c> (<c>0x486900</c>) owns the
/// archive and every sprite; its render (<c>0x486F30</c>) then sends one message to the <b>weapon in
/// hand</b>, and the <c>Gun</c> class answers it with <c>0x479D20</c> — which is what actually places
/// the crosshair, the name, the icon and the ammunition. So what you see depends on the weapon's own
/// record: <see cref="SightDisplayType"/> decides whether there is a crosshair at all, and
/// <see cref="AmmoDisplayType"/> decides how the rounds are counted.
/// </para>
/// <para>
/// <see cref="StatusScreenLayout"/> holds the arithmetic; this holds the resources and the drawing.
/// </para>
/// <para><b>Not built</b>: the health and power bars, the map computer, the action icons and the
/// mission timer. Their sprites are loaded by the same constructor but where they are drawn is a child
/// task of the status screen and has not been read. See <c>TODO.md</c>.</para>
/// </remarks>
public sealed class StatusScreenRenderer : IDisposable
{
    private const string LogCategory = "Hud";

    /// <summary>The archive the HUD's font comes out of.</summary>
    /// <remarks>
    /// <b>Not the HUD's own.</b> <c>status.res</c> holds forty-seven sprites and no font at all; the
    /// text objects the constructor builds take their font from a <i>global</i> the whole game shares
    /// (<c>sub_46B4D0()[4]</c>). Which of the three shipped faces that global holds has not been read,
    /// so the body face is used — see <see cref="FontResource"/>.
    /// </remarks>
    public const string FontArchive = "LOCAL:menusystem/menusystem.res";

    /// <summary>The font the readout is set in.</summary>
    /// <remarks>
    /// One of <c>font1</c>, <c>font3</c> and <c>font4</c>, which are the only fonts the game ships.
    /// <b>Which one the HUD uses is unread</b>; this is the body face, and it is the smallest of the
    /// three, which is what the readout wants.
    /// </remarks>
    public const string FontResource = "font1.fnt";

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private IresArchive? _archive;
    private IresArchive? _fontArchive;
    private BitmapTextRenderer? _font;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="StatusScreenRenderer"/> class.</summary>
    /// <param name="device">The device sprites are uploaded to.</param>
    /// <param name="logger">Where to report a missing archive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public StatusScreenRenderer(IGraphicsDevice device, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the HUD has its archive and can draw.</summary>
    public bool IsReady => _archive is not null;

    /// <summary>Reads the HUD's archive.</summary>
    /// <param name="fileSystem">Where to read it from.</param>
    /// <returns><see langword="false"/> when the archive is missing or unreadable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public bool Load(VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            if (!fileSystem.Exists(StatusScreenLayout.Archive))
            {
                _logger.Warning(LogCategory, $"No HUD archive at {StatusScreenLayout.Archive}; nothing is drawn.");
                return false;
            }

            _archive = IresArchive.Read(fileSystem.ReadAllBytes(StatusScreenLayout.Archive));

            // The font is a global the whole game shares, so it comes out of a different archive.
            if (fileSystem.Exists(FontArchive))
            {
                _fontArchive = IresArchive.Read(fileSystem.ReadAllBytes(FontArchive));
            }
            else
            {
                _logger.Warning(LogCategory, $"No font archive at {FontArchive}; the readout has no text.");
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Could not read the HUD archive: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the HUD for whatever is in the player's hands.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="weapon">The weapon in hand, or <see langword="null"/> to draw nothing.</param>
    /// <param name="roundsInClip">What is left in the magazine.</param>
    /// <param name="reserve">How many loose rounds are carried.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">
    /// How much to magnify the readout — see <see cref="UiScale"/>. The <i>anchors</i> are fractions of
    /// the back buffer and scale on their own; this is what stops the sprites and the text staying
    /// 640×480-sized inside them.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The order is <c>0x479D20</c>'s: the ammunition count and its gauge, then the weapon's name, then
    /// its icon, then the underline, then the crosshair last of all.
    /// </remarks>
    public void Draw(
        SpriteBatch batch,
        WeaponType? weapon,
        int roundsInClip,
        int reserve,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
    {
        ArgumentNullException.ThrowIfNull(batch);

        float s = scale < UiScale.Native ? UiScale.Native : scale;

        if (_archive is null || weapon is null)
        {
            return;
        }

        float right = StatusScreenLayout.RightMargin(screenWidth);
        float baseline = StatusScreenLayout.Baseline(screenHeight);
        float nameWidth = 0f;
        float iconWidth = 0f;

        // Every part of the readout is gated on the weapon counting its ammunition at all -- the knife
        // declares AMMODISPLAYTYPE_NONE and gets no name, no icon and no underline.
        bool counts = weapon.AmmoDisplay != AmmoDisplayType.None;

        if (counts)
        {
            float ammoY = baseline + (StatusScreenLayout.AmmoDrop * s);
            float ammoRight = right - (StatusScreenLayout.AmmoInset * s);
            float ammoWidth = DrawRightAligned(batch, StatusScreenLayout.AmmoText(reserve), ammoRight, ammoY, s);

            // The magazine gauge is a 61-frame sprite that empties as the clip does, and only the
            // clip-counting weapons show one.
            if (weapon.AmmoDisplay == AmmoDisplayType.Clip
                && Sprite(StatusScreenLayout.MagazineSprite) is { } magazine
                && magazine.Asset.Frames.Count > 0)
            {
                int frame = StatusScreenLayout.MagazineFrame(roundsInClip, weapon.RoundsPerClip);
                frame = Math.Min(frame, magazine.Asset.Frames.Count - 1);

                SpriteFrame first = magazine.Asset.Frames[0];
                float x = ammoRight - (StatusScreenLayout.MagazineGap * s) - (first.Width * s) - ammoWidth;
                magazine.DrawFrame(
                    batch, magazine.Asset.Frames[frame], x, ammoY, s, s, BitmapTextRenderer.NeutralColor);
            }

            nameWidth = DrawRightAligned(
                batch,
                weapon.DisplayName,
                right,
                baseline - (StatusScreenLayout.NameGap * s) - (LineHeight() * s),
                s);

            if (Sprite(IconFor(weapon)) is { } icon && icon.Asset.Frames.Count > 0)
            {
                SpriteFrame frame = icon.Asset.Frames[0];
                iconWidth = frame.Width * s;
                float y = baseline
                    - ((StatusScreenLayout.NameGap + StatusScreenLayout.IconGap) * s)
                    - (frame.Height * s)
                    - (LineHeight() * s);

                icon.DrawFrame(batch, frame, right - iconWidth, y, s, s, BitmapTextRenderer.NeutralColor);
            }

            // The underline spans the wider of the icon and the name, and the name gets three pixels
            // of padding before the two are compared.
            float span = Math.Max(iconWidth, nameWidth + (StatusScreenLayout.UnderlineNamePadding * s));
            (float r, float g, float b, float a) = StatusScreenLayout.UnderlineColour;
            batch.FillRect(
                new RectF(right - span, baseline, span, s),
                new Bgra32((byte)(b * 255f), (byte)(g * 255f), (byte)(r * 255f), (byte)(a * 255f)));
        }

        // The crosshair last, and only when the weapon declares one -- which the knife does not.
        if (weapon.SightDisplay == SightDisplayType.Crosshair
            && Sprite(StatusScreenLayout.CrosshairSprite) is { } cross
            && cross.Asset.Frames.Count > 0)
        {
            SpriteFrame frame = cross.Asset.Frames[0];
            (float x, float y) = StatusScreenLayout.Crosshair(
                screenWidth, screenHeight, frame.Width * s, frame.Height * s);
            cross.DrawFrame(batch, frame, x, y, s, s, BitmapTextRenderer.NeutralColor);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SpriteResource? sprite in _sprites.Values)
        {
            sprite?.Dispose();
        }

        _sprites.Clear();
        _font = null;
        _archive = null;
        _fontArchive = null;
    }

    // The icon is chosen by WEAPON ID -- 0x479D20 switches on the record's first field and picks one of
    // eighteen sprites. The names are the archive's own; matching on the weapon's script id is this
    // port's shortcut for the original's hard-coded case labels, and it gives the same answer for every
    // shipped weapon.
    private static string IconFor(WeaponType weapon) => weapon.ScriptId switch
    {
        "WEAPON_ID_AK47" => "ak47.spr",
        "WEAPON_ID_BINOCULARS" => "binoculars.spr",
        "WEAPON_ID_COLT" => "colt.spr",
        "WEAPON_ID_DESERTEAGLE" => "deserteagle.spr",
        "WEAPON_ID_DRAGUNOV" => "druganov.spr",
        "WEAPON_ID_FLASHBANG" => "flashgrenade.spr",
        "WEAPON_ID_GLOCK" => "glock.spr",
        "WEAPON_ID_GRENADE" => "splintgrenade.spr",
        "WEAPON_ID_JACKHAMMER" => "jackhammer.spr",
        "WEAPON_ID_KNIFE" => "knife.spr",
        "WEAPON_ID_M16A2" => "M16.spr",
        "WEAPON_ID_MEDIPACK" => "health.spr",
        "WEAPON_ID_MINIMI" => "SAW.spr",
        "WEAPON_ID_MP5SD" => "Mp5.spr",
        "WEAPON_ID_PROXIMITYMINE" => "proximity.spr",
        "WEAPON_ID_RPG18" => "LAW80.spr",
        "WEAPON_ID_SPAS12" => "spaz.spr",
        "WEAPON_ID_UZI" or "WEAPON_ID_UZIX2" => "uzi.spr",
        _ => "stationary.spr",
    };

    private float LineHeight() => Font()?.LineHeight ?? 0f;

    private float DrawRightAligned(SpriteBatch batch, string text, float rightEdge, float y, float scale = 1f)
    {
        if (Font() is not { } font || string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        (float width, _) = font.Measure(text, scale, scale);
        font.Draw(batch, text, rightEdge - width, y, BitmapTextRenderer.NeutralColor, scale, scale);
        return width;
    }

    private BitmapTextRenderer? Font()
    {
        if (_font is not null || _archive is null)
        {
            return _font;
        }

        if (Sprite(FontResource) is { IsFont: true } font)
        {
            _font = new BitmapTextRenderer(font);
        }

        return _font;
    }

    private SpriteResource? Sprite(string name)
    {
        if (_sprites.TryGetValue(name, out SpriteResource? cached))
        {
            return cached;
        }

        SpriteResource? resource = null;
        IresEntry? entry = Find(_archive, name) ?? Find(_fontArchive, name);
        if (entry is not null)
        {
            try
            {
                SpriteAsset asset = SpriteLoader.Load(entry.Data, name);
                resource = SpriteResource.Upload(_device, asset);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                _logger.Warning(LogCategory, $"Could not read the HUD sprite '{name}': {ex.Message}");
            }
        }

        _sprites[name] = resource;
        return resource;
    }

    private static IresEntry? Find(IresArchive? archive, string name) =>
        archive?.Entries.FirstOrDefault(
            e => e.Kind == IresEntryKind.Body
                && Path.GetFileName(e.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
}
