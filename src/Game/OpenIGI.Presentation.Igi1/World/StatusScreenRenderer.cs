using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws the in-game HUD, including the retail action icons.</summary>
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
/// <para>
/// The human's handler then adds the bottom-filled health power bar and any timed-interaction progress;
/// <c>StatusScreen::Render</c> finishes with the authored <c>LevelFlow</c> countdown. The map computer is
/// a separate screen and remains owned by <see cref="MapComputerRenderer"/>.
/// </para>
/// </remarks>
internal sealed class StatusScreenRenderer : IHudPresenter, OpenIGI.Presentation.Hud.Remastered.IHudIconSource
{
    private const string LogCategory = "Hud";

    /// <summary>The archive the HUD's font comes out of.</summary>
    /// <remarks>
    /// <b>Not the HUD's own.</b> <c>status.res</c> holds forty-seven sprites and no font at all. The
    /// constructor reads <c>sub_46B4D0()+16</c>, which <c>0x46B070</c> initializes from the third
    /// computer font. That archive is therefore part of the retail HUD even when the computer is closed.
    /// </remarks>
    public const string FontArchive = "COMPUTER:computer.res";

    /// <summary>The retail installation path used when the optional <c>COMPUTER:</c> alias is not mounted.</summary>
    public const string LocalFontArchive = "LOCAL:computer/computer.res";

    /// <summary>The game's global sprite archive, the third place a sprite is looked for.</summary>
    /// <remarks>
    /// The original's by-name sprite loader (<c>0x4169D0</c>) reads from here — it is where the
    /// Dragunov's <c>snipersight</c> overlay and the other non-HUD sprites live, which
    /// <c>status.res</c> does not carry.
    /// </remarks>
    public const string SpriteArchive = MissionLoader.CommonSpritesArchive;

    /// <summary>The font the readout is set in.</summary>
    /// <remarks>
    /// <c>font3.fnt</c>, the value at the global computer-resource block's <c>+16</c> field.
    /// </remarks>
    public const string FontResource = "font3.fnt";

    /// <summary>The shipped face used by the cutscene subtitle text objects.</summary>
    /// <remarks>
    /// This is deliberately not resolved from <see cref="LocalFontArchive"/>. The retail
    /// <c>StatusMessageScreen::GetFont</c> selects <c>font4.fnt</c> for cutscenes through the menu
    /// font loader; in the IGI installation that file is the standalone asset at
    /// <see cref="SubtitleFontPath"/>. <c>computer.res</c> happens to contain another font with the
    /// same name, but it is not the subtitle face.
    /// </remarks>
    public const string SubtitleFontResource = "font4.fnt";

    /// <summary>The standalone retail cutscene font selected by the original menu-font loader.</summary>
    public const string SubtitleFontPath = "LOCAL:screens/intro/font4.fnt";

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    // Every sprite and the readout font, searched through status.res, then the font archive, then the
    // global sprites -- the order the archives are listed in below.
    private readonly RetailUiResources _resources;
    private readonly BinocularOverlayRenderer _binocular;
    private readonly HudMessageRenderer _messages;
    private IresArchive? _archive;
    private IresArchive? _fontArchive;
    private IresArchive? _spriteArchive;
    private BitmapTextRenderer? _subtitleFont;
    private SpriteResource? _subtitleFontResource;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="StatusScreenRenderer"/> class.</summary>
    /// <param name="device">The device sprites are uploaded to.</param>
    /// <param name="logger">Where to report a missing archive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public StatusScreenRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _resources = new RetailUiResources(
            device,
            new RetailUiResourceOptions { LogCategory = LogCategory, FailureSubject = "the HUD sprite " },
            _logger);
        _binocular = new BinocularOverlayRenderer(Font, GlobalSprite);
        _messages = new HudMessageRenderer(Font, SubtitleFont, Sprite);
    }

    /// <summary>Gets whether the HUD has its archive and can draw.</summary>
    public bool IsReady => _archive is not null;

    /// <summary>Advances <c>StatusScreen+1256</c> once on the fixed 30 Hz simulation tick.</summary>
    public void Tick() => _messages.Tick();

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

            _archive = fileSystem.ReadArchive(StatusScreenLayout.Archive);

            // The font is a global the whole game shares, so it comes out of a different archive. The
            // desktop host does not require fsconfig.qsc and therefore may not have COMPUTER: mounted;
            // probe the installation's physical path, just as MapComputerRenderer does.
            if (fileSystem.Exists(LocalFontArchive))
            {
                _fontArchive = fileSystem.ReadArchive(LocalFontArchive);
            }
            else
            {
                _logger.Warning(
                    LogCategory,
                    $"No font archive at {FontArchive} or {LocalFontArchive}; the readout has no text.");
            }

            // And the global sprites -- the scope overlay lives here, not in status.res.
            if (fileSystem.Exists(SpriteArchive))
            {
                _spriteArchive = fileSystem.ReadArchive(SpriteArchive);
            }
            else
            {
                _logger.Warning(LogCategory, $"No sprite archive at {SpriteArchive}; the scope overlay is absent.");
            }

            LoadSubtitleFont(fileSystem);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Could not read the HUD archive: {ex.Message}");
            return false;
        }
        finally
        {
            // Whatever was read before a failure is searched, exactly as the fields it was read into.
            _resources.SetArchives(_archive, _fontArchive, _spriteArchive);
        }
    }

    /// <summary>Draws the HUD for whatever is in the player's hands.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="weapon">The weapon in hand, or <see langword="null"/> to draw nothing.</param>
    /// <param name="roundsInClip">What is left in the magazine.</param>
    /// <param name="reserve">How many loose rounds are carried.</param>
    /// <param name="secondSubCounter">The Gun+412 effect counter used to animate a revolver cylinder.</param>
    /// <param name="m203Reserve">How many M203 rounds are carried when the weapon is an M16A2.</param>
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
        int secondSubCounter,
        int m203Reserve,
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
        // declares AMMODISPLAYTYPE_NONE and gets no name, no icon and no underline.  Medipack is the
        // one intentional exception: its native task also declares NONE because it has no firearm
        // gauge, but its fixed AMMO_ID_MEDIPACK pool is still a consumable inventory count.  Hiding that
        // pool made collected medipacks appear to have zero charges even though the pickup had awarded
        // them to the player.
        bool medipack = weapon.Category == WeaponCategory.Medipack;
        bool counts = weapon.AmmoDisplay != AmmoDisplayType.None || medipack;

        if (counts)
        {
            float ammoY = baseline + (StatusScreenLayout.AmmoDrop * s);
            float ammoRight = right - (StatusScreenLayout.AmmoInset * s);
            bool loadedRocket = weapon.WeaponId == 12 && roundsInClip != 0;
            int displayedReserve = DisplayedAmmoCount(weapon, roundsInClip, reserve);
            float ammoWidth = DrawRightAligned(
                batch, StatusScreenLayout.AmmoText(displayedReserve), ammoRight, ammoY, s);

            (string? Sprite, int Frame) gauge = weapon.AmmoDisplay switch
            {
                AmmoDisplayType.Clip when weapon.WeaponId == 12 && loadedRocket =>
                    (StatusScreenLayout.RocketSprite, 0),
                AmmoDisplayType.Clip when weapon.WeaponId == 12 => (null, 0),
                AmmoDisplayType.Clip =>
                    (StatusScreenLayout.MagazineSprite,
                     StatusScreenLayout.MagazineFrame(roundsInClip, weapon.RoundsPerClip)),
                AmmoDisplayType.Barrel =>
                    (StatusScreenLayout.BarrelSprite,
                     StatusScreenLayout.BarrelFrame(roundsInClip, secondSubCounter)),
                AmmoDisplayType.Shell =>
                    (StatusScreenLayout.ShellSprite, StatusScreenLayout.ShellFrame(roundsInClip)),
                _ => (null, 0),
            };

            if (gauge.Sprite is { } gaugeName
                && Sprite(gaugeName) is { } gaugeSprite
                && gaugeSprite.Asset.Frames.Count > 0)
            {
                int frameIndex = Math.Min(gauge.Frame, gaugeSprite.Asset.Frames.Count - 1);
                SpriteFrame frame = gaugeSprite.Asset.Frames[frameIndex];
                float x = ammoRight - (StatusScreenLayout.MagazineGap * s) - (frame.Width * s) - ammoWidth;
                gaugeSprite.DrawFrame(batch, frame, x, ammoY, s, s, BitmapTextRenderer.NeutralColor);
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

        // GunM16A2's status handler calls the complete Gun parent first, then appends the M203 count
        // and STATUSSCREEN:M16_alternative.spr. The helper deliberately measures the primary-ammo
        // count again so the secondary block begins to the left of the existing clip readout.
        if (weapon.ScriptId == "WEAPON_ID_M16A2")
        {
            DrawM203(batch, reserve, m203Reserve, right, baseline, s);
        }
    }

    /// <summary>Draws the player-owned part of the retail status display.</summary>
    /// <param name="batch">The active 2D batch.</param>
    /// <param name="healthFraction">Remaining health from zero through one.</param>
    /// <param name="interactionProgress">Timed-use progress, or <see langword="null"/> when idle.</param>
    /// <param name="missionTimeRemainingTicks">
    /// Authored LevelFlow time left in 30 Hz ticks, or <see langword="null"/> when its interface timer is off.
    /// </param>
    /// <param name="screenWidth">The framebuffer width.</param>
    /// <param name="screenHeight">The framebuffer height.</param>
    /// <param name="scale">UI magnification; anchors remain resolution-relative.</param>
    /// <remarks>
    /// This is the original call order: <c>Human::StatusDisplay</c> adds health after the weapon handler,
    /// <c>HumanPlayer</c> adds the held-action progress bar, then <c>StatusScreen::Render</c> adds the timer.
    /// Keeping it separate from <see cref="Draw(SpriteBatch, WeaponType?, int, int, int, int, int, int, int)"/> also lets health and progress remain visible while a
    /// full-body interaction has hidden the first-person weapon.
    /// </remarks>
    public void DrawPlayerStatus(
        SpriteBatch batch,
        float healthFraction,
        float? interactionProgress,
        int? missionTimeRemainingTicks,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        if (_archive is null)
        {
            return;
        }

        float s = Math.Max(UiScale.Native, scale);
        DrawHealth(batch, Math.Clamp(healthFraction, 0f, 1f), screenWidth, screenHeight, s);

        if (interactionProgress is { } progress)
        {
            DrawInteractionProgress(batch, progress, screenWidth, screenHeight);
        }

        if (missionTimeRemainingTicks is { } remainingTicks && Font() is { } font)
        {
            (float x, float y) = StatusScreenLayout.TimerPosition(screenWidth, screenHeight);
            font.Draw(
                batch,
                StatusScreenLayout.TimerText(remainingTicks),
                x,
                y,
                BitmapTextRenderer.NeutralColor,
                s,
                s);
        }
    }

    /// <summary>Draws the Dragunov's scope overlay: the circular mask and the two hairlines.</summary>
    /// <param name="batch">The 2D batch, already begun.</param>
    /// <param name="screenWidth">The window width in pixels.</param>
    /// <param name="screenHeight">The window height.</param>
    /// <remarks>
    /// <para>
    /// <c>0x473040</c>, drawn while the scope latch is up — and INSTEAD of the ordinary weapon HUD,
    /// which that handler skips entirely. <c>snipersight.spr</c> is one quarter of the circular
    /// mask, drawn four times mirrored about the screen's centre; <c>snipersight_x</c> and
    /// <c>_y</c> are the hairlines, stretched across the full width and height at their own pixel
    /// thickness. Each quadrant is overscanned by the original's own margin — screen width over
    /// sprite width — so the quarters meet at the centre with no seam at any aspect.
    /// </para>
    /// <para>All five draws use the retail modulate of 128, which is this renderer's neutral.</para>
    /// </remarks>
    public void DrawSniperScope(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);

        if (Sprite("snipersight.spr") is not { } mask || mask.Asset.Frames.Count == 0)
        {
            return;
        }

        SpriteFrame quarter = mask.Asset.Frames[0];
        float overscan = (float)screenWidth / quarter.Width;
        float halfWidth = screenWidth * 0.5f;
        float halfHeight = screenHeight * 0.5f;
        float scaleX = (halfWidth + overscan) / quarter.Width;
        float scaleY = (halfHeight + overscan) / quarter.Height;

        Bgra32 neutral = BitmapTextRenderer.NeutralColor;
        mask.DrawFrame(batch, quarter, -overscan, -overscan, scaleX, scaleY, neutral);
        mask.DrawFrame(batch, quarter, screenWidth + overscan, -overscan, -scaleX, scaleY, neutral);
        mask.DrawFrame(batch, quarter, -overscan, screenHeight + overscan, scaleX, -scaleY, neutral);
        mask.DrawFrame(batch, quarter, screenWidth + overscan, screenHeight + overscan, -scaleX, -scaleY, neutral);

        if (Sprite("snipersight_x.spr") is { } horizontal && horizontal.Asset.Frames.Count > 0)
        {
            SpriteFrame line = horizontal.Asset.Frames[0];
            horizontal.DrawFrame(
                batch,
                line,
                0f,
                halfHeight - (line.Height * 0.5f),
                (float)screenWidth / line.Width,
                1f,
                neutral);
        }

        if (Sprite("snipersight_y.spr") is { } vertical && vertical.Asset.Frames.Count > 0)
        {
            SpriteFrame line = vertical.Asset.Frames[0];
            vertical.DrawFrame(
                batch,
                line,
                halfWidth - (line.Width * 0.5f),
                0f,
                1f,
                (float)screenHeight / line.Height,
                neutral);
        }
    }

    /// <inheritdoc cref="BinocularOverlayRenderer.Draw"/>
    public void DrawBinocularOverlay(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets = null,
        float headingRadians = 0f,
        int simulationTick = 0,
        float? rangeMetres = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        _binocular.Draw(batch, screenWidth, screenHeight, targets, headingRadians, simulationTick, rangeMetres);
    }

    /// <inheritdoc cref="BinocularOverlayRenderer.FormatBinocularRange"/>
    internal static string FormatBinocularRange(float distanceMetres) =>
        BinocularOverlayRenderer.FormatBinocularRange(distanceMetres);

    /// <inheritdoc cref="BinocularOverlayRenderer.BinocularRangeText"/>
    internal static string BinocularRangeText(EnemyOverlayBox? nearest) =>
        BinocularOverlayRenderer.BinocularRangeText(nearest);

    internal static int DisplayedAmmoCount(WeaponType weapon, int roundsInClip, int reserve)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (weapon.Category == WeaponCategory.Medipack)
        {
            int loaded = roundsInClip == GunTiming.UnloadedClipSentinel ? 0 : Math.Max(0, roundsInClip);
            return loaded + Math.Max(0, reserve);
        }

        bool loadedRocket = weapon.WeaponId == 12 && roundsInClip != 0;
        return weapon.WeaponId == 12 && loadedRocket ? reserve + 1 : reserve;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resources.Dispose();
        _subtitleFontResource?.Dispose();
        _subtitleFontResource = null;
        _subtitleFont = null;
        _archive = null;
        _fontArchive = null;
        _spriteArchive = null;
    }

    // The icon is chosen by WEAPON ID -- 0x479D20 switches on the record's first field and picks one of
    // eighteen sprites. The names are the archive's own; matching on the weapon's script id is this
    // port's shortcut for the original's hard-coded case labels, and it gives the same answer for every
    // shipped weapon.
    internal static string IconFor(WeaponType weapon) => weapon.ScriptId switch
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

    /// <summary>Draws activate type 10 exactly as the ladder status-display handler does.</summary>
    /// <param name="batch">The active 2D batch.</param>
    /// <param name="screenWidth">The framebuffer width.</param>
    /// <param name="screenHeight">The framebuffer height.</param>
    /// <param name="scale">UI magnification for the native sprite dimensions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void DrawLadderAction(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
        => DrawAction(batch, StatusScreenLayout.LadderActionSprite, screenWidth, screenHeight, scale);

    /// <inheritdoc cref="HudMessageRenderer.DrawAction"/>
    public void DrawAction(
        SpriteBatch batch,
        string spriteName,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
        => _messages.DrawAction(batch, spriteName, screenWidth, screenHeight, scale);

    /// <inheritdoc cref="HudMessageRenderer.DrawStatusMessages"/>
    public void DrawStatusMessages(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float scale = 1f)
        => _messages.DrawStatusMessages(batch, messages, screenWidth, screenHeight, scale);

    /// <inheritdoc cref="HudMessageRenderer.DrawHandicamTimecode"/>
    public void DrawHandicamTimecode(
        SpriteBatch batch, CutsceneFrame frame, int screenWidth, int screenHeight)
        => _messages.DrawHandicamTimecode(batch, frame, screenWidth, screenHeight);

    private float LineHeight() => Font()?.LineHeight ?? 0f;

    private void DrawM203(
        SpriteBatch batch,
        int primaryReserve,
        int m203Reserve,
        float right,
        float baseline,
        float scale)
    {
        float primaryWidth = TextWidth(StatusScreenLayout.AmmoText(primaryReserve), scale);
        SpriteResource? indicator = Sprite(StatusScreenLayout.M16AlternativeSprite);
        SpriteFrame? indicatorFrame = indicator is { Asset.Frames.Count: > 0 }
            ? indicator.Asset.Frames[0]
            : null;
        float indicatorWidth = indicatorFrame is { } frame ? frame.Width * scale : 0f;
        float helperX = right
            - (StatusScreenLayout.AmmoInset * scale)
            - (StatusScreenLayout.MagazineGap * scale)
            - indicatorWidth
            - primaryWidth;
        float y = baseline + (StatusScreenLayout.AmmoDrop * scale);
        float secondaryWidth = DrawRightAligned(
            batch, StatusScreenLayout.AmmoText(m203Reserve), helperX, y, scale);

        if (indicator is not null && indicatorFrame is { } secondaryFrame)
        {
            float x = helperX
                - secondaryWidth
                - (StatusScreenLayout.MagazineGap * scale)
                - indicatorWidth;
            indicator.DrawFrame(
                batch,
                secondaryFrame,
                x,
                y,
                scale,
                scale,
                BitmapTextRenderer.NeutralColor);
        }
    }

    private void DrawHealth(
        SpriteBatch batch,
        float healthFraction,
        int screenWidth,
        int screenHeight,
        float scale)
    {
        (float anchorX, float anchorY) = StatusScreenLayout.HealthAnchor(screenWidth, screenHeight);
        Bgra32 neutral = BitmapTextRenderer.NeutralColor;

        if (Sprite(StatusScreenLayout.HealthBackgroundSprite) is { } background
            && background.Asset.Frames.Count > 0)
        {
            SpriteFrame frame = background.Asset.Frames[0];
            background.DrawFrame(
                batch,
                frame,
                anchorX - (frame.Width * scale * 0.5f),
                anchorY - (frame.Height * scale * 0.5f),
                scale,
                scale,
                neutral);
        }

        if (healthFraction <= 0f
            || Sprite(StatusScreenLayout.HealthForegroundSprite) is not { } foreground
            || foreground.Asset.Frames.Count == 0)
        {
            return;
        }

        // Human::StatusDisplay does not squeeze the bar. It advances both the destination top and the
        // source V coordinate by (1-health), retaining the lower portion of powerbar.spr at native width.
        SpriteFrame fill = foreground.Asset.Frames[0];
        float cursorX = anchorX
            - (StatusScreenLayout.HealthForegroundInset * scale)
            - (fill.Width * scale * 0.5f);
        float cursorY = anchorY
            - (StatusScreenLayout.HealthForegroundInset * scale)
            - (fill.Height * scale * 0.5f);
        float croppedTop = 1f - healthFraction;
        RectF destination = new(
            cursorX + (fill.OffsetX * scale),
            cursorY + (fill.OffsetY * scale) + (fill.Height * scale * croppedTop),
            fill.Width * scale,
            fill.Height * scale * healthFraction);
        RectF source = RectF.FromEdges(
            fill.U0,
            fill.V0 + ((fill.V1 - fill.V0) * croppedTop),
            fill.U1,
            fill.V1);
        batch.Draw(foreground.Textures[fill.TextureIndex], destination, source, neutral);
    }

    private static void DrawInteractionProgress(
        SpriteBatch batch,
        float progress,
        int screenWidth,
        int screenHeight)
    {
        (float fillX, float fillY, float fillWidth, float fillHeight) =
            StatusScreenLayout.InteractionFill(screenWidth, screenHeight, progress);
        if (fillWidth > 0f && fillHeight > 0f)
        {
            // Submission order and colours match HumanPlayer's status-display handler at 0x413120.
            batch.FillRect(new RectF(fillX, fillY, fillWidth, fillHeight), new Bgra32(0, 0, 128, 128));
        }

        (float backX, float backY, float backWidth, float backHeight) =
            StatusScreenLayout.InteractionBackground(screenWidth, screenHeight);
        batch.FillRect(new RectF(backX, backY, backWidth, backHeight), new Bgra32(0, 100, 0, 128));
    }

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

    private float TextWidth(string text, float scale) =>
        Font() is { } font && !string.IsNullOrEmpty(text) ? font.Measure(text, scale, scale).Width : 0f;

    private BitmapTextRenderer? Font() => _archive is null ? null : _resources.Font(FontResource);

    private BitmapTextRenderer? SubtitleFont()
        => _subtitleFont;


    private void LoadSubtitleFont(VirtualFileSystem fileSystem)
    {
        _subtitleFont = null;
        _subtitleFontResource?.Dispose();
        _subtitleFontResource = null;

        if (!fileSystem.Exists(SubtitleFontPath))
        {
            _logger.Warning(LogCategory, $"No cutscene subtitle font at {SubtitleFontPath}; cutscene text is absent.");
            return;
        }

        try
        {
            SpriteAsset asset = SpriteLoader.Load(
                fileSystem.ReadAllBytes(SubtitleFontPath),
                Path.GetFileName(SubtitleFontPath));
            if (!asset.IsFont)
            {
                _logger.Warning(LogCategory, $"Cutscene subtitle asset at {SubtitleFontPath} is not a font.");
                return;
            }

            _subtitleFontResource = SpriteResource.Upload(_device, asset);
            _subtitleFont = new BitmapTextRenderer(_subtitleFontResource);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Could not read cutscene subtitle font at {SubtitleFontPath}: {ex.Message}");
        }
    }

    private SpriteResource? Sprite(string name) => _resources.Sprite(name);

    /// <inheritdoc/>
    /// <remarks>The status archive's own sprites, which the remastered display draws as Project I.G.I.'s pictures.</remarks>
    public bool TryMeasure(OpenIGI.Presentation.Hud.Remastered.HudIcon icon, out float width, out float height)
    {
        if (icon.IsSome && Sprite(icon.Name) is { Asset.Frames.Count: > 0 } sprite)
        {
            width = sprite.Asset.Frames[0].Width;
            height = sprite.Asset.Frames[0].Height;
            return true;
        }

        width = 0f;
        height = 0f;
        return false;
    }

    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, OpenIGI.Presentation.Hud.Remastered.HudIcon icon, float x, float y, float scale, Bgra32 colour)
    {
        if (icon.IsSome && Sprite(icon.Name) is { Asset.Frames.Count: > 0 } sprite)
        {
            sprite.DrawFrame(batch, sprite.Asset.Frames[0], x, y, scale, scale, colour);
        }
    }

    // Only the global sprite archive, under a key of its own: binoculars.spr names both the HUD icon in
    // status.res and the device frame in the global archive, and the overlay wants the frame.
    private SpriteResource? GlobalSprite(string name) =>
        _resources.SpriteFrom(_spriteArchive, name, $"global:{name}", "the global HUD sprite ");
}
