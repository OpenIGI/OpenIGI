using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// A race against a ghost, in the played title's look and kept small at the top of the screen: the ghost's mark and how far ahead
/// of it or behind it the player is now, in the ally's ink ahead and the danger's behind; the splits so far listed under it; and
/// the newest split held up larger as it is reached, fading.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // How many splits are listed under the timer, the newest last.
    private const int RaceSplitsShown = 4;

    // Within this of the ghost, in seconds, the player is level with it: the gap shows as 0.0.
    private const double Level = 0.05;

    // The ghost's mark: none nearer than the first, in metres, whole from the second; its diamond, in layout pixels.
    private const double GhostMarkFrom = 12.0;
    private const double GhostMarkWhole = 18.0;
    private const float GhostMarkDiamond = 7f;

    /// <summary>How strongly the ghost's mark is drawn at a distance and presence of the ghost, nought to one.</summary>
    /// <param name="distanceMetres">How far off the ghost is.</param>
    /// <param name="presence">How much of the ghost is there.</param>
    /// <returns>The mark's opacity: nought close to, where the ghost's own contour is enough, rising as it draws away.</returns>
    public static float GhostMarkOpacity(double distanceMetres, float presence) =>
        (float)Math.Clamp((distanceMetres - GhostMarkFrom) / (GhostMarkWhole - GhostMarkFrom), 0.0, 1.0) * Math.Clamp(presence, 0f, 1f);

    /// <inheritdoc/>
    /// <remarks>
    /// Kept spare, in the ghost's own green: a small hollow diamond over its head, the word and how far over that, through walls,
    /// pinned to the screen's edge with an arrow when it is out of view. Close to, none: the ghost's own contour is enough.
    /// </remarks>
    public void DrawRaceGhostMarker(
        SpriteBatch batch,
        RaceGhostMarker marker,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(marker);
        WorldCanvas canvas = new(marker.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
        float opacity = GhostMarkOpacity(canvas.DistanceMetres, marker.Presence);
        if (opacity <= 0f || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        bool inView = canvas.PinToScreen(o.Px(48f), out float x, out float y);
        var centre = new UiVec2(x, y);
        float radius = o.Px(GhostMarkDiamond);
        Bgra32 green = HudOverlay.Fade(GhostPalette.Interface, opacity);
        o.Diamond(centre, radius + o.Px(2f), HudOverlay.Fade(Bgra32.FromRgba(0, 0, 0, 160), opacity));
        o.Diamond(centre, radius, green);
        o.Diamond(centre, radius - MathF.Max(1.5f, o.Px(2f)), HudOverlay.Fade(Bgra32.FromRgba(0, 0, 0, 200), opacity));
        if (!inView)
        {
            o.EdgeArrow(centre, radius + o.Px(5f), new UiVec2(x - (screenWidth * 0.5f), y - (screenHeight * 0.5f)), green);
        }

        // Over the diamond while the ghost is in view, so nothing of the mark stands on the body under it; pinned to the edge,
        // under it, where the edge's room is.
        float captionHeight = o.Text.LineHeightAt(o.Px(HudOverlay.Small));
        float captionTop = inView ? centre.Y - radius - o.Px(4f) - captionHeight : centre.Y + radius + o.Px(4f);
        o.Caption(centre.X, captionTop, "GHOST", HudOverlay.Distance(canvas.DistanceMetres), GhostPalette.Interface, opacity, HudOverlay.Small);
    }

    /// <inheritdoc/>
    public void DrawRace(SpriteBatch batch, RaceHud race, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(race);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float body = o.Px(HudOverlay.Body);
        float large = o.Px(HudOverlay.Large);
        float small = o.Px(HudOverlay.Small);
        float centre = o.Safe.X + (o.Safe.Width * 0.5f);
        float top = o.Safe.Y + o.Px(20f);

        // The timer: the ghost's mark, and the gap.
        // Level with the ghost -- the gap rounding to nought, as at the start -- is neither ahead nor behind.
        string gap = race.DeltaSeconds is { } delta ? Signed(delta) : "--.-";
        Bgra32 ink = race.DeltaSeconds is not { } now || Math.Abs(now) < Level ? o.Quiet : now < 0.0 ? o.Ink.Ally : o.Ink.Danger;
        float tagWidth = o.Text.MeasureAt("GHOST", small) + o.Px(10f);
        float width = tagWidth + o.Px(14f) + MathF.Max(o.Text.MeasureAt("+00:00.0", large), o.Px(150f)) + o.Px(28f);
        float height = o.Text.LineHeightAt(large) + o.Px(16f);
        var plate = new UiRect(centre - (width * 0.5f), top, width, height);
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 160));
        float lineY = plate.Y + ((plate.Height - o.Text.LineHeightAt(large)) * 0.5f);
        float tagRight = o.Tag("GHOST", plate.X + o.Px(14f), plate.Y + ((plate.Height - o.Text.LineHeightAt(small) - o.Px(4f)) * 0.5f), GhostPalette.Interface);
        o.Text.DrawCentredAt(gap, (tagRight + plate.Right) * 0.5f, lineY, large, ink, shadow: true);

        // The splits so far, the newest last.
        float y = plate.Bottom + o.Px(6f);
        int first = Math.Max(0, race.Splits.Count - RaceSplitsShown);
        for (int i = first; i < race.Splits.Count; i++)
        {
            RaceSplit split = race.Splits[i];
            string line = $"{OverlayText.Capitals(split.Label)}  {Clock(split.Seconds)}  {Signed(split.DeltaSeconds)}";
            o.Text.DrawCentredAt(line, centre, y, small, split.DeltaSeconds <= 0.0 ? o.Ink.Ally : o.Ink.Danger, shadow: true);
            y += o.Text.LineHeightAt(small) + o.Px(2f);
        }

        // The newest split, held up as it is reached.
        if (race.LatestShown > 0f && race.Splits.Count > 0)
        {
            RaceSplit latest = race.Splits[^1];
            o.Painter.PushOpacity(Math.Clamp(race.LatestShown, 0f, 1f));
            float midY = o.Safe.Y + (o.Safe.Height * 0.3f);
            o.Text.DrawCentredAt(OverlayText.Capitals(latest.Label), centre, midY, body, o.Ink.Highlight, shadow: true);
            o.Text.DrawCentredAt(
                Signed(latest.DeltaSeconds),
                centre,
                midY + o.Text.LineHeightAt(body) + o.Px(4f),
                o.Px(HudOverlay.Huge),
                latest.DeltaSeconds <= 0.0 ? o.Ink.Ally : o.Ink.Danger,
                shadow: true);
            o.Painter.PopOpacity();
        }

        static string Signed(double seconds)
        {
            double size = Math.Abs(seconds);
            string sign = size < Level ? string.Empty : seconds < 0.0 ? "-" : "+";
            return size >= 60.0
                ? string.Create(CultureInfo.InvariantCulture, $"{sign}{(int)(size / 60.0)}:{size % 60.0:00.0}")
                : string.Create(CultureInfo.InvariantCulture, $"{sign}{size:0.0}");
        }

        static string Clock(double seconds)
        {
            double s = Math.Max(0.0, seconds);
            return string.Create(CultureInfo.InvariantCulture, $"{(int)(s / 60.0):00}:{s % 60.0:00.0}");
        }
    }
}
