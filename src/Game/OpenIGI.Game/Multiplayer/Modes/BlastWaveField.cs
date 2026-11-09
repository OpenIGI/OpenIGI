using System.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// How a bomb's blast reaches every place around it: not the straight line from the bomb, but the way a pressure wave goes --
/// out through the open air, round corners at a cost, through doorways and windows, and almost not at all through walls.
/// </summary>
/// <remarks>
/// <para>
/// The space round the bomb is a grid of cells (<see cref="CellMetres"/> a side, coarser for a bigger blast so the grid's
/// cost stays the same). Between neighbouring cells the level's
/// collision is cast once: open, or a wall. A wave is then spread from the bomb over the open steps -- a Dijkstra walk over
/// the cells, diagonal steps allowed where both their sides are open, so the open air reads within a few percent of the
/// straight-line distance -- and every cell keeps how far the wave had to travel to reach it. A wall can still be crossed,
/// at <see cref="WallPenaltyMetres"/> of extra travel, which is what lets a thin wall muffle the blast rather than stop it
/// dead.
/// </para>
/// <para>
/// The distance is then made <em>effective</em>: the detour the wave took beyond the straight line is counted again
/// (<see cref="DetourWeight"/>), because a wave that turns a corner sheds energy into the corner as well as travelling
/// further. Standing round a corner from the bomb is worth more than the metres alone say; standing behind a wall worth a
/// great deal more. This is the shape CS2's July 2026 bomb took (a precomputed propagation, corners dissipating, walls
/// stifling, the damage arriving as an expanding front); the model here is the port's own.
/// </para>
/// <para>
/// Built a slice at a time (<see cref="Advance"/>), from the plant until the blast, so the server's frame never stalls for
/// it -- a browser-hosted match included, which has no thread to give it. Every cast is the level's own collision, so a
/// door left open lets the wave through and a closed one does not, as they stood when the bomb was planted.
/// </para>
/// </remarks>
public sealed class BlastWaveField
{
    /// <summary>The finest a cell's edge gets, in metres.</summary>
    public const double FinestCellMetres = 1.5;

    /// <summary>How many cells span the field's radius across: a bigger blast has bigger cells, not more of them.</summary>
    public const double CellsAcrossRadius = 36.0;

    /// <summary>How far below the bomb the field reaches, as a share of its radius.</summary>
    public const double BelowShare = 0.1;

    /// <summary>How far above it, as a share of its radius.</summary>
    public const double AboveShare = 0.2;

    /// <summary>The extra travel a wall costs the wave, in metres: enough that a wall all but stops it.</summary>
    public const double WallPenaltyMetres = 28.0;

    /// <summary>How much of a detour beyond the straight line counts again, for what corners shed.</summary>
    public const double DetourWeight = 1.25;

    private const float OpenStep = 0f;

    // How short of a point a hit may be and still not hide it: the surface the point is on, a pane or a body.
    private const double SurfaceSpareUnits = 0.35 * WorldUnits.PerMetre;

    private readonly WorldRayCast _cast;
    private readonly Vector3d _bomb;
    private readonly Vector3d _origin;
    private readonly int _nx;
    private readonly int _ny;
    private readonly int _nz;
    private readonly double _cell;
    private readonly double _cellMetres;
    private readonly double _reachMetres;

    // Whether the step from each cell to its +X, +Y, +Z neighbour is open: 1 open, 0 wall; unknown until cast.
    private readonly byte[] _open;
    private readonly float[] _travel;
    private int _castCursor;
    private bool _spread;

    /// <summary>Initializes a new instance of the <see cref="BlastWaveField"/> class.</summary>
    /// <param name="cast">The level's collision.</param>
    /// <param name="bombUnits">Where the bomb is, in world units.</param>
    /// <param name="reachMetres">How far round the bomb the field reaches across: the farthest anything it does can matter.</param>
    public BlastWaveField(WorldRayCast cast, Vector3d bombUnits, double reachMetres = 45.0)
    {
        ArgumentNullException.ThrowIfNull(cast);
        _cast = cast;

        // A little above where it lies, so the wave starts in the air and not in the floor.
        _bomb = bombUnits + new Vector3d(0.0, 0.0, 0.4 * WorldUnits.PerMetre);
        _reachMetres = Math.Max(reachMetres, 10.0);
        _cellMetres = Math.Max(FinestCellMetres, _reachMetres / CellsAcrossRadius);
        _cell = _cellMetres * WorldUnits.PerMetre;
        double below = Math.Max(6.0, _reachMetres * BelowShare);
        double above = Math.Max(12.0, _reachMetres * AboveShare);
        _nx = (int)Math.Ceiling(2.0 * _reachMetres / _cellMetres) + 1;
        _ny = _nx;
        _nz = (int)Math.Ceiling((below + above) / _cellMetres) + 1;
        _origin = _bomb - new Vector3d(_reachMetres, _reachMetres, below) * WorldUnits.PerMetre;
        _open = new byte[_nx * _ny * _nz * 3];
        _travel = new float[_nx * _ny * _nz];
    }

    /// <summary>Gets a cell's edge, in metres.</summary>
    public double CellMetres => _cellMetres;

    /// <summary>Gets a value indicating whether the field is whole and may be read.</summary>
    public bool IsReady => _spread;

    /// <summary>Gets where the bomb is, in world units.</summary>
    public Vector3d Bomb => _bomb;

    /// <summary>Gets how far through its casting the field is, nought to one.</summary>
    public double Progress => _spread ? 1.0 : _castCursor / (double)_open.Length;

    /// <summary>Builds some more of the field.</summary>
    /// <param name="budget">How long it may take this time; <see cref="TimeSpan.MaxValue"/> to finish it.</param>
    public void Advance(TimeSpan budget)
    {
        if (_spread)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        long allowed = budget == TimeSpan.MaxValue ? long.MaxValue : (long)(budget.TotalSeconds * Stopwatch.Frequency);
        while (_castCursor < _open.Length)
        {
            // A stride of casts between clock reads: a cast is microseconds, the clock nearly as dear.
            for (int i = 0; i < 64 && _castCursor < _open.Length; i++, _castCursor++)
            {
                _open[_castCursor] = CastStep(_castCursor);
            }

            if (Stopwatch.GetTimestamp() - started > allowed)
            {
                return;
            }
        }

        Spread();
        _spread = true;
    }

    /// <summary>How far the blast effectively travels to reach a point: the wave's path, walls and corners counted.</summary>
    /// <param name="atUnits">The point, in world units.</param>
    /// <returns>The effective distance in metres; the straight line where the field does not reach.</returns>
    public double EffectiveMetres(Vector3d atUnits)
    {
        double straight = (atUnits - _bomb).Length / WorldUnits.PerMetre;
        if (!_spread)
        {
            return straight;
        }

        Vector3d local = (atUnits - _origin) / _cell;
        int x0 = (int)Math.Floor(local.X);
        int y0 = (int)Math.Floor(local.Y);
        int z0 = (int)Math.Floor(local.Z);
        if (x0 < 0 || y0 < 0 || z0 < 0 || x0 + 1 >= _nx || y0 + 1 >= _ny || z0 + 1 >= _nz)
        {
            // Beyond the field: past where the bomb can do anything.
            return Math.Max(straight, _reachMetres) * (1.0 + DetourWeight);
        }

        // In plain sight of the bomb the wave comes the straight way, whatever the grid's steps would say.
        if (!Blocked(_bomb, atUnits, SurfaceSpareUnits))
        {
            return straight;
        }

        // The wave reaches the point from whichever of its eight surrounding cells it can see, the nearest way.
        double best = double.MaxValue;
        double blind = double.MaxValue;
        for (int c = 0; c < 8; c++)
        {
            int x = x0 + (c & 1);
            int y = y0 + ((c >> 1) & 1);
            int z = z0 + ((c >> 2) & 1);
            float stepped = _travel[Index(x, y, z)];
            if (stepped == float.MaxValue)
            {
                continue;
            }

            // The grid's 26 steps run a few percent long off its axes and diagonals; taken back by how much longer the
            // stepped way to the cell would be in the open, so only the detour the walls force is left.
            Vector3d centre = Centre(x, y, z);
            double travel = stepped / StairFactor(centre - _bomb);
            double hop = (centre - atUnits).Length / WorldUnits.PerMetre;
            double through = travel + hop;
            if (through < best && !Blocked(centre, atUnits, SurfaceSpareUnits))
            {
                best = through;
            }

            blind = Math.Min(blind, through + WallPenaltyMetres);
        }

        double path = best < double.MaxValue ? best : blind;
        if (path == double.MaxValue)
        {
            return Math.Max(straight, _reachMetres) * (1.0 + DetourWeight);
        }

        path = Math.Max(path, straight);
        return path + ((path - straight) * DetourWeight);
    }

    // How much longer the grid's straight, face-diagonal and body-diagonal steps make the way along an offset than the line.
    private static double StairFactor(Vector3d offset)
    {
        double a = Math.Abs(offset.X);
        double b = Math.Abs(offset.Y);
        double c = Math.Abs(offset.Z);
        if (a < b)
        {
            (a, b) = (b, a);
        }

        if (b < c)
        {
            (b, c) = (c, b);
        }

        if (a < b)
        {
            (a, b) = (b, a);
        }

        double line = Math.Sqrt((a * a) + (b * b) + (c * c));
        if (line < 1e-6)
        {
            return 1.0;
        }

        double stepped = (a - b) + ((b - c) * Math.Sqrt(2.0)) + (c * Math.Sqrt(3.0));
        return stepped / line;
    }

    // One step's cast: the cursor names a cell and an axis.
    private byte CastStep(int cursor)
    {
        int cell = cursor / 3;
        int axis = cursor % 3;
        int x = cell % _nx;
        int y = (cell / _nx) % _ny;
        int z = cell / (_nx * _ny);
        int nx = x + (axis == 0 ? 1 : 0);
        int ny = y + (axis == 1 ? 1 : 0);
        int nz = z + (axis == 2 ? 1 : 0);
        if (nx >= _nx || ny >= _ny || nz >= _nz)
        {
            return 0;
        }

        Vector3d a = Centre(x, y, z);
        Vector3d b = Centre(nx, ny, nz);
        return Blocked(a, b) || Blocked(b, a) ? (byte)0 : (byte)1;
    }

    /// <summary>Whether the wave is stopped between two points: by anything but glass and open-work, which it goes through.</summary>
    /// <param name="from">One end, world units.</param>
    /// <param name="to">The other.</param>
    /// <param name="spareUnits">How short of the far end a hit may be and still not count: the far end's own surface.</param>
    /// <returns>Whether it is stopped.</returns>
    public bool Blocked(Vector3d from, Vector3d to, double spareUnits = 0.0)
    {
        Vector3d direction = to - from;
        double length = direction.Length;
        if (length < 1e-6)
        {
            return false;
        }

        direction /= length;
        Vector3d start = from;
        for (int i = 0; i < 4; i++)
        {
            if (!_cast(start, to, out WorldRayHit hit))
            {
                return false;
            }

            double reached = Vector3d.Dot(hit.Position - from, direction);
            if (reached >= length - spareUnits)
            {
                return false;
            }

            if (!GameMaterial.IsBlastTransparent(hit.Material))
            {
                return true;
            }

            start = hit.Position + (direction * 8.0);
            if (Vector3d.Dot(to - start, direction) <= 0.0)
            {
                return false;
            }
        }

        return false;
    }

    // The wave spread from the bomb: Dijkstra over the cells, straight and diagonal steps, walls at a cost.
    private void Spread()
    {
        Array.Fill(_travel, float.MaxValue);
        var queue = new PriorityQueue<int, float>();

        // The cells round the bomb it can see start the wave at their straight distance.
        Vector3d local = (_bomb - _origin) / _cell;
        int bx = (int)Math.Floor(local.X);
        int by = (int)Math.Floor(local.Y);
        int bz = (int)Math.Floor(local.Z);
        for (int dz = -1; dz <= 2; dz++)
        {
            for (int dy = -1; dy <= 2; dy++)
            {
                for (int dx = -1; dx <= 2; dx++)
                {
                    int x = bx + dx, y = by + dy, z = bz + dz;
                    if (!Inside(x, y, z))
                    {
                        continue;
                    }

                    Vector3d centre = Centre(x, y, z);
                    float start = (float)((centre - _bomb).Length / WorldUnits.PerMetre);
                    if (Blocked(_bomb, centre))
                    {
                        start += (float)WallPenaltyMetres;
                    }

                    int index = Index(x, y, z);
                    if (start < _travel[index])
                    {
                        _travel[index] = start;
                        queue.Enqueue(index, start);
                    }
                }
            }
        }

        float straightStep = (float)_cellMetres;
        float diagonal2 = (float)(_cellMetres * Math.Sqrt(2.0));
        float diagonal3 = (float)(_cellMetres * Math.Sqrt(3.0));
        while (queue.TryDequeue(out int index, out float travel))
        {
            if (travel > _travel[index])
            {
                continue;
            }

            int x = index % _nx;
            int y = (index / _nx) % _ny;
            int z = index / (_nx * _ny);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int axes = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                        if (axes == 0 || !Inside(x + dx, y + dy, z + dz))
                        {
                            continue;
                        }

                        bool open = StepOpen(x, y, z, dx, dy, dz);
                        if (!open && axes > 1)
                        {
                            // Across a wall only straight on: a diagonal through one is the straight step's job.
                            continue;
                        }

                        float step = axes == 1 ? straightStep : axes == 2 ? diagonal2 : diagonal3;
                        float next = travel + step + (open ? OpenStep : (float)WallPenaltyMetres);
                        int to = Index(x + dx, y + dy, z + dz);
                        if (next < _travel[to] && next < _reachMetres * 4.0)
                        {
                            _travel[to] = next;
                            queue.Enqueue(to, next);
                        }
                    }
                }
            }
        }
    }

    // Whether a step to a neighbour is open: a straight step by its cast, a diagonal one when every way round it along the
    // axes is open, which a corner's edge is not.
    private bool StepOpen(int x, int y, int z, int dx, int dy, int dz)
    {
        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) == 1)
        {
            return AxisOpen(x, y, z, dx, dy, dz);
        }

        // Every ordering of the axes the step moves along must be open.
        Span<int> moves = stackalloc int[3];
        int count = 0;
        if (dx != 0)
        {
            moves[count++] = 0;
        }

        if (dy != 0)
        {
            moves[count++] = 1;
        }

        if (dz != 0)
        {
            moves[count++] = 2;
        }

        return count == 2
            ? Path(x, y, z, dx, dy, dz, moves[0], moves[1], -1) && Path(x, y, z, dx, dy, dz, moves[1], moves[0], -1)
            : Path(x, y, z, dx, dy, dz, 0, 1, 2) && Path(x, y, z, dx, dy, dz, 2, 1, 0) && Path(x, y, z, dx, dy, dz, 1, 0, 2);
    }

    private bool Path(int x, int y, int z, int dx, int dy, int dz, int first, int second, int third)
    {
        foreach (int axis in (ReadOnlySpan<int>)[first, second, third])
        {
            if (axis < 0)
            {
                continue;
            }

            int sx = axis == 0 ? dx : 0, sy = axis == 1 ? dy : 0, sz = axis == 2 ? dz : 0;
            if (!AxisOpen(x, y, z, sx, sy, sz))
            {
                return false;
            }

            x += sx;
            y += sy;
            z += sz;
        }

        return true;
    }

    private bool AxisOpen(int x, int y, int z, int dx, int dy, int dz)
    {
        // A step is stored on its lower cell, toward +axis.
        int axis = dx != 0 ? 0 : dy != 0 ? 1 : 2;
        int step = dx + dy + dz;
        int lx = step < 0 ? x + dx : x;
        int ly = step < 0 ? y + dy : y;
        int lz = step < 0 ? z + dz : z;
        return Inside(lx, ly, lz) && _open[(Index(lx, ly, lz) * 3) + axis] == 1;
    }

    private bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < _nx && y < _ny && z < _nz;

    private int Index(int x, int y, int z) => x + (_nx * (y + (_ny * z)));

    private Vector3d Centre(int x, int y, int z) => _origin + (new Vector3d(x, y, z) * _cell);
}
