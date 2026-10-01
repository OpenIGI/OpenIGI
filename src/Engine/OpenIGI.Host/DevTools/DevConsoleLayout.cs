using OpenIGI.Presentation.Cameras;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using System.Globalization;

namespace OpenIGI.Host.DevTools;

/// <summary>Where one developer-tool panel sat, and whether it was showing.</summary>
/// <param name="Frame">The panel's rectangle in back-buffer pixels.</param>
/// <param name="IsOpen">Whether the panel was on screen.</param>
/// <param name="IsCollapsed">Whether only its title bar was showing.</param>
public readonly record struct PanelPlacement(UiRect Frame, bool IsOpen, bool IsCollapsed);

/// <summary>
/// Serialises where the developer tools' panels sit, so a reader's arrangement survives a restart.
/// </summary>
/// <remarks>
/// <para>
/// The whole layout is one string variable — <c>console:40,40,1200,620,1,0|cvars:…</c> — rather than
/// five numbers per panel. <c>settings.cfg</c> records every archived variable that differs from its
/// default, and thirty geometry numbers would bury the registry that <c>find</c> and <c>cvarlist</c>
/// exist to make searchable.
/// </para>
/// <para>
/// <b>Parsing never throws and never rejects the whole string.</b> A layout is written by a tool and
/// may be hand-edited, may come from an older build with different panels, and may name a panel this
/// build no longer has. Any segment that does not make sense is skipped and its panel falls back to
/// its default position; a reader who mangles one entry does not lose the rest of their arrangement.
/// </para>
/// </remarks>
public static class DevConsoleLayout
{
    /// <summary>Renders the placements of a set of named panels.</summary>
    /// <param name="panels">The panels, keyed by the name they are stored under.</param>
    /// <returns>The serialised layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="panels"/> is <see langword="null"/>.</exception>
    public static string Render(IReadOnlyDictionary<string, UiWindow> panels)
    {
        ArgumentNullException.ThrowIfNull(panels);

        List<string> parts = [];
        foreach ((string name, UiWindow window) in panels)
        {
            UiRect f = window.Frame;
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}:{Round(f.X)},{Round(f.Y)},{Round(f.Width)},{Round(f.Height)},{(window.IsOpen ? 1 : 0)},{(window.IsCollapsed ? 1 : 0)}"));
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join('|', parts);
    }

    /// <summary>Reads a serialised layout into placements keyed by panel name.</summary>
    /// <param name="text">The serialised layout, which may be empty, stale or malformed.</param>
    /// <returns>The placements that could be read; never <see langword="null"/>.</returns>
    public static Dictionary<string, PanelPlacement> Parse(string? text)
    {
        Dictionary<string, PanelPlacement> placements = new(StringComparer.OrdinalIgnoreCase);

        foreach (string segment in (text ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = segment.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string name = segment[..colon];
            string[] fields = segment[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 4)
            {
                continue;
            }

            if (!TryFloat(fields[0], out float x) ||
                !TryFloat(fields[1], out float y) ||
                !TryFloat(fields[2], out float w) ||
                !TryFloat(fields[3], out float h))
            {
                continue;
            }

            // A zero-sized panel is unusable and unrecoverable by dragging, so treat it as corrupt.
            if (w <= 1f || h <= 1f || !float.IsFinite(x) || !float.IsFinite(y))
            {
                continue;
            }

            bool open = fields.Length < 5 || fields[4] != "0";
            bool collapsed = fields.Length >= 6 && fields[5] == "1";

            placements[name] = new PanelPlacement(new UiRect(x, y, w, h), open, collapsed);
        }

        return placements;
    }

    /// <summary>Applies a stored placement to a window, keeping it on screen.</summary>
    /// <param name="window">The window to place.</param>
    /// <param name="placement">Where it was.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The size is clamped to the display as well as the position: a panel saved on a 4K screen and
    /// restored on a 1080p one would otherwise be larger than the screen it is now on, with its own
    /// resize grips beyond reach.
    /// </remarks>
    public static void Apply(UiWindow window, in PanelPlacement placement, UiVec2 viewport)
    {
        ArgumentNullException.ThrowIfNull(window);

        UiRect frame = placement.Frame;
        window.Frame = new UiRect(
            frame.X,
            frame.Y,
            MathF.Max(window.MinSize.X, MathF.Min(frame.Width, viewport.X)),
            MathF.Max(window.MinSize.Y, MathF.Min(frame.Height, viewport.Y)));

        window.IsOpen = placement.IsOpen;
        window.IsCollapsed = placement.IsCollapsed;
        window.ClampIntoView(viewport);
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Round(float value) =>
        MathF.Round(value).ToString(CultureInfo.InvariantCulture);
}
