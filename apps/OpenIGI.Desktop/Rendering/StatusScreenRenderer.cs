using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

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
public sealed class StatusScreenRenderer : IDisposable
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
    public const string SpriteArchive = "LOCAL:common/sprites/sprites.res";

    /// <summary>The font the readout is set in.</summary>
    /// <remarks>
    /// <c>font3.fnt</c>, the value at the global computer-resource block's <c>+16</c> field.
    /// </remarks>
    public const string FontResource = "font3.fnt";

    /// <summary>The tall white shipped face used by the cutscene subtitle text objects.</summary>
    /// <remarks>
    /// <c>font3.fnt</c> is the nine-pixel green computer/status face. The retail subtitle is
    /// <c>font1.fnt</c>: an eighteen-pixel greyscale face whose glyph texture can actually produce
    /// white. A white vertex colour cannot turn font3's baked-green pixels white.
    /// </remarks>
    public const string SubtitleFontResource = "font1.fnt";

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private IresArchive? _archive;
    private IresArchive? _fontArchive;
    private IresArchive? _spriteArchive;
    private BitmapTextRenderer? _font;
    private BitmapTextRenderer? _subtitleFont;
    private float _actionPulsePhase;
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

    /// <summary>Advances <c>StatusScreen+1256</c> once on the fixed 30 Hz simulation tick.</summary>
    public void Tick()
    {
        _actionPulsePhase += StatusScreenLayout.ActionPulseStep;
        if (_actionPulsePhase > 2f)
        {
            _actionPulsePhase = 0f;
        }
    }

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

            // The font is a global the whole game shares, so it comes out of a different archive. The
            // desktop host does not require fsconfig.qsc and therefore may not have COMPUTER: mounted;
            // probe the installation's physical path, just as MapComputerRenderer does.
            if (fileSystem.Exists(LocalFontArchive))
            {
                _fontArchive = IresArchive.Read(fileSystem.ReadAllBytes(LocalFontArchive));
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
                _spriteArchive = IresArchive.Read(fileSystem.ReadAllBytes(SpriteArchive));
            }
            else
            {
                _logger.Warning(LogCategory, $"No sprite archive at {SpriteArchive}; the scope overlay is absent.");
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
        // declares AMMODISPLAYTYPE_NONE and gets no name, no icon and no underline.
        bool counts = weapon.AmmoDisplay != AmmoDisplayType.None;

        if (counts)
        {
            float ammoY = baseline + (StatusScreenLayout.AmmoDrop * s);
            float ammoRight = right - (StatusScreenLayout.AmmoInset * s);
            bool loadedRocket = weapon.WeaponId == 12 && roundsInClip != 0;
            int displayedReserve = weapon.WeaponId == 12 && loadedRocket ? reserve + 1 : reserve;
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
    /// Keeping it separate from <see cref="Draw"/> also lets health and progress remain visible while a
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
        _subtitleFont = null;
        _archive = null;
        _fontArchive = null;
        _spriteArchive = null;
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

    /// <summary>Draws the native action sprite selected by a retail activatable task.</summary>
    public void DrawAction(
        SpriteBatch batch,
        string spriteName,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrEmpty(spriteName);
        if (Sprite(spriteName) is not { } icon
            || icon.Asset.Frames.Count == 0)
        {
            return;
        }

        float s = Math.Max(UiScale.Native, scale);
        SpriteFrame frame = icon.Asset.Frames[0];
        (float x, float y) = StatusScreenLayout.ActionIcon(
            screenWidth, screenHeight, frame.Width * s, frame.Height * s);
        float alpha = StatusScreenLayout.ActionIconAlpha(_actionPulsePhase);
        byte alphaByte = (byte)Math.Clamp((int)(alpha * byte.MaxValue), 0, byte.MaxValue);
        icon.DrawFrame(batch, frame, x, y, s, s, new Bgra32(128, 128, 128, alphaByte));
    }

    /// <summary>Draws a stack of debug lines down the top-left corner, readable over any background.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="lines">The lines, top to bottom.</param>
    /// <param name="scale">The UI scale.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The text keeps its contrast against bright or busy scenery two ways, both cheaper and steadier
    /// than sampling the framebuffer to pick a colour: a dark translucent panel sits behind the whole
    /// block, and every line is drawn once in black, offset by a pixel, before the light text goes on top.
    /// </remarks>
    public void DrawDebug(SpriteBatch batch, IReadOnlyList<string> lines, float scale = 1f) =>
        DrawDebugPanel(batch, lines, 8f * scale, 8f * scale, scale);

    /// <summary>Draws the same stack of lines with its top-right corner at a given point.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="lines">The lines, top to bottom.</param>
    /// <param name="right">The right edge to hang the block from, in pixels.</param>
    /// <param name="top">The top edge, in pixels.</param>
    /// <param name="scale">The UI scale.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// So a second panel can be up at the same time as the first without the two writing over each
    /// other — the frame timings are read while the state lines are being read.
    /// </remarks>
    public void DrawDebugTopRight(
        SpriteBatch batch, IReadOnlyList<string> lines, float right, float top, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lines);
        if (Font() is not { } font)
        {
            return;
        }

        float widest = 0f;
        foreach (string line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                (float w, _) = font.Measure(line, scale, scale);
                widest = Math.Max(widest, w);
            }
        }

        DrawDebugPanel(batch, lines, right - widest, top, scale);
    }

    /// <summary>Draws the status messages the mission has sent, as the original places them.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="messages">The messages on screen, in slot order.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">The UI scale.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>StatusMessageScreen</c>'s draw slot, <c>0x486020</c>. Ordinary messages stack down the left of
    /// the screen from a tenth of the way in; a cutscene message leaves the column and takes a single
    /// subtitle line low on the screen instead, which is why the two are separated here rather than
    /// merged into one list with a flag.
    /// </para>
    /// <para>
    /// The step between messages is applied per message <em>drawn</em>, so when one in the middle of the
    /// column finishes the ones below it close the gap on the next frame. That is the original's own
    /// behaviour and it is why the loop advances its own cursor rather than indexing by slot.
    /// </para>
    /// </remarks>
    public void DrawStatusMessages(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(messages);
        BitmapTextRenderer? font = Font();
        BitmapTextRenderer? subtitleFont = SubtitleFont() ?? font;
        if (font is null && subtitleFont is null)
        {
            return;
        }

        float left = StatusScreenLayout.MessageLeft(screenWidth);
        float y = StatusScreenLayout.MessageTop(screenHeight);

        foreach (StatusMessageDisplay message in messages)
        {
            if (message.IsCutscene)
            {
                if (subtitleFont is not null)
                {
                    DrawSubtitle(batch, subtitleFont, message, screenWidth, screenHeight, scale);
                }
                continue;
            }

            if (font is not null)
            {
                DrawStatusMessage(batch, font, message, left, y, scale);
            }
            y += StatusScreenLayout.MessageStep * scale;
        }
    }

    /// <summary>Draws the HANDICAM battery and clock at their retail anchors.</summary>
    /// <remarks>
    /// <c>sub_4A4D10</c> draws <c>LOCAL:common/sprites/battery.spr</c> at 10%/20%, formats a
    /// four-field, 24-frame clock, and right-aligns it at 90%/80%. The original seeds the counter
    /// from its global random stream over a 21600-frame span; a task-id seed keeps captures
    /// deterministic while preserving that range and rate.
    /// </remarks>
    public void DrawHandicamTimecode(
        SpriteBatch batch, CutsceneFrame frame, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(frame);
        if (Font() is not { } font)
        {
            return;
        }

        const int FramesPerSecond = 24;
        const int SeedSpan = 21600;
        int seed = (int)((uint)(frame.Scene.Id * 1103515245 + 12345) % SeedSpan);
        int total = seed + (int)((long)frame.Scene.Ticks * FramesPerSecond / CutsceneRuntime.Frequency);
        int field3 = total % FramesPerSecond;
        int seconds = (total / FramesPerSecond) % 60;
        int minutes = (total / FramesPerSecond / 60) % 60;
        int hours = (total / FramesPerSecond / 60 / 60) % 60;
        string text = $"{hours:00} : {minutes:00} : {seconds:00} : {field3:00}";
        float scale = Math.Max(1f, screenWidth / (float)UiScale.AuthoredWidth);
        (float width, float height) = font.Measure(text, scale, scale);
        float x = (screenWidth * 0.90f) - width;
        float y = (screenHeight * 0.80f) - height;
        // The text object's three colour fields are literal 100.0 percent: full white. Retail submits
        // it once, so there is no invented black shadow or second outline pass.
        font.Draw(batch, text, x, y, new Bgra32(255, 255, 255, 255), scale, scale);

        if (Sprite("battery.spr") is { } battery && battery.Asset.Frames.Count > 0)
        {
            // The timer in 0x4A4CB0 wraps every 1.5 seconds and changes the sprite state at 0.75.
            // Multi-frame variants use the second frame for that half-cycle; shipped single-frame
            // assets retain their only frame.
            bool secondState = (frame.Scene.Ticks % 45) >= 23;
            int index = secondState && battery.Asset.Frames.Count > 1 ? 1 : 0;
            SpriteFrame batteryFrame = battery.Asset.Frames[index];
            float displayWidth = screenWidth * 0.07f;
            float spriteScale = displayWidth / Math.Max(1f, batteryFrame.Width);
            battery.DrawFrame(
                batch,
                batteryFrame,
                screenWidth * 0.10f,
                screenHeight * 0.20f,
                spriteScale,
                spriteScale,
                new Bgra32(128, 128, 128, 255));
        }
    }

    private static void DrawStatusMessage(
        SpriteBatch batch,
        BitmapTextRenderer font,
        in StatusMessageDisplay message,
        float x,
        float y,
        float scale)
    {
        // The panel, drawn open from its own centre. Its width and height are independent factors that
        // finish at different times, so for the first couple of ticks this is a zero-width line and
        // nothing shows at all -- see StatusScreenLayout.MessageHeightScale.
        float halfWidth = StatusScreenLayout.MessagePanelHalfWidth * scale;
        float centreX = x + halfWidth;
        float halfHeight = StatusScreenLayout.MessagePanelHeight * 0.5f * scale;
        float centreY = y + halfHeight;
        float openWidth = halfWidth * StatusScreenLayout.MessageWidthScale(message.DisplayFrame);
        float openHeight = halfHeight * StatusScreenLayout.MessageHeightScale(message.DisplayFrame);

        if (openWidth > 0f && openHeight > 0f)
        {
            byte alpha = (byte)Math.Clamp(
                StatusScreenLayout.MessagePanelAlpha(message.DisplayFrame) * 255f, 0f, 255f);
            Bgra32 panel = new(0, 0, 0, alpha);
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(centreX - openWidth, centreY - openHeight, 0f, 0f, panel),
                new Vertex2D(centreX + openWidth, centreY - openHeight, 1f, 0f, panel),
                new Vertex2D(centreX + openWidth, centreY + openHeight, 1f, 1f, panel),
                new Vertex2D(centreX - openWidth, centreY + openHeight, 0f, 1f, panel));
        }

        // Nothing is typed until the panel has finished opening, so there is never text over a panel that
        // is still narrower than it is.
        if (message.RevealedCharacters <= 0)
        {
            return;
        }

        DrawWrapped(
            batch,
            font,
            message.Text.AsSpan(0, message.RevealedCharacters),
            x + (StatusScreenLayout.MessageTextInset * scale),
            y + (StatusScreenLayout.MessageTextDrop * scale),
            StatusScreenLayout.MessageWrapWidth * scale,
            scale,
            cutscene: false);
    }

    private static void DrawSubtitle(
        SpriteBatch batch,
        BitmapTextRenderer font,
        in StatusMessageDisplay message,
        int screenWidth,
        int screenHeight,
        float scale)
    {
        if (message.RevealedCharacters <= 0)
        {
            return;
        }

        // No panel and no reveal: a cutscene line is whole from its first frame and runs nearly the full
        // width of the screen, because it is a subtitle rather than a readout.
        DrawWrapped(
            batch,
            font,
            message.Text.AsSpan(0, message.RevealedCharacters),
            StatusScreenLayout.MessageLeft(screenWidth),
            StatusScreenLayout.SubtitleY(screenHeight),
            screenWidth * StatusScreenLayout.SubtitleWrapFraction,
            scale,
            cutscene: true);
    }

    /// <summary>Draws text broken at spaces to a maximum width, in the message colour.</summary>
    /// <remarks>
    /// Greedy, and measured against the same font the line is drawn with, which is what the original's
    /// re-layout amounts to. A word longer than the limit overhangs rather than being split.
    /// </remarks>
    private static void DrawWrapped(
        SpriteBatch batch,
        BitmapTextRenderer font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float maxWidth,
        float scale,
        bool cutscene)
    {
        Bgra32 ink;
        if (cutscene)
        {
            // StatusMessageScreen selects the white subtitle pen for its cutscene branch. The green
            // status-feed colour belongs only to the ordinary expanding message panels.
            ink = new Bgra32(255, 255, 255, 255);
        }
        else
        {
            (float R, float G, float B) colour = StatusScreenLayout.MessageColour;
            ink = new Bgra32((byte)colour.B, (byte)colour.G, (byte)colour.R, 255);
        }
        Bgra32 shadow = new(0, 0, 0, 190);
        float step = font.LineHeight * scale;

        int lineStart = 0;
        int lastBreak = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            if (!end && text[i] != ' ')
            {
                continue;
            }

            // Measured from the start of the line to here; if that no longer fits, the previous space is
            // where the line ends.
            if (font.Measure(text[lineStart..i], scale, scale).Width > maxWidth && lastBreak > lineStart)
            {
                ReadOnlySpan<char> line = text[lineStart..lastBreak];
                font.Draw(batch, line, x + scale, y + scale, shadow, scale, scale);
                font.Draw(batch, line, x, y, ink, scale, scale);
                y += step;
                lineStart = lastBreak + 1;
            }

            lastBreak = i;
        }

        if (lineStart < text.Length)
        {
            ReadOnlySpan<char> tail = text[lineStart..];
            font.Draw(batch, tail, x + scale, y + scale, shadow, scale, scale);
            font.Draw(batch, tail, x, y, ink, scale, scale);
        }
    }

    private void DrawDebugPanel(SpriteBatch batch, IReadOnlyList<string> lines, float x, float y, float scale)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lines);
        if (Font() is not { } font)
        {
            return;
        }

        float step = font.LineHeight * scale;

        // The backing panel, sized to the widest line — drawn first so it sits behind the text (the batch
        // keeps submission order).
        float widest = 0f;
        foreach (string line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            (float w, _) = font.Measure(line, scale, scale);
            widest = Math.Max(widest, w);
        }

        if (widest > 0f)
        {
            float pad = 4f * scale;
            float left = x - pad;
            float top = y - pad;
            float right = x + widest + pad;
            float bottom = y + (step * lines.Count) + pad;
            Bgra32 panel = new(0, 0, 0, 160);
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(left, top, 0f, 0f, panel),
                new Vertex2D(right, top, 1f, 0f, panel),
                new Vertex2D(right, bottom, 1f, 1f, panel),
                new Vertex2D(left, bottom, 0f, 1f, panel));
        }

        Bgra32 shadow = new(0, 0, 0, 220);
        foreach (string line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                font.Draw(batch, line, x + scale, y + scale, shadow, scale, scale);
                font.Draw(batch, line, x, y, BitmapTextRenderer.NeutralColor, scale, scale);
            }

            y += step;
        }
    }

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

    private BitmapTextRenderer? SubtitleFont()
    {
        if (_subtitleFont is not null || _archive is null)
        {
            return _subtitleFont;
        }

        if (Sprite(SubtitleFontResource) is { IsFont: true } font)
        {
            _subtitleFont = new BitmapTextRenderer(font);
        }

        return _subtitleFont;
    }

    private SpriteResource? Sprite(string name)
    {
        if (_sprites.TryGetValue(name, out SpriteResource? cached))
        {
            return cached;
        }

        SpriteResource? resource = null;
        IresEntry? entry = Find(_archive, name) ?? Find(_fontArchive, name) ?? Find(_spriteArchive, name);
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
