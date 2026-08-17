using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using System.Globalization;

namespace OpenIGI.Desktop;

/// <summary>The remaster's tactical, resolution-independent in-game HUD.</summary>
/// <remarks>
/// The status screen remains the faithful retail renderer.  This deliberately consumes the same live
/// player, weapon and objective data but presents it with the modern shell's distance-field fonts and
/// layout system, so changing the surface cannot change the simulation behind it.
/// </remarks>
internal sealed class RemasteredHudRenderer : IDisposable
{
    /// <summary>Leaves breathing room below the compact objective readout for the modern comms cards.</summary>
    public const float MessageTopInsetDesign = 116f;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private UiFont? _body;
    private UiFont? _display;
    private bool _disposed;

    public RemasteredHudRenderer(IGraphicsDevice device, IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    public bool IsReady => _body is not null;

    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!UiFontLibrary.Exists(UiFontLibrary.Body))
        {
            _logger.Warning("Hud", $"No interface font at {UiFontLibrary.FontsDirectory}; remaster HUD cannot draw.");
            return false;
        }

        try
        {
            _body = UiFontLibrary.Load(_device, UiFontLibrary.Body, pixelHeight: 48f);

            // A distance-field face for the binocular's marker labels, so a range digit can carry the
            // outline and glow that make it read as projected onto the optics rather than printed on
            // them. Optional: a missing heading face falls back to the plain body font on the markers.
            _display = UiFontLibrary.Exists(UiFontLibrary.Heading)
                ? UiFontLibrary.Load(_device, UiFontLibrary.Heading, pixelHeight: 64f, FontAtlasKind.SignedDistanceField)
                : null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Hud", $"Could not load the remaster HUD fonts: {ex.Message}");
            return false;
        }
    }

    public void Draw(SpriteBatch batch, in RemasteredHudSnapshot snapshot, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = new UiPainter(batch, context);
        DrawCompass(painter, context, body, snapshot.HeadingRadians);
        DrawObjectives(painter, context, body, snapshot.Objectives);
        DrawVitals(painter, context, body, snapshot.HealthFraction);
        DrawWeapon(painter, context, body, snapshot);
        DrawCrosshair(painter, context, snapshot.Weapon, snapshot.SniperScoped);
        DrawInteraction(painter, context, body, snapshot.ActionSprite);
    }

    /// <summary>Draws the remastered binocular optics overlay over the glass-lens post-process.</summary>
    /// <remarks>
    /// The glass tint, bloom and bezel vignette are a resolve-time display transform (see
    /// <see cref="OpenIGI.Rendering.BinocularDisplaySettings"/>); this draws the crisp vector interface
    /// on top of it. Contacts are modern chevron markers with a glowing range label rather than the
    /// retail rotating rectangle -- the faithful rectangle stays on the vanilla <c>StatusScreenRenderer</c>.
    /// </remarks>
    public void DrawBinocularOverlay(
        SpriteBatch batch,
        float headingRadians,
        float tanX,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets,
        float seconds,
        float? rangeMetres)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = new UiPainter(batch, context);

        // No full-screen wash and no opaque frame: the lens post-process owns the glass now, so the HUD
        // stays crisp chrome over it. Thin rails and corner ticks read as an optics frame without
        // tinting the picture a second time.
        float inset = context.Px(30f);
        UiRect frame = new(inset, inset, context.SafeRect.Width - (inset * 2f), context.SafeRect.Height - (inset * 2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, frame.Width, context.Px(2f)), Signal.WithAlpha(190));
        painter.FillRect(new UiRect(frame.X, frame.Bottom - context.Px(2f), frame.Width, context.Px(2f)), Signal.WithAlpha(120));
        DrawCornerTicks(painter, context, frame, Signal.WithAlpha(170));

        DrawCompass(painter, context, body, headingRadians);

        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        DrawReticle(painter, context, centreX, centreY);

        if (targets is not null)
        {
            foreach (EnemyOverlayBox target in targets)
            {
                if (target.TryClipToViewport(screenWidth, screenHeight, out EnemyOverlayBox clipped))
                {
                    DrawContactMarker(painter, context, clipped, seconds);
                }
            }
        }

        // The range is the crosshair raycast the caller supplies -- the distance to whatever the centre
        // rests on -- exactly as the vanilla readout shows, not the nearest detected contact. Same
        // 1000 m cap as the retail widget.
        string range = rangeMetres is { } metres && float.IsFinite(metres) && metres > 0f && metres < 1000f
            ? FormattableString.Invariant($"RANGE  {metres:0}M")
            : "RANGE  1000+M";
        float magnification = tanX > 0.0001f ? HumanView.DefaultTanX / tanX : 1f;
        UiVectors.DrawTracked(
            painter, body, "OPTICS // BINOCULAR",
            new UiVec2(frame.X + context.Px(18f), frame.Bottom - context.Px(48f)), context.Px(15f), Signal, context.Px(2f));
        painter.DrawText(
            body, FormattableString.Invariant($"x{magnification:0.0}    {range}"),
            new UiVec2(frame.Right - context.Px(250f), frame.Bottom - context.Px(48f)), context.Px(15f), Text);
    }

    /// <summary>The colour a contact's marker is drawn in, by its detector team classification.</summary>
    private static Bgra32 MarkerColour(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgb(120, 235, 140),
        BinocularMarkerColor.Disabled => Bgra32.FromRgb(120, 170, 255),
        _ => Bgra32.FromRgb(255, 72, 72),
    };

    /// <summary>Draws one detected contact: a bobbing chevron over the target and a glowing range label.</summary>
    private void DrawContactMarker(UiPainter painter, LayoutContext context, EnemyOverlayBox target, float seconds)
    {
        float alpha = Math.Clamp(target.Alpha, 0f, 1f);
        if (alpha <= 0f)
        {
            return;
        }

        float centreX = (target.Left + target.Right) * 0.5f;
        Bgra32 colour = MarkerColour(target.Marker);

        // The acquisition flourish reuses the detector's own entry pulse: while PulseScale is still
        // large the contact is freshly seen, so an expanding, fading ring collapses onto the chevron.
        if (target.PulseScale > 1.05f)
        {
            float boxCentreY = (target.Top + target.Bottom) * 0.5f;
            float ringRadius = context.Px(9f) * target.PulseScale * 0.5f;
            float ringFade = Math.Clamp((target.PulseScale - 1f) / 10f, 0f, 1f) * alpha * 0.7f;
            UiVectors.StrokeRing(
                painter, new UiVec2(centreX, boxCentreY), ringRadius,
                MathF.Max(1f, context.Px(2f)), colour.WithAlpha((byte)(255f * ringFade)));
        }

        // The chevron floats just above the contact, bobbing on a per-target phase so a cluster of
        // marks does not pulse in lockstep. A dark backing chevron keeps it legible over bright terrain.
        float bob = context.Px(4f) * MathF.Sin((seconds * 2.2f) + target.TargetId);
        var apex = new UiVec2(centreX, target.Top - context.Px(7f) + bob);
        float halfWidth = context.Px(9f);
        float height = context.Px(11f);
        UiVectors.DrawChevron(
            painter, new UiVec2(apex.X, apex.Y + context.Px(1.5f)),
            halfWidth + context.Px(1.5f), height + context.Px(2f), Bgra32.FromRgba(0, 0, 0, (byte)(130f * alpha)));
        UiVectors.DrawChevron(painter, apex, halfWidth, height, colour.WithAlpha((byte)(255f * alpha)));

        // The range, above the chevron, glowing so it reads as projected onto the optics.
        string label = $"{MathF.Max(0f, target.DistanceMetres):0}M";
        float labelSize = context.Px(15f);
        UiFont labelFont = _display ?? _body!;
        float labelTop = apex.Y - height - labelFont.LineHeight(labelSize) - context.Px(4f);
        DrawGlowLabel(painter, label, centreX, labelTop, labelSize, alpha);
    }

    /// <summary>Draws a centred, glowing label, using the distance-field face when one loaded.</summary>
    private void DrawGlowLabel(UiPainter painter, string text, float centreX, float topY, float size, float alpha)
    {
        Bgra32 white = Bgra32.FromRgb(238, 246, 250).WithAlpha((byte)(255f * alpha));
        if (_display is { } display)
        {
            float width = display.MeasureWidth(text, size);
            SdfTextParams effects = SdfTextParams.Plain(0f)
                .WithOutline(Bgra32.FromRgba(0, 0, 0, (byte)(190f * alpha)), 0.18f)
                .WithGlow(Signal, 0.5f, 0.5f * alpha);
            painter.DrawText(display, text, new UiVec2(centreX - (width * 0.5f), topY), size, white, effects);
        }
        else if (_body is { } body)
        {
            float width = body.MeasureWidth(text, size);
            Bgra32 backing = Bgra32.FromRgba(0, 0, 0, (byte)(170f * alpha));
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f) + 1f, topY + 1f), size, backing);
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f), topY), size, white);
        }
    }

    /// <summary>Draws the gapped rangefinder reticle: crosshair arms, a framing ring and a centre dot.</summary>
    private static void DrawReticle(UiPainter painter, LayoutContext context, float centreX, float centreY)
    {
        Bgra32 mint = Signal.WithAlpha(210);
        float arm = context.Px(24f);
        float gap = context.Px(10f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX + gap, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY - gap - arm, weight, arm), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY + gap, weight, arm), mint);
        UiVectors.StrokeRing(
            painter, new UiVec2(centreX, centreY), context.Px(3.4f), MathF.Max(1f, context.Px(1.2f)), mint.WithAlpha(150));
        UiVectors.FillDisc(painter, new UiVec2(centreX, centreY), MathF.Max(1f, context.Px(1.6f)), Accent);
    }

    /// <summary>Draws short L-brackets at the corners of the optics frame.</summary>
    private static void DrawCornerTicks(UiPainter painter, LayoutContext context, UiRect frame, Bgra32 colour)
    {
        float length = context.Px(28f);
        float weight = MathF.Max(1f, context.Px(2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - length, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Bottom - length, weight, length), colour);
    }

    /// <summary>Draws the modern presentation of the same timed state progress the retail HUD exposes.</summary>
    public void DrawInteractionProgress(SpriteBatch batch, float progress, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = new UiPainter(batch, context);
        float width = MathF.Min(context.Px(430f), context.SafeRect.Width * 0.52f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float y = context.SafeRect.Bottom - context.Px(118f);
        painter.DrawText(body, "INTERACTING", new UiVec2(x, y), context.Px(15f), Muted);
        DrawMeter(
            painter,
            new UiRect(x, y + context.Px(25f), width, context.Px(6f)),
            progress,
            Accent);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _body?.Dispose();
        _body = null;
        _display?.Dispose();
        _display = null;
    }

    private static void DrawObjectives(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        IReadOnlyList<MissionObjective> objectives)
    {
        MissionObjective? primary = RemasteredHudPresentation.PrimaryObjective(objectives);
        if (primary is null && objectives.Count == 0)
        {
            return;
        }

        float x = context.SafeRect.X + context.Px(34f);
        float y = context.SafeRect.Y + context.Px(30f);
        float width = MathF.Min(context.Px(470f), context.SafeRect.Width * 0.36f);
        float accentWidth = context.Px(3f);
        float textX = x + context.Px(15f);
        float labelSize = context.Px(15f);
        float textSize = context.Px(24f);
        string text = primary?.Text.Length > 0 ? primary.Text : primary?.TextResource ?? "OBJECTIVES COMPLETE";
        string wrapped = body.WrapText(text, textSize, width - (textX - x));
        float height = body.Measure(wrapped, textSize).Y + context.Px(35f);

        painter.FillRect(new UiRect(x, y + context.Px(2f), accentWidth, height), Accent);
        painter.DrawText(body, "MISSION OBJECTIVE", new UiVec2(textX, y), labelSize, Muted);
        painter.DrawText(body, wrapped, new UiVec2(textX, y + context.Px(22f)), textSize, Text);
    }

    private static void DrawCompass(UiPainter painter, LayoutContext context, UiFont body, float headingRadians)
    {
        float width = MathF.Min(context.Px(320f), context.SafeRect.Width * 0.26f);
        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float x = centreX - (width * 0.5f);
        float y = context.SafeRect.Y + context.Px(12f);
        float lineY = y + context.Px(30f);
        float heading = RemasteredHudPresentation.HeadingDegrees(headingRadians);
        const float visibleDegrees = 100f;
        const float halfVisible = visibleDegrees * 0.5f;
        int centreDegrees = ((int)MathF.Round(heading) + 360) % 360;

        painter.FillRect(new UiRect(x, lineY, width, context.Px(1f)), Bgra32.FromRgba(255, 255, 255, 42));

        float firstTick = MathF.Ceiling((heading - halfVisible) / 10f) * 10f;
        for (float degrees = firstTick; degrees <= heading + halfVisible; degrees += 10f)
        {
            int normalized = ((int)degrees % 360 + 360) % 360;
            float tickX = centreX + (((degrees - heading) / visibleDegrees) * width);
            bool major = normalized % 30 == 0;
            float tickHeight = context.Px(major ? 7f : 3f);
            painter.FillRect(
                new UiRect(tickX - context.Px(0.5f), lineY, context.Px(1f), tickHeight),
                major ? Muted.WithAlpha(200) : Muted.WithAlpha(100));
        }

        painter.FillRect(
            new UiRect(centreX - context.Px(1.5f), lineY - context.Px(7f), context.Px(3f), context.Px(16f)),
            Accent);
        string readout = $"{centreDegrees:000}°";
        UiVec2 readoutBounds = body.Measure(readout, context.Px(16f));
        painter.DrawText(
            body,
            readout,
            new UiVec2(centreX - (readoutBounds.X * 0.5f), y),
            context.Px(16f),
            Text);

        for (int cardinal = 0; cardinal < 360; cardinal += 45)
        {
            float relative = cardinal - heading;
            if (relative > 180f)
            {
                relative -= 360f;
            }
            else if (relative < -180f)
            {
                relative += 360f;
            }

            if (MathF.Abs(relative) > halfVisible)
            {
                continue;
            }

            string label = CompassCardinal(cardinal);
            float labelSize = cardinal % 90 == 0 ? context.Px(14f) : context.Px(12f);
            UiVec2 labelBounds = body.Measure(label, labelSize);
            painter.DrawText(
                body,
                label,
                new UiVec2(centreX + ((relative / visibleDegrees) * width) - (labelBounds.X * 0.5f), lineY + context.Px(12f)),
                labelSize,
                cardinal % 90 == 0 ? Text : Muted);
        }
    }

    private static string CompassCardinal(int degrees) => degrees switch
    {
        0 => "N",
        45 => "NE",
        90 => "E",
        135 => "SE",
        180 => "S",
        225 => "SW",
        270 => "W",
        315 => "NW",
        _ => string.Empty,
    };

    private static void DrawVitals(UiPainter painter, LayoutContext context, UiFont body, float health)
    {
        float width = context.Px(326f);
        float height = context.Px(70f);
        float x = context.SafeRect.X + context.Px(34f);
        float y = context.SafeRect.Bottom - context.Px(38f) - height;
        float fraction = Math.Clamp(health, 0f, 1f);
        Bgra32 healthColour = RemasteredHudPresentation.HealthColour(fraction);

        painter.DrawText(body, "HEALTH", new UiVec2(x, y), context.Px(18f), Muted);
        painter.DrawText(
            body,
            $"{(int)MathF.Round(fraction * 100f):00}",
            new UiVec2(x, y + context.Px(21f)),
            context.Px(44f),
            healthColour);

        float barX = x + context.Px(92f);
        float barY = y + context.Px(54f);
        float barWidth = width - context.Px(92f);
        float barHeight = context.Px(5f);
        DrawMeter(painter, new UiRect(barX, barY, barWidth, barHeight), fraction, healthColour);

    }

    private static void DrawWeapon(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        in RemasteredHudSnapshot snapshot)
    {
        if (snapshot.Weapon is not { } weapon)
        {
            return;
        }

        float right = context.SafeRect.Right - context.Px(34f);
        float y = context.SafeRect.Bottom - context.Px(114f);
        float left = right - context.Px(360f);
        float ammoGap = context.Px(26f);
        float ammoAreaWidth = context.Px(176f);
        float weaponRight = right - ammoAreaWidth - ammoGap;
        float labelSize = context.Px(17f);

        painter.DrawText(body, RemasteredHudPresentation.WeaponClass(weapon), new UiVec2(left, y), labelSize, Muted);
        painter.DrawText(body, weapon.DisplayName.ToUpperInvariant(), new UiVec2(left, y + context.Px(17f)), context.Px(30f), Text);
        painter.FillRect(
            new UiRect(left, y + context.Px(55f), Math.Max(context.Px(42f), weaponRight - left), context.Px(2f)),
            Accent.WithAlpha(185));

        string state = RemasteredHudPresentation.WeaponStateLabel(snapshot.WeaponState);
        if (state.Length > 0)
        {
            painter.DrawText(body, state, new UiVec2(left, y + context.Px(65f)), context.Px(14f), Accent);
        }

        (string magazine, string reserve) =
            RemasteredHudPresentation.AmmoColumns(weapon, snapshot.RoundsInClip, snapshot.ReserveAmmo);
        float ammoSize = context.Px(42f);
        UiVec2 reserveSize = body.Measure(reserve, ammoSize);
        float reserveX = right - reserveSize.X;
        string reserveLabel = "RESERVE";
        UiVec2 reserveLabelSize = body.Measure(reserveLabel, labelSize);
        painter.DrawText(body, reserveLabel, new UiVec2(right - reserveLabelSize.X, y), labelSize, Muted);
        painter.DrawText(body, reserve, new UiVec2(reserveX, y + context.Px(18f)), ammoSize, Muted);

        UiVec2 magazineSize = body.Measure(magazine, ammoSize);
        float magazineRight = reserveX - context.Px(30f);
        string magazineLabel = "MAG";
        UiVec2 magazineLabelSize = body.Measure(magazineLabel, labelSize);
        painter.DrawText(body, magazineLabel, new UiVec2(magazineRight - magazineLabelSize.X, y), labelSize, Muted);
        painter.DrawText(body, magazine, new UiVec2(magazineRight - magazineSize.X, y + context.Px(18f)), ammoSize, Text);
        painter.FillRect(
            new UiRect(reserveX - context.Px(15f), y + context.Px(20f), context.Px(1f), context.Px(36f)),
            Muted.WithAlpha(115));
    }

    private static void DrawCrosshair(
        UiPainter painter,
        LayoutContext context,
        WeaponType? weapon,
        bool sniperScoped)
    {
        if (!RemasteredHudPresentation.ShowsCrosshair(weapon, sniperScoped))
        {
            return;
        }

        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        float gap = context.Px(7f);
        float arm = context.Px(7f);
        float thickness = MathF.Max(1f, context.Px(1.5f));
        Bgra32 cross = Text.WithAlpha(220);
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (thickness * 0.5f), arm, thickness), cross);
        painter.FillRect(new UiRect(centreX + gap, centreY - (thickness * 0.5f), arm, thickness), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY - gap - arm, thickness, arm), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY + gap, thickness, arm), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY - (thickness * 0.5f), thickness, thickness), Accent);
    }

    private static void DrawInteraction(UiPainter painter, LayoutContext context, UiFont body, string? actionSprite)
    {
        string label = RemasteredHudPresentation.InteractionLabel(actionSprite);
        if (label.Length == 0)
        {
            return;
        }

        float textSize = context.Px(17f);
        string prompt = $"INTERACT  ·  {label}";
        UiVec2 size = body.Measure(prompt, textSize);
        float x = context.SafeRect.X + ((context.SafeRect.Width - size.X) * 0.5f);
        float y = context.SafeRect.Y + (context.SafeRect.Height * 0.5f) + context.Px(47f);
        painter.DrawText(body, prompt, new UiVec2(x, y), textSize, Text);
    }

    private static void DrawMeter(UiPainter painter, UiRect rect, float value, Bgra32 colour)
    {
        painter.FillRect(rect, Bgra32.FromRgba(255, 255, 255, 42));
        float fill = rect.Width * Math.Clamp(value, 0f, 1f);
        if (fill > 0f)
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, fill, rect.Height), colour);
        }
    }

    private static Bgra32 Text => Bgra32.FromRgb(245, 248, 250);
    private static Bgra32 Muted => Bgra32.FromRgb(202, 213, 220);
    private static Bgra32 Accent => Bgra32.FromRgb(240, 176, 84);
    private static Bgra32 Signal => Bgra32.FromRgb(140, 245, 200);
}

/// <summary>Everything the remaster HUD needs for one draw, collected without mutable renderer state.</summary>
internal readonly record struct RemasteredHudSnapshot(
    float HealthFraction,
    float HeadingRadians,
    WeaponType? Weapon,
    int RoundsInClip,
    int ReserveAmmo,
    HumanWeaponState WeaponState,
    string? ActionSprite,
    IReadOnlyList<MissionObjective> Objectives,
    bool SniperScoped);
