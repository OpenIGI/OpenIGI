using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The height of whatever the rain would land on, sampled on a grid that follows the eye.</summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Rain should stop where it meets the world, so that it lands on roofs,
/// steps, crates and the tops of walls. This port had one flat plane taken from the player's feet,
/// so rain fell through every raised surface and every splash sat at one height. On a level of
/// stacked concrete that is not a subtle difference: the ground floor rains and nothing else does.
/// </para>
/// <para>
/// <b>Why a cache and not a ray per drop.</b> A ray per drop per tick is thousands of traces a
/// second through the object index, which is the reason the plane was there in the first place. But
/// the answer a drop needs — how high is the world under this square metre — barely changes, and it
/// is the same answer for every drop above that square. So the grid is sampled a few cells a tick,
/// round-robin, and every drop reads it for free. A full sweep of the grid costs about as much as
/// one drop's worth of tracing did, and it takes under a second, which is far quicker than the
/// player can walk out of it.
/// </para>
/// <para>
/// <b>It follows the eye in whole cells.</b> Re-centring on a fractional position would shift every
/// cached height sideways under the drops; snapping to the lattice means a step only ever scrolls
/// whole cells in and out, and what scrolls in is marked unknown rather than being wrong.
/// </para>
/// </remarks>
internal sealed class SurfaceHeights
{
    /// <summary>Cells across the grid; the reach is this times <see cref="CellMetres"/>, halved.</summary>
    public const int Cells = 48;

    /// <summary>How much ground one cell stands for, in metres.</summary>
    public const float CellMetres = 1.25f;

    /// <summary>How many cells are re-traced each tick.</summary>
    /// <remarks>
    /// The whole grid over about twenty ticks. Higher is fresher and costs linearly; the number that
    /// matters is that a surface which appears -- a door opening, a lift arriving -- is rained on
    /// within a second, and nobody notices a second.
    /// </remarks>
    public const int CellsPerTick = 128;

    private readonly double[] _heights = new double[Cells * Cells];
    private int _originX;
    private int _originY;
    private int _cursor;
    private bool _centred;

    /// <summary>Gets the height under a point, or negative infinity where nothing is known yet.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="y">World Y in metres.</param>
    /// <returns>The world height in metres, or negative infinity.</returns>
    public double At(double x, double y)
    {
        int cellX = (int)Math.Floor(x / CellMetres) - _originX;
        int cellY = (int)Math.Floor(y / CellMetres) - _originY;
        if (!_centred || cellX < 0 || cellY < 0 || cellX >= Cells || cellY >= Cells)
        {
            return double.NegativeInfinity;
        }

        return _heights[(cellY * Cells) + cellX];
    }

    /// <summary>Re-centres on the eye and re-traces this tick's share of the grid.</summary>
    /// <param name="eyeMetres">Where the eye is now, in world metres.</param>
    /// <param name="cast">The world trace, in world units.</param>
    /// <param name="unitsPerMetre">How many world units make a metre.</param>
    /// <param name="fallbackMetres">The height to assume where the trace finds nothing.</param>
    public void Tick(Vector3d eyeMetres, WorldRayCast cast, double unitsPerMetre, double fallbackMetres)
    {
        int wantedX = (int)Math.Floor(eyeMetres.X / CellMetres) - (Cells / 2);
        int wantedY = (int)Math.Floor(eyeMetres.Y / CellMetres) - (Cells / 2);

        if (!_centred || wantedX != _originX || wantedY != _originY)
        {
            // Anything the move brought in is unknown until it is traced; anything that stayed keeps
            // its height, because the world under it did not move.
            Shift(wantedX - _originX, wantedY - _originY, _centred);
            _originX = wantedX;
            _originY = wantedY;
            _centred = true;
        }

        // From well above the eye to well below it. The span has to clear the tallest thing rain can
        // land on from where the player stands and still reach a floor a storey down.
        double from = (eyeMetres.Z + 40.0) * unitsPerMetre;
        double to = (eyeMetres.Z - 60.0) * unitsPerMetre;

        for (int n = 0; n < CellsPerTick; n++)
        {
            int index = _cursor;
            _cursor = (_cursor + 1) % (Cells * Cells);

            double x = (_originX + (index % Cells) + 0.5) * CellMetres * unitsPerMetre;
            double y = (_originY + (index / Cells) + 0.5) * CellMetres * unitsPerMetre;

            _heights[index] = cast(new Vector3d(x, y, from), new Vector3d(x, y, to), out WorldRayHit hit)
                ? hit.Position.Z / unitsPerMetre
                : fallbackMetres;
        }
    }

    /// <summary>Forgets everything, as changing level does.</summary>
    public void Clear()
    {
        _centred = false;
        _cursor = 0;
    }

    private void Shift(int byX, int byY, bool knew)
    {
        if (!knew || Math.Abs(byX) >= Cells || Math.Abs(byY) >= Cells)
        {
            Array.Fill(_heights, double.NegativeInfinity);
            return;
        }

        // Copied in the direction that does not overwrite a cell before it has been read.
        int stepY = byY > 0 ? 1 : -1;
        int stepX = byX > 0 ? 1 : -1;
        int firstY = byY > 0 ? 0 : Cells - 1;
        int firstX = byX > 0 ? 0 : Cells - 1;

        for (int i = 0; i < Cells; i++)
        {
            int destY = firstY + (i * stepY);
            int sourceY = destY + byY;
            for (int j = 0; j < Cells; j++)
            {
                int destX = firstX + (j * stepX);
                int sourceX = destX + byX;
                _heights[(destY * Cells) + destX] =
                    sourceX >= 0 && sourceX < Cells && sourceY >= 0 && sourceY < Cells
                        ? _heights[(sourceY * Cells) + sourceX]
                        : double.NegativeInfinity;
            }
        }
    }
}
