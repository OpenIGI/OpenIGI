using OpenIGI.Core.Collections;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>What a navigation graph covers and how closely: a square of ground, and how its nodes may be linked.</summary>
/// <param name="CentreMetres">The square's middle, in world metres (only X and Y are used).</param>
/// <param name="SideMetres">The square's width.</param>
/// <param name="MaximumNodes">The most nodes the graph may have; the spacing widens to keep within it.</param>
public sealed record NavGraphRequest(Vector3d CentreMetres, double SideMetres = 200, int MaximumNodes = 256)
{
    /// <summary>Gets the closest the nodes may stand, in metres: the shipped graphs link nodes a median 7 m apart.</summary>
    public double MinimumSpacingMetres { get; init; } = 6;

    /// <summary>Gets the steepest a link may climb, rise over run.</summary>
    public double MaximumSlope { get; init; } = 0.7;

    /// <summary>
    /// Gets how far the ground may stand off a link's straight line, in metres: an <c>AIGraph</c>'s "Link maximum distance to
    /// ground", 1 in the shipped levels.
    /// </summary>
    public double MaximumGroundGapMetres { get; init; } = 1;
}

/// <summary>A navigation graph laid out over ground: nodes in world metres, numbered from 1, and the links between them.</summary>
/// <param name="Nodes">The nodes' ids and world positions, on the ground.</param>
/// <param name="Links">The links, as node-id pairs.</param>
/// <param name="SpacingMetres">How far apart the grid's nodes stand.</param>
public sealed record NavGraphLayout(IReadOnlyList<(int Id, Vector3d PositionMetres)> Nodes, IReadOnlyList<(int A, int B)> Links, double SpacingMetres)
{
    /// <summary>
    /// Lays a graph over the ground: a grid of nodes where there is ground, each linked to its eight neighbours where the
    /// climb is gentle and the ground keeps to the line between them, then only the largest piece that links together.
    /// </summary>
    /// <param name="request">The square and its limits.</param>
    /// <param name="ground">The ground's height at a point, in metres, or null where there is none.</param>
    /// <returns>The layout; empty where the square holds no ground.</returns>
    /// <remarks>
    /// Nodes stand on the ground, as the shipped graphs' do (level 1's graph 4019: a median 0 m above it). The grid is as
    /// fine as the node budget allows, never finer than <see cref="NavGraphRequest.MinimumSpacingMetres"/>.
    /// </remarks>
    public static NavGraphLayout Build(NavGraphRequest request, Func<double, double, double?> ground)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ground);
        int across = Math.Max(2, (int)Math.Floor(Math.Sqrt(request.MaximumNodes)));
        double spacing = Math.Max(request.MinimumSpacingMetres, request.SideMetres / (across - 1));
        across = Math.Max(2, (int)Math.Floor(request.SideMetres / spacing) + 1);
        double left = request.CentreMetres.X - (spacing * (across - 1) / 2), bottom = request.CentreMetres.Y - (spacing * (across - 1) / 2);

        Vector3d?[,] grid = new Vector3d?[across, across];
        for (int j = 0; j < across; j++)
        {
            for (int i = 0; i < across; i++)
            {
                double x = left + (i * spacing), y = bottom + (j * spacing);
                if (ground(x, y) is { } z)
                {
                    grid[i, j] = new Vector3d(x, y, z);
                }
            }
        }

        // Each cell to its right, upper, and both upper diagonal neighbours: every pair once.
        List<((int, int) A, (int, int) B)> pairs = [];
        foreach ((int di, int dj) in (ReadOnlySpan<(int, int)>)[(1, 0), (0, 1), (1, 1), (-1, 1)])
        {
            for (int j = 0; j < across; j++)
            {
                for (int i = 0; i < across; i++)
                {
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && ni < across && nj < across && grid[i, j] is { } a && grid[ni, nj] is { } b && Walkable(a, b, request, ground))
                    {
                        pairs.Add(((i, j), (ni, nj)));
                    }
                }
            }
        }

        // The largest piece that links together: a node nothing reaches would only strand a soldier put on it.
        Dictionary<(int, int), List<(int, int)>> next = [];
        foreach (((int, int) a, (int, int) b) in pairs)
        {
            next.GetOrAdd(a).Add(b);
            next.GetOrAdd(b).Add(a);
        }

        HashSet<(int, int)> best = [];
        HashSet<(int, int)> seen = [];
        foreach ((int, int) start in next.Keys)
        {
            if (!seen.Add(start))
            {
                continue;
            }

            HashSet<(int, int)> piece = [start];
            Queue<(int, int)> open = new([start]);
            while (open.TryDequeue(out (int, int) at))
            {
                foreach ((int, int) to in next[at])
                {
                    if (seen.Add(to))
                    {
                        piece.Add(to);
                        open.Enqueue(to);
                    }
                }
            }

            if (piece.Count > best.Count)
            {
                best = piece;
            }
        }

        // Numbered from 1 in grid order, as the shipped graphs count theirs.
        Dictionary<(int, int), int> ids = [];
        List<(int, Vector3d)> nodes = [];
        for (int j = 0; j < across; j++)
        {
            for (int i = 0; i < across; i++)
            {
                if (best.Contains((i, j)))
                {
                    ids[(i, j)] = nodes.Count + 1;
                    nodes.Add((nodes.Count + 1, grid[i, j]!.Value));
                }
            }
        }

        List<(int, int)> links = [.. pairs.Where(p => ids.ContainsKey(p.A) && ids.ContainsKey(p.B)).Select(p => (ids[p.A], ids[p.B]))];
        return new NavGraphLayout(nodes, links, spacing);
    }

    /// <summary>Whether a soldier can walk straight from one point to another: gentle enough, and the ground keeps to the line.</summary>
    /// <param name="a">One point, in world metres.</param>
    /// <param name="b">The other.</param>
    /// <param name="request">The limits.</param>
    /// <param name="ground">The ground's height at a point, in metres, or null where there is none.</param>
    /// <returns>Whether the link is walkable.</returns>
    public static bool Walkable(Vector3d a, Vector3d b, NavGraphRequest request, Func<double, double, double?> ground)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ground);
        double run = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
        if (run <= 0 || Math.Abs(b.Z - a.Z) / run > request.MaximumSlope)
        {
            return false;
        }

        int steps = Math.Max(2, (int)Math.Ceiling(run / 2));
        for (int s = 1; s < steps; s++)
        {
            double t = (double)s / steps;
            double x = a.X + ((b.X - a.X) * t), y = a.Y + ((b.Y - a.Y) * t);
            if (ground(x, y) is not { } z || Math.Abs(z - (a.Z + ((b.Z - a.Z) * t))) > request.MaximumGroundGapMetres)
            {
                return false;
            }
        }

        return true;
    }
}
