using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
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
    // 0x0046ff00 (LaserDesignatorOverlay_StatusDisplayHandler) positions the native range holder at
    // these normalized video coordinates. They are separate from the 0.7625 y-coordinate used by the
    // binocular reticle line group.
    private const float BinocularRangeX = 0.378125f;
    private const float BinocularRangeY = 0.55f;

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
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private IresArchive? _archive;
    private IresArchive? _fontArchive;
    private IresArchive? _spriteArchive;
    private BitmapTextRenderer? _font;
    private BitmapTextRenderer? _subtitleFont;
    private SpriteResource? _subtitleFontResource;
    private float _actionPulsePhase;
    private readonly BinocularSweep _binocularSweep = new();
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

            LoadSubtitleFont(fileSystem);

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

    /// <summary>Draws the retail binocular task overlay, including its authored device frame.</summary>
    /// <remarks>
    /// Ghidra identifies the retail task as <c>BinocularOverlay_Draw</c>, with separate compass and
    /// distance handlers. The binocular sprite is therefore drawn in place of the ordinary weapon HUD;
    /// the camera and zoom state remain owned by <see cref="OpenIGI.Game.Player.BinocularsView"/>.
    /// </remarks>
    public void DrawBinocularOverlay(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets = null,
        float headingRadians = 0f,
        int simulationTick = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);

        // Retail draws the binoculars.spr frame fullscreen, stretched to the
        // full video dimensions (like the sniper scope quadrants), not as a
        // centred 256×256 status icon. At 640×480 the retail glass therefore
        // fills the screen edge to edge.
        if (GlobalSprite("binoculars.spr") is { } frameResource && frameResource.Asset.Frames.Count > 0)
        {
            SpriteFrame frame = frameResource.Asset.Frames[0];
            float scaleX = (float)screenWidth / Math.Max(1, frame.Width);
            float scaleY = (float)screenHeight / Math.Max(1, frame.Height);
            frameResource.DrawFrame(batch, frame, 0f, 0f, scaleX, scaleY, BitmapTextRenderer.NeutralColor);
        }

        // RetailBinocularUpdater (0x0051A8C0) updates the holder's path; the overlay builder at
        // 0x00471480 then draws the authored binocular frame, reticle and range labels. The native
        // widget is a range readout (weapons/strings/Binoculars/range), not a compass strip.
        DrawNativeBinocularRange(batch, screenWidth, screenHeight, targets);

        // The original advances twelve independently gated records before drawing the arms and axes.
        // FUN_00471A90 turns each phase into a column which grows upward in the lower-left ruler.
        foreach (BinocularSweepColumn column in _binocularSweep.Tick(simulationTick))
        {
            if (column.Height <= 0f)
            {
                continue;
            }

            byte red = (byte)Math.Clamp(MathF.Round(column.Phase * 128f), 0f, 128f);
            Bgra32 sweepColour = Bgra32.FromRgb(red, 128, 0);
            batch.FillRect(new RectF(
                column.X * screenWidth,
                column.Y * screenHeight,
                column.Width * screenWidth,
                column.Height * screenHeight),
                sweepColour);
        }

        // FUN_00471480 updates and draws twelve live 0x14-byte quad records at context+0x2B54,
        // then draws two textured arms and the full sight axes. The sweep state above reproduces
        // those records; the arms below are the confirmed mirrored pair: their vertex data is
        // (-.5,-2.5), (-2,-2.5), (-2,2.5), (-.5,2.5), with anchors at +/-9.375% of video width.
        float centreX = screenWidth * 0.5f;
        float centreY = screenHeight * 0.5f;
        Bgra32 signal = new(64, 160, 64, 210);
        // The reticle/axes are green in the retail device, but FUN_00471B60's contact
        // marker is the hostile-target colour. Keep the two channels separate so a
        // detected human is a red rectangle rather than an indistinguishable green dot.
        Bgra32 targetSignal = new(220, 32, 32, 235);
        float armAnchor = screenWidth * 0.09375f;
        float armScaleX = MathF.Max(1f, screenWidth * 0.015625f);
        float armScaleY = MathF.Max(1f, screenHeight * 0.020833334f);
        float armWidth = armScaleX * 1.5f;
        float armHeight = armScaleY * 5f;
        float armInset = armScaleX * 0.5f;
        batch.FillRect(
            new RectF(centreX - armAnchor - (armScaleX * 2f), centreY - (armHeight * 0.5f), armWidth, armHeight),
            signal);
        batch.FillRect(
            new RectF(centreX + armAnchor + armInset, centreY - (armHeight * 0.5f), armWidth, armHeight),
            signal);

        // FUN_00471480 emits two additional D3D line-list primitives through FUN_004B5730. The
        // first joins (0, centreY) to (screenWidth, centreY), and the second joins
        // (centreX, 0) to (centreX, screenHeight). They are the native binocular sight axes; the
        // short textured arms above are separate geometry and must not replace these lines.
        batch.FillRect(new RectF(0f, centreY - 0.5f, screenWidth, 1f), signal);
        batch.FillRect(new RectF(centreX - 0.5f, 0f, 1f, screenHeight), signal);

        if (targets is not null)
        {
            foreach (EnemyOverlayBox target in targets)
            {
                if (target.TryClipToViewport(screenWidth, screenHeight, out EnemyOverlayBox clipped))
                {
                    DrawTargetBox(batch, clipped, targetSignal);
                }
            }
        }

        // The retail task draws its authored device overlay and range widget here. Do not expose
        // internal tangent/heading diagnostics in the retail presentation; those values are camera
        // state and belong only to the enhanced themed presentation.
    }

    private static void DrawTargetBox(SpriteBatch batch, EnemyOverlayBox target, Bgra32 colour)
    {
        // Retail FUN_00471B60 passes DAT_005340B0 (the closed points {-1,-1}, {1,-1}, {1,1},
        // {-1,1}, {-1,-1}) to FUN_004B5860. That helper rotates and scales each of the four
        // segments around the projected target centre; keep that pulse instead of replacing it
        // with four static axis-aligned rectangles.
        float centreX = (target.Left + target.Right) * 0.5f;
        float centreY = (target.Top + target.Bottom) * 0.5f;
        float halfWidth = MathF.Max(1f, (target.Right - target.Left) * 0.5f * target.PulseScale);
        float halfHeight = MathF.Max(1f, (target.Bottom - target.Top) * 0.5f * target.PulseScale);
        float cos = MathF.Cos(target.RotationRadians);
        float sin = MathF.Sin(target.RotationRadians);
        Point2 TopLeft() => Rotate(centreX, centreY, -halfWidth, -halfHeight, cos, sin);
        Point2 TopRight() => Rotate(centreX, centreY, halfWidth, -halfHeight, cos, sin);
        Point2 BottomRight() => Rotate(centreX, centreY, halfWidth, halfHeight, cos, sin);
        Point2 BottomLeft() => Rotate(centreX, centreY, -halfWidth, halfHeight, cos, sin);

        Point2 a = TopLeft();
        Point2 b = TopRight();
        Point2 c = BottomRight();
        Point2 d = BottomLeft();
        float thickness = Math.Clamp(MathF.Min(target.Right - target.Left, target.Bottom - target.Top) * 0.055f, 1f, 3f);
        Bgra32 faded = colour.WithAlpha((byte)(colour.A * Math.Clamp(target.Alpha, 0f, 1f)));
        DrawSegment(batch, a, b, thickness, faded);
        DrawSegment(batch, b, c, thickness, faded);
        DrawSegment(batch, c, d, thickness, faded);
        DrawSegment(batch, d, a, thickness, faded);
    }

    private static Point2 Rotate(float centreX, float centreY, float x, float y, float cos, float sin) =>
        new(centreX + (x * cos) - (y * sin), centreY + (x * sin) + (y * cos));

    private static void DrawSegment(SpriteBatch batch, Point2 start, Point2 end, float thickness, Bgra32 colour)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        float nx = -dy / length * (thickness * 0.5f);
        float ny = dx / length * (thickness * 0.5f);
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(start.X + nx, start.Y + ny, 0f, 0f, colour),
            new Vertex2D(end.X + nx, end.Y + ny, 1f, 0f, colour),
            new Vertex2D(end.X - nx, end.Y - ny, 1f, 1f, colour),
            new Vertex2D(start.X - nx, start.Y - ny, 0f, 1f, colour));
    }

    private readonly record struct Point2(float X, float Y);

    private void DrawNativeBinocularRange(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        if (Font() is not { } font)
        {
            return;
        }

        // The native holder uses the video dimensions directly: x=37.8125% and y=55%, with the
        // text height subtracted from y. Its distance source is the refined native path; until that
        // holder is exposed to the renderer, the closest visible detector slot is the faithful managed source.
        EnemyOverlayBox? nearest = targets is null
            ? null
            : targets
                .Where(static target => target.DistanceMetres > 0f && float.IsFinite(target.DistanceMetres))
                .OrderBy(static target => target.DistanceMetres)
                .Select(static target => (EnemyOverlayBox?)target)
                .FirstOrDefault();

        float scale = UiScale.For(screenWidth, screenHeight);
        // FUN_00471480 takes the no-contact branch when the holder has no measured distance and
        // formats the authored fallback with "%s: %0.f+%s". The retail metric path is 1000+M;
        // suppressing the label until an enemy is detected leaves the binocular HUD visibly unlike
        // the original while looking at empty terrain.
        string range = BinocularRangeText(nearest);
        font.Draw(
            batch,
            range,
            screenWidth * BinocularRangeX,
            (screenHeight * BinocularRangeY) - (font.LineHeight * scale),
            new Bgra32(0, 128, 0, 255),
            scale,
            scale);
    }

    /// <summary>Formats the native binocular holder's two authored range labels.</summary>
    /// <remarks>
    /// The retail binary stores <c>RANGE: 1000+M</c> and <c>RANGE: %0.fM</c> as separate strings;
    /// the holder switches at 1000 metres instead of allowing the formatted value to grow wider.
    /// </remarks>
    internal static string FormatBinocularRange(float distanceMetres) => distanceMetres >= 1000f
        ? "RANGE: 1000+M"
        : $"RANGE: {MathF.Round(MathF.Max(0f, distanceMetres)):0}M";

    /// <summary>Returns the native range label, including the no-contact fallback.</summary>
    internal static string BinocularRangeText(EnemyOverlayBox? nearest) => nearest is { } target
        ? FormatBinocularRange(target.DistanceMetres)
        : "RANGE: 1000+M";

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
        foreach (SpriteResource? sprite in _sprites.Values)
        {
            sprite?.Dispose();
        }

        _sprites.Clear();
        _subtitleFontResource?.Dispose();
        _subtitleFontResource = null;
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
        BitmapTextRenderer? subtitleFont = SubtitleFont();
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
            // The original's text-object colour uses a different 128-based modulation convention.
            // Our sprite shader uses ordinary 0-255 multiplication, so preserving that literal value
            // incorrectly halves the supplied retail font's brightness. Full white preserves the
            // source pixels; unlike the old code this remains a single pass, with no soft shadow.
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
                if (!cutscene)
                {
                    font.Draw(batch, line, x + scale, y + scale, shadow, scale, scale);
                }
                font.Draw(batch, line, x, y, ink, scale, scale);
                y += step;
                lineStart = lastBreak + 1;
            }

            lastBreak = i;
        }

        if (lineStart < text.Length)
        {
            ReadOnlySpan<char> tail = text[lineStart..];
            if (!cutscene)
            {
                font.Draw(batch, tail, x + scale, y + scale, shadow, scale, scale);
            }
            font.Draw(batch, tail, x, y, ink, scale, scale);
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

    private SpriteResource? GlobalSprite(string name)
    {
        string cacheKey = $"global:{name}";
        if (_sprites.TryGetValue(cacheKey, out SpriteResource? cached))
        {
            return cached;
        }

        SpriteResource? resource = null;
        IresEntry? entry = Find(_spriteArchive, name);
        if (entry is not null)
        {
            try
            {
                SpriteAsset asset = SpriteLoader.Load(entry.Data, name);
                resource = SpriteResource.Upload(_device, asset);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                _logger.Warning(LogCategory, $"Could not read the global HUD sprite '{name}': {ex.Message}");
            }
        }

        _sprites[cacheKey] = resource;
        return resource;
    }

    private static IresEntry? Find(IresArchive? archive, string name) =>
        archive?.Entries.FirstOrDefault(
            e => e.Kind == IresEntryKind.Body
                && Path.GetFileName(e.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
}
