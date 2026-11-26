using OpenIGI.Core.Collections;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>
/// The ground's height under the points of one water sheet, remembered from frame to frame.
/// </summary>
/// <remarks>
/// <para>
/// A sheet is cut up afresh every frame because its waves move, but the ground under it does not, and the cut lands on the
/// same points frame after frame: a new vertex is always the middle of an edge, so the points are the corners halved and
/// halved again, and only those near where the subdivision changes are new. Asking the terrain for each one each frame was
/// a delegate call and a tile search per vertex. A point is keyed by its exact coordinates, so a remembered height is the
/// one the ground gives for that point, bit for bit.
/// </para>
/// <para>Emptied when it grows past <see cref="Capacity"/> points, so a long session's wandering eye cannot grow it without end.</para>
/// </remarks>
internal sealed class Igi2WaterGroundMemo(Func<double, double, double?> ground, double centreX, double centreY)
{
    /// <summary>The most points remembered before the memo starts again.</summary>
    public const int Capacity = 1 << 17;

    private readonly PackedKeyIntMap _slots = new(4096);
    private readonly List<double> _heights = [];

    /// <summary>Whether this is the memo of a sheet at this centre over this ground.</summary>
    public bool IsFor(Func<double, double, double?> source, double x, double y) =>
        ReferenceEquals(source, ground) && x.Equals(centreX) && y.Equals(centreY);

    /// <summary>The ground's height under a point of the sheet, in world units; none where there is no ground.</summary>
    /// <param name="x">The point's offset east of the sheet's centre, in engine units.</param>
    /// <param name="y">Its offset north.</param>
    public double? At(float x, float y)
    {
        long key = ((long)BitConverter.SingleToInt32Bits(x) << 32) | (uint)BitConverter.SingleToInt32Bits(y);
        if (_slots.TryGetValue(key, out int slot))
        {
            double kept = _heights[slot];
            return double.IsNaN(kept) ? null : kept;
        }

        if (_heights.Count >= Capacity)
        {
            _slots.Clear();
            _heights.Clear();
        }

        double? height = ground(centreX + x, centreY + y);
        _slots.Set(key, _heights.Count);
        _heights.Add(height ?? double.NaN);
        return height;
    }
}
