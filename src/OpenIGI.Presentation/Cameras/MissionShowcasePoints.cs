using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Cameras;

/// <summary>One place in a level worth showing behind the menu, and the orbit that frames it.</summary>
/// <param name="Centre">The world point the orbit circles, in metres.</param>
/// <param name="Orbit">The orbit's radius, height, speed and tilt, sized to what is there.</param>
public readonly record struct ShowcasePoint(Vector3d Centre, MenuCameraSpec Orbit);

/// <summary>
/// Picks the handful of buildings in a mission worth looking at: three spread into a triangle across the
/// level, and one or two more standing inside it, each framed close enough to read as a shot of that
/// building.
/// </summary>
/// <remarks>
/// <para>
/// <b>A place is a building.</b> Not a crate, not a tree, not a parked helicopter — the level itself says
/// which is which. Level scripts name their placing tasks, and a structure is placed by a task called
/// <c>Building_1112</c> where a prop is an <c>EditRigidObj</c> or a <c>Heli_353</c>. That is a statement of
/// intent by whoever built the level and no heuristic over triangle counts is going to beat it, so it is
/// read directly and everything else is thrown away. The size and detail tests survive underneath it,
/// because a level names a doorway a building too. A level that uses no such names — a mod's, most likely —
/// falls back to judging the geometry alone, which is what the tests were doing on their own before.
/// </para>
/// <para>
/// <b>Three shots in a triangle, not five in a compound.</b> Ranking buildings and walking down the list
/// with an exclusion radius, which is what this used to do, gives whatever the ranking likes best and then
/// its nearest permitted neighbours: on mission 1 that was four shots inside a hundred and fifty metres of
/// each other, four views of one yard. Spread is not something a greedy walk arrives at, so it is
/// <em>solved for</em> instead: every triple of candidates is scored on how far apart its three corners
/// are and how close to equilateral they sit, and the best one wins. What comes out is three establishing
/// shots taken from three different parts of the map, which is what a menu backdrop is for.
/// </para>
/// <para>
/// <b>Then the middle, if there is a middle.</b> A triangle drawn across a sprawling airbase has a great
/// deal of level inside it and the shots should visit some of it; one drawn across a level barely bigger
/// than the triangle itself has nothing in the middle but the three corners seen again. So the interior
/// shots are conditional on the room being there — measured as the triangle's inradius — and on a building
/// standing well clear of all three edges. A mission that does not earn them gets three shots, which is the
/// honest answer.
/// </para>
/// <para>
/// <b>Close, not wide.</b> The orbit comes from the building's own mesh and sits a little over it, so the
/// subject fills the frame while the camera turns. Sizing shots from a cluster's statistics, as the first
/// attempt did, produced orbits wider than the distance between them — every shot took in the whole area
/// and the camera never actually looked at anything.
/// </para>
/// <para>
/// A level with no buildings at all falls back to the player's spawn, which is the one location in a mission
/// that is not a spoiler by definition — it is where the player is about to be standing. Mission 14 is the
/// only one that needs it: its scene holds a single object.
/// </para>
/// <para>
/// Everything here is deterministic — a ranking, an exhaustive search over a bounded shortlist, no seeding
/// and no iteration — so a level always shows the same places in the same order. A menu that framed a
/// different corner each time it opened would read as a bug.
/// </para>
/// </remarks>
public static class MissionShowcasePoints
{
    private const string LogCategory = "Preview";

    /// <summary>The task-name prefix a level uses for the structures it places.</summary>
    private const string BuildingTaskPrefix = "Building_";

    /// <summary>How many declared buildings a level needs before its own naming is trusted over the geometry.</summary>
    /// <remarks>
    /// Enough to draw the triangle out of. Below that the level either does not follow the convention or has
    /// almost nothing in it, and in both cases judging the meshes is the better answer than obeying two
    /// names.
    /// </remarks>
    private const int FewestDeclaredBuildings = 3;

    /// <summary>The smallest a model may be, in metres, before it is scenery rather than a structure.</summary>
    private const double MinimumStructureRadius = 6.0;

    /// <summary>The largest a model may be, in metres; past this it is a compound, not a building.</summary>
    /// <remarks>
    /// A 45-metre radius is a 90-metre building, which is already a hangar. Letting it go higher puts the
    /// orbit at its clamp and gives back the wide view that circling a place was meant to replace.
    /// </remarks>
    private const double MaximumStructureRadius = 45.0;

    /// <summary>The fewest triangles a structure may have, so a big flat plane is not mistaken for one.</summary>
    private const int MinimumTriangles = 120;

    /// <summary>How far around a structure counts as its neighbourhood, in metres.</summary>
    private const double NeighbourhoodMetres = 60.0;

    /// <summary>How much has to stand within a structure's neighbourhood before it counts as somewhere.</summary>
    /// <remarks>
    /// A building alone in a field is a building but it is not a location. Low, because the corners of the
    /// triangle are by definition at the edges of the built-up area and a strict count would pull all three
    /// back into the middle of the base — which is the shot this was written to stop producing.
    /// </remarks>
    private const int MinimumNeighbours = 5;

    /// <summary>How many cells across the level's candidates are bucketed for the triangle search.</summary>
    /// <remarks>
    /// The search is exhaustive over triples, so its input has to be bounded: one candidate per cell of an
    /// 8×8 grid caps it at 64 corners and around forty thousand triples, which is nothing. Bucketing by
    /// PLACE rather than taking the top 64 by rank is the point — the highest-ranked buildings in a level
    /// are all in the same compound, so a rank-based shortlist would hand the search the very clustering it
    /// exists to undo.
    /// </remarks>
    private const int ShortlistCellsAcross = 8;

    /// <summary>How much a triple is discounted for showing the same kind of building twice.</summary>
    /// <remarks>
    /// A level places one prefab a dozen times, and a search that only wanted distance happily picked three
    /// copies of the same hut standing far apart: three corners, one building, which is what made the whole
    /// thing look broken. A discount rather than a ban, so a level built out of one prefab still gets its
    /// three shots.
    /// </remarks>
    private const double FamilyRepeatPenalty = 0.4;

    /// <summary>The side length, in metres, below which the best triangle is really one compound.</summary>
    /// <remarks>Reported, not enforced: a small level still gets the best triple it has.</remarks>
    private const double CompactTriangleSide = 110.0;

    /// <summary>How much room the triangle needs inside it, as an inradius in metres, to be worth filling.</summary>
    /// <remarks>
    /// The largest circle that fits between the three corners, which is the honest measure of whether the
    /// middle is somewhere else or just the corners seen again — a long thin triangle has plenty of area
    /// and no middle at all. A little under twice the tightest orbit, so a shot placed in there is looking
    /// at its own subject rather than at the ones on either side of it.
    /// </remarks>
    private const double InteriorInradiusMetres = 55.0;

    /// <summary>How far off every edge an interior building must stand, in barycentric terms.</summary>
    /// <remarks>
    /// A barycentric coordinate is the fraction of the way across the triangle from the opposite edge, so
    /// requiring all three above this keeps an interior shot clear of the edges and out of the corners —
    /// a building just inside one edge is a fourth view of the corner shot next to it.
    /// </remarks>
    private const double InteriorMargin = 0.14;

    /// <summary>How far an interior shot must stand from the corners, as a fraction of the shortest side.</summary>
    private const double InteriorGapFraction = 0.28;

    /// <summary>The closest an interior shot may ever come to another shot, in metres.</summary>
    private const double InteriorMinimumGap = 70.0;

    /// <summary>The most shots taken from inside the triangle.</summary>
    private const int MaximumInteriorPoints = 2;

    /// <summary>Finds the places in a mission worth showing.</summary>
    /// <param name="scene">The built scene, for its placements.</param>
    /// <param name="levelRoot">The level's task tree, for the player's spawn; may be <see langword="null"/>.</param>
    /// <param name="count">The most places to return; three of them are the triangle's corners.</param>
    /// <param name="fallback">The orbit style each place's own orbit is based on.</param>
    /// <param name="logger">Where to report what was found.</param>
    /// <returns>Up to <paramref name="count"/> places: the triangle first, then its interior.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ShowcasePoint> Extract(
        WorldScene scene,
        GameTask? levelRoot,
        int count,
        MenuCameraSpec fallback,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (count <= 0 || scene.Placements.Count == 0)
        {
            return [];
        }

        List<Structure> candidates = FindBuildings(scene, logger);
        if (candidates.Count == 0)
        {
            logger?.Information(LogCategory, "Nothing in this level reads as a building; showing the spawn instead.");
            return SpawnOnly(levelRoot, fallback);
        }

        var chosen = new List<Structure>(count);
        if (candidates.Count < 3)
        {
            // One or two buildings is not a triangle. Show what there is rather than nothing.
            chosen.AddRange(candidates.Take(count));
            logger?.Information(
                LogCategory,
                $"Only {candidates.Count} building(s) to show; no triangle to draw between them.");
        }
        else
        {
            Structure[] triangle = PickTriangle(candidates, logger);
            chosen.AddRange(triangle);

            int room = Math.Min(count - chosen.Count, MaximumInteriorPoints);
            if (room > 0)
            {
                chosen.AddRange(PickInside(triangle, candidates, room, logger));
            }
        }

        var points = new List<ShowcasePoint>(chosen.Count);
        foreach (Structure structure in chosen)
        {
            points.Add(structure.ToPoint(fallback));
            logger?.Information(
                LogCategory,
                $"  {structure.Task} ({structure.Model}), {structure.Radius * 2:0} m across, "
                + $"{structure.Neighbours} object(s) around it.");
        }

        TightenOverlappingShots(points);

        logger?.Information(
            LogCategory,
            $"{points.Count} showcase point(s) from {scene.Placements.Count} placement(s).");

        return points;
    }

    /// <summary>The player's spawn, orbited close, for a level with nothing standing in it to circle.</summary>
    /// <remarks>
    /// A last resort, not a shot anyone chose. Mission 14 puts a single object in its scene, so there is no
    /// building to find and no triangle to draw; without this the backdrop falls back to a wide orbit around
    /// the median of every placement, which on a level with one placement is a survey of empty ground. The
    /// spawn at least frames where the player is about to be standing.
    /// </remarks>
    private static IReadOnlyList<ShowcasePoint> SpawnOnly(GameTask? levelRoot, MenuCameraSpec fallback)
    {
        // The spawn's position comes off the task tree in ENGINE UNITS -- an ObjectPos parameter is read
        // raw, where WorldScene divides placements down as it builds them -- so it has to be converted or
        // the camera ends up four thousand times too far out.
        if (levelRoot is null || !PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn))
        {
            return [];
        }

        return
        [
            new ShowcasePoint(
                new Vector3d(
                    spawn.Position.X / WorldScene.UnitsPerMetre,
                    spawn.Position.Y / WorldScene.UnitsPerMetre,
                    (spawn.Position.Z / WorldScene.UnitsPerMetre) + 4.0),
                fallback with { Radius = 30.0, Height = 13.0, StartAngleRadians = 0.0 }),
        ];
    }

    /// <summary>Every placement in a level that is a building, measured and scored.</summary>
    private static List<Structure> FindBuildings(WorldScene scene, IIgiLogger? logger)
    {
        // Measured once per distinct model: a level places the same hangar a dozen times and walking every
        // vertex of each copy is the same answer paid for repeatedly.
        var measured = new Dictionary<string, double>(StringComparer.Ordinal);
        var declared = new List<Structure>();
        var anyShape = new List<Structure>();

        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.Model.IsEmpty || placement.Model.TriangleCount < MinimumTriangles)
            {
                continue;
            }

            string key = placement.Instance.Name;
            if (!measured.TryGetValue(key, out double radius))
            {
                radius = ModelRadiusMetres(placement.Model);
                measured[key] = radius;
            }

            if (radius is < MinimumStructureRadius or > MaximumStructureRadius)
            {
                continue;
            }

            var structure = new Structure(
                placement.Position,
                radius,
                placement.Model.TriangleCount,
                0,
                placement.Instance.Name,
                placement.TaskName);

            anyShape.Add(structure);
            if (structure.IsDeclaredBuilding)
            {
                declared.Add(structure);
            }
        }

        // The level's own word for it, whenever the level gives one. This is what keeps helicopters, spline
        // segments and loose rigid objects — all of them big and detailed enough to pass every geometric
        // test there is — out of a set of shots that is supposed to be of buildings.
        List<Structure> pool = declared.Count >= FewestDeclaredBuildings ? declared : anyShape;
        if (declared.Count < FewestDeclaredBuildings && anyShape.Count > 0)
        {
            logger?.Information(
                LogCategory,
                "This level declares no buildings by name; falling back to judging the geometry.");
        }

        // A building standing among other things is somewhere; the same building alone in a field is not.
        for (int i = 0; i < pool.Count; i++)
        {
            int neighbours = 0;
            double reachSquared = NeighbourhoodMetres * NeighbourhoodMetres;
            foreach (WorldPlacement placement in scene.Placements)
            {
                if (FlatDistanceSquared(placement.Position, pool[i].Position) <= reachSquared)
                {
                    neighbours++;
                }
            }

            pool[i] = pool[i] with { Neighbours = neighbours };
        }

        // Relaxed rather than enforced when it would empty the level out: three shots of lonely buildings
        // beat no backdrop at all.
        List<Structure> populated = pool.FindAll(s => s.Neighbours >= MinimumNeighbours);
        List<Structure> chosen = populated.Count >= 3 ? populated : pool;

        // Best first, ties broken on position so the order never depends on how the level happened to
        // enumerate its placements.
        chosen.Sort((a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore
                : a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X)
                : a.Position.Y.CompareTo(b.Position.Y);
        });

        logger?.Information(
            LogCategory,
            $"{chosen.Count} building(s) to choose from ({declared.Count} declared by the level), "
            + $"spread over {Extent(chosen):0} m.");

        return chosen;
    }

    /// <summary>Solves for the three buildings that spread widest and sit most evenly around the level.</summary>
    /// <remarks>
    /// Exhaustive over the shortlist rather than greedy. A greedy walk takes the best-ranked building and
    /// then whatever else it is allowed, which on every retail level is the rest of the same compound; only
    /// looking at the triple as a whole can trade a slightly duller building for one that is four hundred
    /// metres away.
    /// </remarks>
    private static Structure[] PickTriangle(List<Structure> candidates, IIgiLogger? logger)
    {
        List<Structure> corners = Shortlist(candidates);
        double best = candidates[0].Score;
        double bestValue = double.NegativeInfinity;
        (int A, int B, int C) pick = (0, 1, 2);

        for (int a = 0; a < corners.Count - 2; a++)
        {
            for (int b = a + 1; b < corners.Count - 1; b++)
            {
                double ab = FlatDistance(corners[a].Position, corners[b].Position);
                for (int c = b + 1; c < corners.Count; c++)
                {
                    double bc = FlatDistance(corners[b].Position, corners[c].Position);
                    double ca = FlatDistance(corners[c].Position, corners[a].Position);
                    double shortest = Math.Min(ab, Math.Min(bc, ca));
                    double perimeter = ab + bc + ca;
                    if (perimeter <= 0.0)
                    {
                        continue;
                    }

                    // How close to equilateral, 1 for a perfect triangle and 0 for three points in a line.
                    // Without it the widest triple is almost always three buildings strung along the
                    // longest axis of the level, which is a line of shots rather than a tour of the map.
                    double shape = 12.0 * Math.Sqrt(3.0)
                        * TriangleArea(corners[a].Position, corners[b].Position, corners[c].Position)
                        / (perimeter * perimeter);

                    double quality = Math.Cbrt(
                        (corners[a].Score / best) * (corners[b].Score / best) * (corners[c].Score / best));

                    bool repeats = corners[a].Family == corners[b].Family
                        || corners[b].Family == corners[c].Family
                        || corners[c].Family == corners[a].Family;

                    // How far apart the three are, as the geometric mean of the shortest side and the
                    // average one. The shortest side alone is what a "no two shots may overlap" rule
                    // measures, and optimising it hands back a small, tidy triangle in the middle of a
                    // large level — mission 4's buildings stand over 389 metres and it picked a 160-metre
                    // one. Folding the average in lets a triangle that covers twice the ground win even
                    // though one of its corners is closer than it might be.
                    double spread = Math.Sqrt(shortest * (perimeter / 3.0));

                    // Distance leads; how even the triangle is and how good the three buildings are only
                    // modulate it. Spread is what was missing, so spread is what decides.
                    double value = spread
                        * (0.35 + (0.65 * shape))
                        * (0.45 + (0.55 * quality))
                        * (repeats ? FamilyRepeatPenalty : 1.0);

                    if (value > bestValue)
                    {
                        bestValue = value;
                        pick = (a, b, c);
                    }
                }
            }
        }

        Structure[] triangle = [corners[pick.A], corners[pick.B], corners[pick.C]];
        double sideA = FlatDistance(triangle[0].Position, triangle[1].Position);
        double sideB = FlatDistance(triangle[1].Position, triangle[2].Position);
        double sideC = FlatDistance(triangle[2].Position, triangle[0].Position);

        logger?.Information(
            LogCategory,
            $"Triangle of {corners.Count} corner(s): sides {sideA:0} / {sideB:0} / {sideC:0} m.");

        if (Math.Min(sideA, Math.Min(sideB, sideC)) < CompactTriangleSide)
        {
            logger?.Information(
                LogCategory,
                "This level is barely wider than one compound; its three shots are as far apart as it gets.");
        }

        return triangle;
    }

    /// <summary>Keeps the best building in each cell of a grid over the level, so the search stays bounded.</summary>
    private static List<Structure> Shortlist(List<Structure> candidates)
    {
        int cap = ShortlistCellsAcross * ShortlistCellsAcross;
        if (candidates.Count <= cap)
        {
            return candidates;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        foreach (Structure candidate in candidates)
        {
            minX = Math.Min(minX, candidate.Position.X);
            minY = Math.Min(minY, candidate.Position.Y);
            maxX = Math.Max(maxX, candidate.Position.X);
            maxY = Math.Max(maxY, candidate.Position.Y);
        }

        double cell = Math.Max(1.0, Math.Max(maxX - minX, maxY - minY) / ShortlistCellsAcross);
        var bestInCell = new Dictionary<(int X, int Y), Structure>();

        // The candidates arrive best first, so the first one to reach a cell is the best one in it.
        foreach (Structure candidate in candidates)
        {
            (int, int) key = ((int)((candidate.Position.X - minX) / cell), (int)((candidate.Position.Y - minY) / cell));
            if (!bestInCell.ContainsKey(key))
            {
                bestInCell[key] = candidate;
            }
        }

        var shortlist = new List<Structure>(bestInCell.Values);
        shortlist.Sort((a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore
                : a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X)
                : a.Position.Y.CompareTo(b.Position.Y);
        });

        return shortlist;
    }

    /// <summary>Takes the best buildings standing well inside the triangle, when there is room for any.</summary>
    private static List<Structure> PickInside(
        Structure[] triangle,
        List<Structure> candidates,
        int wanted,
        IIgiLogger? logger)
    {
        var inside = new List<Structure>(wanted);

        Vector3d a = triangle[0].Position;
        Vector3d b = triangle[1].Position;
        Vector3d c = triangle[2].Position;
        double ab = FlatDistance(a, b);
        double bc = FlatDistance(b, c);
        double ca = FlatDistance(c, a);
        double perimeter = ab + bc + ca;
        if (perimeter <= 0.0)
        {
            return inside;
        }

        // The radius of the largest circle that fits inside, which is the honest measure of whether there
        // is any level in there to look at — a long thin triangle has area without having a middle.
        double inradius = 2.0 * TriangleArea(a, b, c) / perimeter;
        if (inradius < InteriorInradiusMetres)
        {
            logger?.Information(
                LogCategory,
                $"The triangle's middle is only {inradius:0} m across; nothing to show inside it.");
            return inside;
        }

        double gap = Math.Max(InteriorMinimumGap, Math.Min(ab, Math.Min(bc, ca)) * InteriorGapFraction);
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            triangle[0].Family,
            triangle[1].Family,
            triangle[2].Family,
        };

        int within = 0;
        int sameFamily = 0;
        int crowded = 0;
        foreach (Structure candidate in candidates)
        {
            if (!IsWellInside(candidate.Position, a, b, c))
            {
                continue;
            }

            within++;
            if (families.Contains(candidate.Family))
            {
                sameFamily++;
                continue;
            }

            if (IsCrowded(candidate.Position, triangle, inside, gap))
            {
                crowded++;
                continue;
            }

            if (inside.Count < wanted)
            {
                inside.Add(candidate);
                families.Add(candidate.Family);
            }
        }

        logger?.Information(
            LogCategory,
            $"{inside.Count} shot(s) inside a triangle {inradius:0} m across at its widest, "
            + $"of {within} building(s) standing in it ({sameFamily} a corner's kind, {crowded} too close).");

        return inside;
    }

    /// <summary>Whether a point sits clear of all three of the triangle's edges.</summary>
    private static bool IsWellInside(Vector3d point, Vector3d a, Vector3d b, Vector3d c)
    {
        double denominator = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
        if (Math.Abs(denominator) < 1e-6)
        {
            return false;
        }

        double u = (((b.Y - c.Y) * (point.X - c.X)) + ((c.X - b.X) * (point.Y - c.Y))) / denominator;
        double v = (((c.Y - a.Y) * (point.X - c.X)) + ((a.X - c.X) * (point.Y - c.Y))) / denominator;
        double w = 1.0 - u - v;
        return u >= InteriorMargin && v >= InteriorMargin && w >= InteriorMargin;
    }

    private static bool IsCrowded(
        Vector3d position,
        IReadOnlyList<Structure> triangle,
        IReadOnlyList<Structure> inside,
        double gap)
    {
        double squared = gap * gap;
        foreach (Structure corner in triangle)
        {
            if (FlatDistanceSquared(position, corner.Position) < squared)
            {
                return true;
            }
        }

        foreach (Structure other in inside)
        {
            if (FlatDistanceSquared(position, other.Position) < squared)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Pulls each orbit in until it cannot take in the subject of another shot.</summary>
    /// <param name="points">The chosen places; their orbits are narrowed in place.</param>
    /// <remarks>
    /// A backstop rather than the normal path, now that the shots are solved for spread: two of them can
    /// still end up close on a level with nowhere else to put them, and two orbits that overlap are one
    /// shot shown twice. Rather than drop a place for being too near its neighbour, its orbit is pulled in
    /// to just under half the distance between them.
    /// </remarks>
    private static void TightenOverlappingShots(List<ShowcasePoint> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            double nearest = double.MaxValue;
            for (int j = 0; j < points.Count; j++)
            {
                if (i != j)
                {
                    nearest = Math.Min(nearest, FlatDistanceSquared(points[i].Centre, points[j].Centre));
                }
            }

            if (nearest is double.MaxValue or <= 0.0)
            {
                continue;
            }

            double limit = Math.Sqrt(nearest) * 0.45;
            MenuCameraSpec orbit = points[i].Orbit;
            if (orbit.Radius <= limit)
            {
                continue;
            }

            // The height comes down with the radius, so a tightened shot keeps its angle rather than
            // becoming a steep look straight down at the roof.
            double scale = Math.Max(0.35, limit / orbit.Radius);
            points[i] = points[i] with
            {
                Orbit = orbit with
                {
                    Radius = Math.Max(Structure.ClosestOrbitMetres, orbit.Radius * scale),
                    Height = Math.Max(Structure.LowestOrbitMetres, orbit.Height * scale),
                },
            };
        }
    }

    /// <summary>The radius of a model's own mesh, in metres.</summary>
    /// <remarks>
    /// The horizontal extent, not the full three-dimensional one: the orbit is a circle on the ground, so
    /// what decides how far out it has to sit is how wide the building is, not how tall. A radio mast
    /// framed by its height would be circled from three hundred metres away.
    /// </remarks>
    private static double ModelRadiusMetres(MefModel model)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (MefVertex vertex in model.Vertices)
        {
            minX = MathF.Min(minX, vertex.Position.X);
            minY = MathF.Min(minY, vertex.Position.Y);
            maxX = MathF.Max(maxX, vertex.Position.X);
            maxY = MathF.Max(maxY, vertex.Position.Y);
        }

        if (minX > maxX)
        {
            return 0.0;
        }

        double halfWidth = (maxX - minX) * 0.5 / WorldScene.UnitsPerMetre;
        double halfDepth = (maxY - minY) * 0.5 / WorldScene.UnitsPerMetre;
        return Math.Sqrt((halfWidth * halfWidth) + (halfDepth * halfDepth));
    }

    /// <summary>How far across the level its buildings stand, in metres, along its longer axis.</summary>
    private static double Extent(IReadOnlyList<Structure> candidates)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        foreach (Structure candidate in candidates)
        {
            minX = Math.Min(minX, candidate.Position.X);
            minY = Math.Min(minY, candidate.Position.Y);
            maxX = Math.Max(maxX, candidate.Position.X);
            maxY = Math.Max(maxY, candidate.Position.Y);
        }

        return candidates.Count == 0 ? 0.0 : Math.Max(maxX - minX, maxY - minY);
    }

    /// <summary>Twice the area of the triangle three points make on the ground, halved.</summary>
    private static double TriangleArea(Vector3d a, Vector3d b, Vector3d c) =>
        Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) * 0.5;

    private static double FlatDistance(Vector3d a, Vector3d b) => Math.Sqrt(FlatDistanceSquared(a, b));

    private static double FlatDistanceSquared(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>One building worth circling: where it stands, how wide it is, how detailed, how busy.</summary>
    private readonly record struct Structure(
        Vector3d Position,
        double Radius,
        int Triangles,
        int Neighbours,
        string Model,
        string Task)
    {
        /// <summary>The tightest the orbit is ever pulled, in metres.</summary>
        public const double ClosestOrbitMetres = 20.0;

        /// <summary>The lowest the camera is ever brought, in metres above the subject's middle.</summary>
        public const double LowestOrbitMetres = 8.0;

        /// <summary>Gets a value indicating whether the level itself called this a building.</summary>
        public bool IsDeclaredBuilding =>
            Task.StartsWith(BuildingTaskPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets the model's family — everything before the first underscore of its name.</summary>
        /// <remarks>
        /// A pack names a model <c>418_01_1</c> and <c>418_02_1</c>, which are two pieces or two variants of
        /// one structure. Keeping the shots distinct by full model name lets both through and puts what is
        /// visibly the same building on screen twice; the family is what actually has to differ.
        /// </remarks>
        public string Family
        {
            get
            {
                int underscore = Model.IndexOf('_', StringComparison.Ordinal);
                return underscore > 0 ? Model[..underscore] : Model;
            }
        }

        /// <summary>How good a subject this is: how detailed, how big, and how busy around it.</summary>
        public double Score =>
            Triangles
            * (1.0 + Math.Sqrt(Neighbours))

            // Size counts for something on its own: between two equally detailed buildings the bigger one
            // is the landmark, and a landmark is what a shot wants to be of.
            * (1.0 + (Radius / 20.0));

        /// <summary>Turns the building into a shot of itself: an orbit sized to the mesh, close in.</summary>
        /// <remarks>
        /// <para>
        /// The distance is a multiple of the building's own radius plus a fixed margin, never a flat number:
        /// a fixed one either stands a shed off at a hundred metres or drives the camera through a hangar's
        /// wall. At 1.3× plus nine metres a small building is watched from a dozen metres off its wall and
        /// a large one from around twenty — close enough to read as a shot of that building rather than of
        /// the area it stands in, and still outside anything the level puts against it.
        /// </para>
        /// <para>
        /// The height is deliberately low against the radius. A tall orbit looks down at a roof, and what
        /// makes a building read is its elevation; a third of the radius keeps the camera tilted about
        /// twenty degrees down, which sees the front of a wall and the ground it stands on at once.
        /// </para>
        /// </remarks>
        public ShowcasePoint ToPoint(MenuCameraSpec fallback)
        {
            double radius = Math.Clamp((Radius * 1.3) + 9.0, ClosestOrbitMetres, 70.0);
            double height = Math.Clamp((Radius * 0.35) + 6.0, LowestOrbitMetres, 20.0);

            // Look at the middle of the structure rather than the ground it stands on.
            return new ShowcasePoint(
                new Vector3d(Position.X, Position.Y, Position.Z + Math.Clamp(Radius * 0.4, 3.0, 16.0)),
                fallback with { Radius = radius, Height = height });
        }
    }
}
