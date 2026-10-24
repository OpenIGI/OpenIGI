using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>What the remastered display is drawn with this frame, beyond the model and the theme.</summary>
/// <param name="Width">The screen's width in pixels.</param>
/// <param name="Height">The screen's height in pixels.</param>
/// <param name="RequestedScale">The interface scale the player asked for, or automatic.</param>
/// <param name="Seconds">The time now, in seconds: the simulation's, so a capture draws the same every run.</param>
/// <param name="Effects">Whether the visor's effects are on.</param>
/// <param name="Intensity">How strongly, nought to one.</param>
/// <param name="CrosshairSpread">How far the aiming mark stands open, in design pixels; nought hides it.</param>
public readonly record struct HudFrame(int Width, int Height, int RequestedScale, float Seconds, bool Effects, float Intensity, float CrosshairSpread)
{
    /// <summary>Gets how much of the display stays up.</summary>
    public HudDensity Density { get; init; } = HudDensity.Full;

    /// <summary>Gets a value indicating whether the display trails the turning view.</summary>
    public bool Sway { get; init; }

    /// <summary>Gets a value indicating whether threats are marked at the screen's edges and tint the visor.</summary>
    public bool Threats { get; init; }

    /// <summary>Gets a value indicating whether the objective is projected onto its place in the level.</summary>
    public bool WorldObjective { get; init; }

    /// <summary>Gets the player's view, for what is projected onto the level, or <see langword="null"/> for none.</summary>
    public HudProjector? Projector { get; init; }

    /// <summary>Gets the frame's settings as the player has them (<see cref="HudCVars"/>).</summary>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="requestedScale">The interface scale asked for.</param>
    /// <param name="seconds">The simulation's time, in seconds.</param>
    /// <param name="crosshairSpread">How far the aiming mark stands open, in design pixels; nought hides it.</param>
    /// <returns>The frame.</returns>
    public static HudFrame FromSettings(int width, int height, int requestedScale, float seconds, float crosshairSpread) =>
        new(width, height, requestedScale, seconds, HudCVars.HudFx.Value, HudCVars.HudFxIntensity.Value, crosshairSpread)
        {
            Density = HudCVars.HudDensity.Value,
            Sway = HudCVars.HudSway.Value,
            Threats = HudCVars.HudThreat.Value,
            WorldObjective = HudCVars.WorldObjective.Value,
        };
}

/// <summary>
/// The remastered heads-up display, shared by both titles: each fills a <see cref="HudModel"/> from its own player and hands it
/// in with its own <see cref="HudTheme"/>, and this draws it in that title's look, at its places, through the visor.
/// </summary>
/// <remarks>
/// <para>
/// The display is drawn into a layer of its own (<see cref="IDisplayDevice.BeginHudLayer"/>) and laid over the frame by one pass
/// that bends, fringes, lines, lights and shakes it (<see cref="IDisplayDevice.ComposeHudLayer"/>); on a device without the
/// layer, or with the effects off, it is drawn straight over the frame, flat. The glass the groups stand on samples the frame as
/// it was before the display, blurred.
/// </para>
/// <para>
/// One instance keeps the reactive state for one player's display: what changed since the last frame is what it animates.
/// </para>
/// </remarks>
public sealed class RemasteredHud : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly HudReactiveState _reactive = new();
    private HudFonts? _fonts;
    private RhiBackdrop? _glass;
    private BackdropBlur? _blur;

    /// <summary>Initializes a new instance of the <see cref="RemasteredHud"/> class.</summary>
    /// <param name="device">The device the faces are uploaded to and the layer drawn through.</param>
    /// <param name="logger">Where load problems are told.</param>
    public RemasteredHud(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets a value indicating whether the faces loaded and the display can be drawn.</summary>
    public bool IsReady => _fonts is not null;

    /// <summary>Gets what changed and how far its animations have run, as of the last draw.</summary>
    public HudReactiveState Reactive => _reactive;

    /// <summary>Loads the faces.</summary>
    /// <returns>Whether they loaded.</returns>
    public bool Load()
    {
        if (_fonts is not null)
        {
            return true;
        }

        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning("Hud", $"No interface font at {UiFontLibrary.FontsDirectory}; the remastered HUD cannot draw.");
            return false;
        }

        try
        {
            // The faces the menus and the rest of the interface share (UiFontLibrary keeps one set per device).
            IgiTypography typography = UiFontLibrary.Typography(_device);
            _fonts = new HudFonts(typography.Mono, typography.Display, typography.Heading, typography.Body);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Hud", $"Could not load the remastered HUD's fonts: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the display into an open batch, which it leaves open.</summary>
    /// <param name="batch">The open batch.</param>
    /// <param name="model">What the display shows.</param>
    /// <param name="theme">The title's look.</param>
    /// <param name="icons">The title's pictures.</param>
    /// <param name="frame">The screen, the time and the effects.</param>
    public void Draw(SpriteBatch batch, HudModel model, HudTheme theme, IHudIconSource icons, in HudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(icons);
        _reactive.Update(model, frame.Seconds);
        if (_fonts is not { } fonts || frame.Width <= 0 || frame.Height <= 0 || model.Show == HudParts.None)
        {
            return;
        }

        // The glass's backdrop is the frame as it stands, before the display: blurred now, while the batch is closed.
        batch.End();
        RhiBackdrop? glass = Glass(frame.Width, frame.Height);
        bool layered = frame.Effects && frame.Intensity > 0f && _device.BeginHudLayer();
        batch.Begin();

        float scale = theme.ScaleFor(frame.RequestedScale, frame.Width, frame.Height);
        LayoutContext layout = new(frame.Width, frame.Height);
        UiPainter painter = UiPainter.Create(batch, layout, glass);
        HudDrawContext context = new(batch, painter, theme, fonts, icons, _reactive, frame.Width, frame.Height, scale);

        Part(context, HudParts.Status, frame.Density, model, static (c, m) => HudStatus.Draw(c, m));
        Part(context, HudParts.Inventory, frame.Density, model, static (c, m) => HudInventory.Draw(c, m));
        Part(context, HudParts.Weapon, frame.Density, model, static (c, m) => HudWeapon.Draw(c, m));
        HudActions.Draw(context, model);
        if (frame.WorldObjective && frame.Projector is { } projector)
        {
            HudObjective.Draw(context, model, projector);
        }

        if (frame.Threats && frame.Projector is { } view)
        {
            HudDetection.Draw(context, model, view);
        }
        HudReadouts.DrawProgress(context, model);
        HudReadouts.DrawTimer(context, model);
        HudReadouts.DrawMessages(context, model);
        if (frame.CrosshairSpread > 0f)
        {
            HudReadouts.DrawCrosshair(context, model, frame.CrosshairSpread);
            HudAlerts.DrawAmmoArc(context, model, frame.CrosshairSpread);
        }

        if (layered)
        {
            batch.End();
            _device.ComposeHudLayer(Fx(theme.Fx, model, frame, context.DesignScale));
            batch.Begin();
        }
    }

    /// <inheritdoc/>
    /// <remarks>The faces are the interface's shared set, and stay.</remarks>
    public void Dispose() => _fonts = null;

    // A part at the density's opacity: the painter carries it to the shapes and text, the context to the title's pictures.
    private static void Part(HudDrawContext context, HudParts part, HudDensity density, HudModel model, Action<HudDrawContext, HudModel> draw)
    {
        float presence = context.Reactive.Presence(part, density, model);
        if (presence <= 0.01f)
        {
            return;
        }

        context.PartOpacity = presence;
        context.Painter.PushOpacity(presence);
        draw(context, model);
        context.Painter.PopOpacity();
        context.PartOpacity = 1f;
    }

    // The theme's visor at the player's strength, with this frame's flicker, shake, sway and hit.
    private HudFxSettings Fx(in HudFxSettings look, HudModel model, in HudFrame frame, float scale)
    {
        float shake = _reactive.Shake;
        float angle = frame.Seconds * 71f;
        HudDamage damage = model.Damage;
        (float swayX, float swayY) = frame.Sway ? _reactive.Sway : (0f, 0f);
        return look with
        {
            Intensity = Math.Clamp(frame.Intensity, 0f, 1f),
            Appear = _reactive.Appear,
            Seconds = frame.Seconds,
            Shake = ((MathF.Sin(angle) * 6f * scale * shake) + (swayX * scale), (MathF.Cos(angle * 1.3f) * 4f * scale * shake) + (swayY * scale)),
            Damage = (damage.TopLeft, damage.TopRight, damage.BottomLeft, damage.BottomRight),
        };
    }

    // The frame blurred for the glass, restated for the painter; kept while the device hands back the same blur.
    private RhiBackdrop? Glass(int width, int height)
    {
        BackdropBlur blur = _device.BlurFrameForBackdrop();
        if (!ReferenceEquals(blur, _blur) || _glass is null)
        {
            _blur = blur;
            _glass = RhiBackdrop.From(blur, width, height);
        }

        return _glass;
    }
}
