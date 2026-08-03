using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>One place in a level worth showing behind the menu, and the orbit that frames it.</summary>
/// <param name="Centre">The world point the orbit circles, in metres.</param>
/// <param name="Orbit">The orbit's radius, height, speed and tilt, sized to what is there.</param>
public readonly record struct ShowcasePoint(Vector3d Centre, MenuCameraSpec Orbit);

/// <summary>
/// Picks the handful of places in a mission worth looking at: its largest structures, framed closely and
/// spread across the level.
/// </summary>
/// <remarks>
/// <para>
/// <b>A place is a building, not a neighbourhood.</b> The first attempt at this clustered placements by
/// density and orbited each cluster's centroid, and it produced four near-identical wide shots: the points
/// it found on mission 1 spanned 138 metres while the orbits it sized for them were 99 to 166 metres
/// across, so every shot took in the whole area and the camera never actually looked at anything. Density
/// tells you where a lot of objects are, which in this game is "the base" — one answer, found four times.
/// </para>
/// <para>
/// What a point of interest actually is here is a <em>structure</em>: a hangar, a control tower, a radar
/// dish. Those are found directly — the placements are ranked by triangle count, which separates a
/// building from a barrel by orders of magnitude, and each one's own mesh gives the size to frame it at.
/// The orbit is then derived from the object rather than from a cluster's statistics, so it sits close
/// enough to read as a shot of that building.
/// </para>
/// <para>
/// <b>Spread is enforced, not hoped for.</b> Taking a structure strikes out everything within an exclusion
/// radius, which is derived from how big the level actually is so a compact trainyard and a sprawling
/// airbase both end up with their shots spaced across the whole of themselves. Without it the top five
/// structures are usually five parts of the same compound.
/// </para>
/// <para>
/// The player's own spawn leads the set when the level declares one. It is the one location in a mission
/// that is not a spoiler by definition — it is where the player is about to be standing.
/// </para>
/// <para>
/// Everything here is deterministic — a ranking and a greedy walk, no seeding and no iteration — so a level
/// always shows the same places in the same order. A menu that framed a different corner each time it
/// opened would read as a bug.
/// </para>
/// </remarks>
public static class MissionShowcasePoints
{
    private const string LogCategory = "Preview";

    /// <summary>The smallest a model may be, in metres, before it is scenery rather than a structure.</summary>
    private const double MinimumStructureRadius = 6.0;

    /// <summary>The largest a model may be, in metres; past this it is a compound, not a building.</summary>
    /// <remarks>
    /// A 45-metre radius is a 90-metre building, which is already a hangar. Letting it go higher is what
    /// put three of mission 1's five shots on meshes big enough that the orbit clamped at its maximum and
    /// every one of them was another wide view — the very thing circling a place was meant to replace.
    /// </remarks>
    private const double MaximumStructureRadius = 45.0;

    /// <summary>How far around a structure counts as its neighbourhood, in metres.</summary>
    private const double NeighbourhoodMetres = 60.0;

    /// <summary>The task-name prefix a level uses for the structures it places.</summary>
    private const string BuildingTaskPrefix = "Building_";

    /// <summary>How much more a structure the level declared as a building is worth than anything else.</summary>
    private const double DeclaredBuildingBonus = 4.0;

    /// <summary>How much has to stand within a structure's neighbourhood before it counts as somewhere.</summary>
    /// <remarks>
    /// A big object on its own in a field is not a location, and one slipped through on a relaxation pass
    /// with three things near it. Somewhere has stuff around it.
    /// </remarks>
    private const int MinimumNeighbours = 8;

    /// <summary>The fewest triangles a structure may have, so a big flat plane is not mistaken for one.</summary>
    private const int MinimumTriangles = 120;

    /// <summary>The closest two shots may ever be, in metres, however small the level is.</summary>
    private const double MinimumSeparation = 130.0;

    /// <summary>The floor the relaxation passes never go below, in metres.</summary>
    /// <remarks>
    /// Twice the tightest orbit the framing will produce, so two shots can never take in each other's
    /// subject however cramped the level is. A level that cannot place its shots this far apart gets fewer
    /// of them — SAM Base ends up with three, which is the honest answer for a base that small.
    /// </remarks>
    private const double AbsoluteMinimumGap = 72.0;

    /// <summary>The furthest apart the exclusion is ever pushed, in metres, however large the level is.</summary>
    private const double MaximumSeparation = 520.0;

    /// <summary>Finds the places in a mission worth showing.</summary>
    /// <param name="scene">The built scene, for its placements.</param>
    /// <param name="levelRoot">The level's task tree, for the player's spawn; may be <see langword="null"/>.</param>
    /// <param name="count">How many places to return.</param>
    /// <param name="fallback">The orbit style each place's own orbit is based on.</param>
    /// <param name="logger">Where to report what was found.</param>
    /// <returns>Up to <paramref name="count"/> places, the spawn first when there is one.</returns>
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

        var points = new List<ShowcasePoint>(count);

        // The spawn leads, when the level declares one. Its position comes off the task tree in ENGINE
        // UNITS -- an ObjectPos parameter is read raw, where WorldScene divides placements down as it
        // builds them -- so it has to be converted or the camera ends up four thousand times too far out.
        if (levelRoot is not null && PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn))
        {
            points.Add(new ShowcasePoint(
                new Vector3d(
                    spawn.Position.X / WorldScene.UnitsPerMetre,
                    spawn.Position.Y / WorldScene.UnitsPerMetre,
                    (spawn.Position.Z / WorldScene.UnitsPerMetre) + 4.0),
                fallback with { Radius = 34.0, Height = 14.0, StartAngleRadians = 0.0 }));
        }

        foreach (Structure structure in FindStructures(scene, count - points.Count, points, logger))
        {
            points.Add(structure.ToPoint(fallback));
        }

        TightenOverlappingShots(points);

        logger?.Information(
            LogCategory,
            $"{points.Count} showcase point(s) from {scene.Placements.Count} placement(s).");

        return points;
    }

    /// <summary>Pulls each orbit in until it cannot take in the subject of another shot.</summary>
    /// <param name="points">The chosen places; their orbits are narrowed in place.</param>
    /// <remarks>
    /// A level only has so much room in it. On a compact one the places end up closer together than the
    /// orbits sized for them, and two shots that overlap are one shot shown twice — which is exactly what
    /// made the first attempt at this worthless. Rather than drop a place for being too near its
    /// neighbour, its orbit is pulled in to just under half the distance between them: the shots stay
    /// distinct, and a cramped level simply gets tighter framing than a sprawling one.
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
                    Radius = Math.Max(24.0, orbit.Radius * scale),
                    Height = Math.Max(10.0, orbit.Height * scale),
                },
            };
        }
    }

    /// <summary>Takes the biggest structures in a level, spread out, largest first.</summary>
    private static List<Structure> FindStructures(
        WorldScene scene,
        int wanted,
        IReadOnlyList<ShowcasePoint> taken,
        IIgiLogger? logger)
    {
        var chosen = new List<Structure>(Math.Max(0, wanted));
        if (wanted <= 0)
        {
            return chosen;
        }

        // Every placement that is a building rather than a prop or a piece of landscape, measured by its
        // own mesh. The size is what frames the shot, so it has to come from the model, not from a guess.
        var candidates = new List<Structure>();
        var seen = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.Model.IsEmpty || placement.Model.TriangleCount < MinimumTriangles)
            {
                continue;
            }

            // Measured once per distinct model: a level places the same hangar a dozen times and walking
            // every vertex of each copy is the same answer paid for repeatedly.
            string key = placement.Instance.Name;
            if (!seen.TryGetValue(key, out double radius))
            {
                radius = ModelRadiusMetres(placement.Model);
                seen[key] = radius;
            }

            if (radius is < MinimumStructureRadius or > MaximumStructureRadius)
            {
                continue;
            }

            candidates.Add(new Structure(
                placement.Position,
                radius,
                placement.Model.TriangleCount,
                0,
                placement.Instance.Name,
                placement.TaskName));
        }

        // A building standing among other things is somewhere; the same building alone in a field is not.
        // Scoring on detail alone picked the latter often enough to matter, so the count of what stands
        // around each candidate is folded in — the two signals together are what a location actually is.
        for (int i = 0; i < candidates.Count; i++)
        {
            int neighbours = 0;
            double reachSquared = NeighbourhoodMetres * NeighbourhoodMetres;
            foreach (WorldPlacement placement in scene.Placements)
            {
                if (FlatDistanceSquared(placement.Position, candidates[i].Position) <= reachSquared)
                {
                    neighbours++;
                }
            }

            candidates[i] = candidates[i] with { Neighbours = neighbours };
        }

        candidates.RemoveAll(c => c.Neighbours < MinimumNeighbours);
        if (candidates.Count == 0)
        {
            logger?.Information(LogCategory, "Every structure in this level stands on its own; none is a place.");
            return chosen;
        }

        if (candidates.Count == 0)
        {
            logger?.Information(LogCategory, "No structure in this level is big enough to circle.");
            return chosen;
        }

        // Best first, ties broken on position so the order never depends on how the level happened to
        // enumerate. Detail rather than size — a detailed building beats a big empty shed — multiplied by
        // how much stands around it.
        candidates.Sort((a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore
                : a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X)
                : a.Position.Y.CompareTo(b.Position.Y);
        });

        // How far apart the shots should be, from how big the level actually is: the five best structures
        // in a level are otherwise five corners of the same compound.
        double separation = Separation(candidates, wanted);

        // Passes of decreasing separation, so a level too small to spread its shots gets them closer
        // together rather than getting fewer of them.
        //
        // What never relaxes is that no two shots may be of the same KIND of building. A level places one
        // prefab a dozen times, and the first version of this happily picked three of them: the same hut,
        // three times, which is what made the whole thing look broken. A mission with only two distinct
        // structures worth showing gets two shots and cycles between two — fewer places, honestly shown,
        // beats five that are the same place.
        double[] reaches =
        [
            separation,
            Math.Max(separation * 0.5, AbsoluteMinimumGap),
            AbsoluteMinimumGap,
        ];

        var usedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (double reach in reaches)
        {
            if (chosen.Count >= wanted)
            {
                break;
            }

            foreach (Structure candidate in candidates)
            {
                if (chosen.Count >= wanted)
                {
                    break;
                }

                if (!usedFamilies.Contains(candidate.Family) && IsClear(candidate, reach, taken, chosen))
                {
                    chosen.Add(candidate);
                    usedFamilies.Add(candidate.Family);
                }
            }
        }

        logger?.Information(
            LogCategory,
            $"Chose {chosen.Count} structure(s) of {candidates.Count} candidate(s), {separation:0} m apart.");

        foreach (Structure c in chosen)
        {
            logger?.Information(
                LogCategory,
                $"  {c.Task} ({c.Model}), {c.Radius * 2:0} m across, {c.Neighbours} object(s) around it.");
        }

        return chosen;
    }

    /// <summary>How far apart the shots are pushed, from the spread of the level's own structures.</summary>
    private static double Separation(List<Structure> candidates, int wanted)
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

        // Roughly the level's extent divided between the shots, so they land across the whole of it.
        double extent = Math.Max(maxX - minX, maxY - minY);
        return Math.Clamp(extent / Math.Max(2, wanted), MinimumSeparation, MaximumSeparation);
    }

    private static bool IsClear(
        Structure candidate,
        double separation,
        IReadOnlyList<ShowcasePoint> taken,
        List<Structure> chosen)
    {
        double squared = separation * separation;
        foreach (ShowcasePoint point in taken)
        {
            if (FlatDistanceSquared(candidate.Position, point.Centre) < squared)
            {
                return false;
            }
        }

        foreach (Structure other in chosen)
        {
            if (FlatDistanceSquared(candidate.Position, other.Position) < squared)
            {
                return false;
            }
        }

        return true;
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

    private static double FlatDistanceSquared(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>One structure worth circling: where it stands, how wide it is, how detailed, how busy.</summary>
    private readonly record struct Structure(
        Vector3d Position,
        double Radius,
        int Triangles,
        int Neighbours,
        string Model,
        string Task)
    {
        /// <summary>Gets a value indicating whether the level itself called this a building.</summary>
        /// <remarks>
        /// Level scripts name their placing tasks, and the name is authored: a structure is placed by a
        /// task called <c>Building_1112</c> where a crate or a truck is an <c>EditRigidObj</c>. That is a
        /// statement of intent by whoever built the level, and no heuristic over triangle counts is going
        /// to beat it — so it is read directly and weighs more than anything derived.
        /// </remarks>
        public bool IsDeclaredBuilding =>
            Task.StartsWith(BuildingTaskPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets the model's family — everything before the first underscore of its name.</summary>
        /// <remarks>
        /// A pack names a model <c>418_01_1</c> and <c>418_02_1</c>, which are two pieces or two variants
        /// of one structure. Keeping the shots distinct by full model name let both through and put what
        /// is visibly the same building on screen twice; the family is what actually has to differ.
        /// </remarks>
        public string Family
        {
            get
            {
                int underscore = Model.IndexOf('_', StringComparison.Ordinal);
                return underscore > 0 ? Model[..underscore] : Model;
            }
        }

        /// <summary>How good a subject this is: what it is, how big, how detailed, and how busy around it.</summary>
        public double Score =>
            Triangles
            * (1.0 + Math.Sqrt(Neighbours))
            * (IsDeclaredBuilding ? DeclaredBuildingBonus : 1.0)

            // Size counts for something on its own: between two equally detailed buildings the bigger one
            // is the landmark, and a landmark is what a shot wants to be of.
            * (1.0 + (Radius / 20.0));

        public ShowcasePoint ToPoint(MenuCameraSpec fallback)
        {
            // Close enough that the building fills the frame. The old sizing put the camera further out
            // than the distance between two shots, which is why every shot looked the same; this keeps the
            // orbit to a little over the object it is circling.
            double radius = Math.Clamp((Radius * 2.2) + 16.0, 30.0, 80.0);
            double height = Math.Clamp((Radius * 0.7) + 9.0, 12.0, 34.0);

            // Look at the middle of the structure rather than the ground it stands on.
            return new ShowcasePoint(
                new Vector3d(Position.X, Position.Y, Position.Z + (Radius * 0.35)),
                fallback with { Radius = radius, Height = height });
        }
    }
}
