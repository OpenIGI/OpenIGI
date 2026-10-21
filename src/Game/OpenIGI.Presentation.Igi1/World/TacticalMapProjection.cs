using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// The tactical computer's straight-down projection: engine units on the ground to pixels on the map
/// and back, at the fixed altitude the map is read from.
/// </summary>
/// <remarks>
/// One copy of the arithmetic for the input (the cursor-anchored zoom and the drag), the overlay (every
/// marker and the grid) and the readouts (the scale ratio and bar), so they cannot disagree about where
/// a point is.
/// </remarks>
internal static class TacticalMapProjection
{
    /// <summary>How high above the ground the map is read from, in metres.</summary>
    /// <remarks>The retail altitude, kept: it is what the authored field-of-view range is calibrated to.</remarks>
    public const double AltitudeMetres = 900.0;

    /// <summary>Engine units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The widest the map opens out to, in radians — the retail overview.</summary>
    private const float WidestFieldOfView = 0.7853982f;

    /// <summary>The closest the map goes in, in radians.</summary>
    /// <remarks>
    /// About forty metres of ground across the frame, which is a building and its yard. The retail
    /// closest was a tenth of this and framed roughly a doorway, which is past the point where an
    /// overhead view of a level tells anyone anything.
    /// </remarks>
    private const float NarrowestFieldOfView = 0.0455f;

    /// <summary>Gets the field of view a zoom position asks for, in radians.</summary>
    /// <remarks>
    /// Geometric rather than linear across the range, for the reason
    /// <see cref="MapComputerCamera"/> interpolates one geometrically: zoom is perceived in ratios, and a
    /// linear ramp over a range this wide races at one end and crawls at the other.
    /// </remarks>
    public static float FieldOfViewFor(float zoom) =>
        WidestFieldOfView * MathF.Pow(NarrowestFieldOfView / WidestFieldOfView, Math.Clamp(zoom, 0f, 1f));

    /// <summary>Gets how many engine units one map pixel spans, across and down.</summary>
    public static (double X, double Y) UnitsPerPixel(float fieldOfView, Viewport viewport)
    {
        double altitudeUnits = AltitudeMetres * UnitsPerMetre;
        double halfHeight = altitudeUnits * Math.Tan(fieldOfView * 0.5);
        double halfWidth = halfHeight * viewport.Width / Math.Max(1.0, viewport.Height);
        return (2.0 * halfWidth / Math.Max(1, viewport.Width), 2.0 * halfHeight / Math.Max(1, viewport.Height));
    }

    /// <summary>Projects a ground position to the map; <see langword="false"/> when it falls outside the viewport.</summary>
    public static bool TryProject(
        Vector3d worldUnits,
        Vector3d centreUnits,
        float fieldOfView,
        Viewport viewport,
        out float x,
        out float y)
    {
        (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, viewport);
        x = viewport.X + (viewport.Width * 0.5f) + (float)((worldUnits.X - centreUnits.X) / unitsPerPixelX);
        y = viewport.Y + (viewport.Height * 0.5f) - (float)((worldUnits.Y - centreUnits.Y) / unitsPerPixelY);
        return x >= viewport.X && x <= viewport.X + viewport.Width
            && y >= viewport.Y && y <= viewport.Y + viewport.Height;
    }

    /// <summary>Takes a map pixel back to the ground position under it, at the centre's height.</summary>
    public static Vector3d Unproject(
        float x,
        float y,
        Vector3d centreUnits,
        float fieldOfView,
        Viewport viewport)
    {
        (double unitsPerPixelX, double unitsPerPixelY) = UnitsPerPixel(fieldOfView, viewport);
        return new Vector3d(
            centreUnits.X + ((x - viewport.X - (viewport.Width * 0.5)) * unitsPerPixelX),
            centreUnits.Y - ((y - viewport.Y - (viewport.Height * 0.5)) * unitsPerPixelY),
            centreUnits.Z);
    }

    /// <summary>Rounds a span up to the next 1, 2 or 5 times a power of ten.</summary>
    /// <param name="value">The span to round.</param>
    /// <returns>The nearest round number at or above it.</returns>
    public static double NiceStep(double value)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return 1.0;
        }

        double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
        double normalised = value / magnitude;
        double step = normalised <= 1.0 ? 1.0 : normalised <= 2.0 ? 2.0 : normalised <= 5.0 ? 5.0 : 10.0;
        return step * magnitude;
    }
}
