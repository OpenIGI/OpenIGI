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
        DrawCrosshair(painter, context, snapshot.Weapon);
        DrawInteraction(painter, context, body, snapshot.ActionSprite);
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
        float height = context.Px(64f);
        float x = context.SafeRect.X + context.Px(34f);
        float y = context.SafeRect.Bottom - context.Px(38f) - height;
        float fraction = Math.Clamp(health, 0f, 1f);
        Bgra32 healthColour = RemasteredHudPresentation.HealthColour(fraction);

        painter.DrawText(body, "HEALTH", new UiVec2(x, y), context.Px(14f), Muted);
        painter.DrawText(
            body,
            $"{(int)MathF.Round(fraction * 100f):00}",
            new UiVec2(x, y + context.Px(18f)),
            context.Px(42f),
            healthColour);

        float barX = x + context.Px(82f);
        float barY = y + context.Px(49f);
        float barWidth = width - context.Px(82f);
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
        float y = context.SafeRect.Bottom - context.Px(120f);
        float detailsWidth = context.Px(280f);
        float textX = right - detailsWidth;
        painter.FillRect(new UiRect(textX, y + context.Px(4f), context.Px(3f), context.Px(45f)), Accent);
        painter.DrawText(body, RemasteredHudPresentation.WeaponClass(weapon), new UiVec2(textX + context.Px(14f), y), context.Px(14f), Muted);
        painter.DrawText(body, weapon.DisplayName.ToUpperInvariant(), new UiVec2(textX + context.Px(14f), y + context.Px(18f)), context.Px(28f), Text);

        string state = RemasteredHudPresentation.WeaponStateLabel(snapshot.WeaponState);
        if (state.Length > 0)
        {
            painter.DrawText(body, state, new UiVec2(textX + context.Px(14f), y + context.Px(52f)), context.Px(14f), Accent);
        }

        bool countsAmmo = weapon.AmmoDisplay != AmmoDisplayType.None;
        string magazine = countsAmmo ? snapshot.RoundsInClip.ToString("00", CultureInfo.InvariantCulture) : "--";
        string reserve = countsAmmo ? snapshot.ReserveAmmo.ToString("000", CultureInfo.InvariantCulture) : "--";
        float ammoSize = context.Px(40f);
        UiVec2 reserveSize = body.Measure(reserve, ammoSize);
        float reserveX = right - reserveSize.X;
        painter.DrawText(body, reserve, new UiVec2(reserveX, y + context.Px(73f)), ammoSize, Muted);
        painter.DrawText(body, "/", new UiVec2(reserveX - context.Px(24f), y + context.Px(80f)), context.Px(21f), Muted);
        UiVec2 magazineSize = body.Measure(magazine, ammoSize);
        painter.DrawText(body, magazine, new UiVec2(reserveX - context.Px(36f) - magazineSize.X, y + context.Px(73f)), ammoSize, Text);
    }

    private static void DrawCrosshair(UiPainter painter, LayoutContext context, WeaponType? weapon)
    {
        if (weapon?.SightDisplay != SightDisplayType.Crosshair)
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
    IReadOnlyList<MissionObjective> Objectives);
