using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// A stand of trees as lines meet it: the stand's index walked toward the lines, and each tree near them traced as a
/// solid model placed where the tree stands.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps one solid model in the stand, <c>tRigidTree</c>, moves it onto each tree it tests, and runs the
/// solid model's own line handler on it. A hit by the tree is then reported as a hit on the stand.
/// </para>
/// <para>
/// The walk is the stand's two-dimensional index (<see cref="Igi2ForestBsp"/>) taken nearest first from the first
/// line's start: the child on the start's side of each split first, then the node's own tree, then the far child,
/// each child only when its region, widened by a margin, still reaches the lines.
/// </para>
/// </remarks>
public sealed class Igi2ForestCollider : IIgi2Collider
{
    /// <summary>The material a tree's line trace passes through, <c>iIgnoreMaterial = 32</c>: the foliage.</summary>
    public const int FoliageMaterial = 32;

    private readonly Igi2ForestBsp _index;
    private readonly Vector3f[] _scales;
    private readonly float[][] _orientations;
    private readonly Igi2RigidObject _tree;
    private readonly Igi2LineTrace _scratch;
    private readonly float _treeRadius;
    private readonly float _marginX;
    private readonly float _marginY;
    private readonly float _halfExtent;

    private Igi2ForestCollider(
        object owner,
        Vector3d centre,
        float areaSize,
        Igi2ForestBsp index,
        Vector3f[] scales,
        float[][] orientations,
        Igi2RigidObject tree,
        float treeRadius,
        float marginX,
        float marginY)
    {
        Owner = owner;
        Position = centre;
        _index = index;
        _scales = scales;
        _orientations = orientations;
        _tree = tree;
        _treeRadius = treeRadius;
        _marginX = marginX;
        _marginY = marginY;
        _halfExtent = ((Fpu)areaSize * 2048.0).F;
        BoundingRadius = ((Fpu)areaSize * 4096.0).F;
        _scratch = new Igi2LineTrace(32);
    }

    /// <summary>Gets the stand, as a hit on it reports.</summary>
    public object Owner { get; }

    /// <summary>Gets where the stand sits, in world units: what its trees are placed relative to.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the stand's reach in the world's object tree, <c>vSize * 4096</c> (<c>Forest_UpdateInternalDataHandler</c>).</summary>
    public double BoundingRadius { get; }

    /// <summary>Gets the stand's query group, the forest's.</summary>
    public uint Group { get; } = Igi2CollisionWorld.ForestGroup;

    /// <summary>Gets the tree model's radius about its render centre, <c>vTreeRadius</c>.</summary>
    public float TreeRadius => _treeRadius;

    /// <summary>Builds a stand's collider over the tree model it plants.</summary>
    /// <param name="stand">The stand.</param>
    /// <param name="model">The tree model.</param>
    /// <returns>The collider, or <see langword="null"/> when the model has no collision mesh or the stand no trees.</returns>
    /// <remarks>
    /// <c>Forest_UpdateInternalDataHandler</c> (<c>igi2 0x5A2F40</c>) measures the tree radius from the model's collision
    /// vertices: the furthest of them from the render mesh's sphere centre. Each tree's orientation is
    /// <c>Real32x9_Create</c> of its three angles, as <c>Forest_LoadData</c> (<c>0x59FBD0</c>) builds it.
    /// </remarks>
    public static Igi2ForestCollider? Build(in Igi2ForestStand stand, MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (stand.Trees.Count == 0 || Igi2CollisionWorld.Meshes(model) is not { } meshes || meshes[0] is null)
        {
            return null;
        }

        if (!MefCollisionMesh.TryRead(model.Container, 0, out MefCollisionMesh? mesh, OpenIGI.Formats.AssetFormatProfile.Igi2.Collision)
            || mesh is null)
        {
            return null;
        }

        Vector3f centre = model.RenderSphereCentre;
        float furthest = 0f;
        foreach (Vector3f v in mesh.Vertices)
        {
            float dx = ((Fpu)v.X - centre.X).F, dy = ((Fpu)v.Y - centre.Y).F;
            Fpu squared = ((Fpu)dx * dx) + ((Fpu)dy * dy) + (((Fpu)v.Z - centre.Z) * ((Fpu)v.Z - centre.Z));
            if (squared.Value > furthest)
            {
                furthest = squared.F;
            }
        }

        float treeRadius = Fpu.Sqrt(furthest).F;
        Igi2ForestBsp index = Igi2ForestBsp.Build(stand.Trees, stand.Centre, stand.HalfExtent, modelHeight: 0f);
        Vector3f[] scales = new Vector3f[index.Count];
        float[][] orientations = new float[index.Count][];
        for (int node = 0; node < index.Count; node++)
        {
            int tree = index.Nodes[node].Tree;
            Igi2Tree source = stand.Trees[index.TreeOf(node)];
            scales[tree] = source.Scale;
            orientations[tree] = Igi2Pose.Rotation(source.Angles.X, source.Angles.Y, source.Angles.Z);
        }

        Igi2CollisionOwner owner = new(ForestTypeName, stand.TaskId, stand.Model, 0);
        Igi2RigidObject solid = new(owner, meshes, model.NativeMarkerBound, isBuildingMesh: false) { Group = Igi2CollisionWorld.ForestGroup };
        float marginX = (((Fpu)stand.RandomScaleRangeX * 0.5 + 1.0) * treeRadius).F;
        float marginY = (((Fpu)stand.RandomScaleRangeY * 0.5 + 1.0) * treeRadius).F;
        return new Igi2ForestCollider(owner, stand.Centre, stand.AreaSize, index, scales, orientations, solid, treeRadius, marginX, marginY);
    }

    /// <summary>The task type a stand is placed by.</summary>
    public const string ForestTypeName = "Forest";

    /// <summary>
    /// Vertical lines against the trees, <c>Forest_GetNextZHandler</c> (<c>igi2 0x5A1480</c>) over
    /// <c>Forest_GetNextZTrees_Optimized</c> (<c>0x5A0E40</c>).
    /// </summary>
    /// <param name="trace">The lines and their running results.</param>
    /// <remarks>
    /// Only the first line's start steers the walk: a tree takes part when the start is inside its footprint, its
    /// position give or take its scaled radius. Every line is traced against such a tree, and the walk stops at the
    /// first tree that becomes the first line's hit, which is then reported as the stand's.
    /// </remarks>
    public void NextZ(Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (_index.Root == Igi2ForestBsp.NoNode)
        {
            return;
        }

        float px = ((Fpu)trace.Starts[0].X - Position.X).F;
        float py = ((Fpu)trace.Starts[0].Y - Position.Y).F;
        Region root = new(_halfExtent, -_halfExtent, _halfExtent, -_halfExtent);
        _ = Ground(trace, _index.Root, 0, root, px, py);
    }

    /// <summary>
    /// Lines in any direction against the trees' trunks, <c>Forest_TraceLinesHandler</c> (<c>igi2 0x5A12D0</c>) over
    /// <c>Forest_TraceLineTrees_Optimized</c> (<c>0x5A0610</c>).
    /// </summary>
    /// <param name="trace">The lines and their running results.</param>
    /// <remarks>
    /// The lines' bounding rectangle steers the walk: a tree takes part when its square, a radius and a half either
    /// way, overlaps it. Each such tree is traced with the foliage passed through, against copies of the results, and
    /// a line it struck takes the copy, its local position moved from the tree's frame to the stand's. The walk stops
    /// once as many lines have been struck as there are lines.
    /// </remarks>
    public void TraceLines(Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (_index.Root == Igi2ForestBsp.NoNode)
        {
            return;
        }

        float rminX = ((Fpu)trace.Starts[0].X - Position.X).F, rminY = ((Fpu)trace.Starts[0].Y - Position.Y).F;
        float rmaxX = rminX, rmaxY = rminY;
        for (int i = 0; i < trace.Count; i++)
        {
            Grow(trace.Starts[i]);
            Grow(trace.Ends[i]);
        }

        _scratch.Count = trace.Count;
        _scratch.Mesh = trace.Mesh;
        _scratch.TracePortals = trace.TracePortals;
        _scratch.IgnoreMaterial = FoliageMaterial;
        _scratch.Includes = trace.Includes;
        _scratch.Lighting = trace.Lighting;
        for (int i = 0; i < trace.Count; i++)
        {
            _scratch.Starts[i] = trace.Starts[i];
            _scratch.Ends[i] = trace.Ends[i];
            _scratch.Intersections[i].CopyFrom(trace.Intersections[i]);
        }

        Rectangle lines = new(rminX, rminY, rmaxX, rmaxY);
        float px = ((Fpu)trace.Starts[0].X - Position.X).F;
        float py = ((Fpu)trace.Starts[0].Y - Position.Y).F;
        Region root = new(_halfExtent, -_halfExtent, _halfExtent, -_halfExtent);
        int struck = 0;
        _ = Lines(trace, _index.Root, 0, root, px, py, lines, ref struck);

        void Grow(Vector3d point)
        {
            float x = ((Fpu)point.X - Position.X).F, y = ((Fpu)point.Y - Position.Y).F;
            rmaxX = x > rmaxX ? x : rmaxX;
            rminX = x < rminX ? x : rminX;
            rmaxY = y > rmaxY ? y : rmaxY;
            rminY = y < rminY ? y : rminY;
        }
    }

    /// <summary>
    /// A line of sight against the trees, <c>Forest_GetLineOfSightHandler</c> (<c>igi2 0x5A1260</c>) over
    /// <c>Forest_LineOfSightTrees_Optimized</c> (<c>0x5A0B10</c>).
    /// </summary>
    /// <param name="sight">The line of sight.</param>
    /// <remarks>
    /// The walk steers by the sight's start and enters a region, or meets a tree, only when the line crosses it widened
    /// by a radius and a half (<c>Forest_IsLineOutOfNode</c>, <c>0x5A02F0</c>). Each tree is met as a solid model,
    /// foliage and all; one that crosses the sight nearer than anything before blocks it outright and ends the walk.
    /// </remarks>
    public void LineOfSight(Igi2LineOfSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        if (_index.Root == Igi2ForestBsp.NoNode)
        {
            return;
        }

        float vx = ((Fpu)sight.Start.X - Position.X).F;
        float vy = ((Fpu)sight.Start.Y - Position.Y).F;
        Region root = new(_halfExtent, -_halfExtent, _halfExtent, -_halfExtent);
        _ = Sight(sight, _index.Root, 0, root, vx, vy);
    }

    private bool Sight(Igi2LineOfSight sight, int node, int level, Region region, float vx, float vy)
    {
        Igi2ForestBspNode n = _index.Nodes[node];
        Vector3f at = _index.Positions[n.Tree];
        int axis = level & 1;
        bool below = Side(at, axis, vx, vy);

        Region near = region.Split(axis, keepLow: !below, Coordinate(at, axis));
        int nearChild = below ? n.High : n.Low;
        if (nearChild != Igi2ForestBsp.NoNode && !IsLineOut(near, sight)
            && Sight(sight, nearChild, level + 1, near, vx, vy))
        {
            return true;
        }

        if (!IsLineOut(new Region(at.X, at.X, at.Y, at.Y), sight))
        {
            float before = sight.T;
            Place(n.Tree, at);
            Igi2RigidLineOfSight.Meet(_tree, sight);
            if (sight.IsHit && sight.T < before)
            {
                sight.Transparency = 0f;
                return true;
            }
        }

        Region far = region.Split(axis, keepLow: below, Coordinate(at, axis));
        int farChild = below ? n.Low : n.High;
        return farChild != Igi2ForestBsp.NoNode && !IsLineOut(far, sight)
            && Sight(sight, farChild, level + 1, far, vx, vy);
    }

    /// <summary>
    /// Whether the sight's line, seen from above, misses a region widened by a radius and a half:
    /// <c>Forest_IsLineOutOfNode</c> (<c>igi2 0x5A02F0</c>).
    /// </summary>
    /// <remarks>
    /// Each end gets an outcode from the signs of its four distances to the sides. An end inside means the line is in;
    /// ends outside the same side mean it is out. Otherwise the line is cut against the sides the start is outside of,
    /// left, below, right and above in turn, each cut moving the start's other coordinate to where the line crosses that
    /// side, and it is in when a cut falls strictly within the side's span.
    /// </remarks>
    private bool IsLineOut(Region region, Igi2LineOfSight sight)
    {
        float x1 = ((Fpu)sight.Start.X - Position.X).F, y1 = ((Fpu)sight.Start.Y - Position.Y).F;
        float x2 = ((Fpu)sight.End.X - Position.X).F, y2 = ((Fpu)sight.End.Y - Position.Y).F;
        Fpu reach = (Fpu)_treeRadius * 1.5f;
        float minX = ((Fpu)region.MinX - reach).F, minY = ((Fpu)region.MinY - reach).F;
        float maxX = (reach + region.MaxX).F, maxY = (reach + region.MaxY).F;

        int start = Outcode(x1, y1), finish = Outcode(x2, y2);
        if (start * finish == 0)
        {
            return false;
        }

        if ((start & finish) != 0)
        {
            return true;
        }

        if (start == 5 || finish == 10)
        {
            return false;
        }

        if ((start & 1) != 0)
        {
            y1 = (((((Fpu)y2 - y1) * ((Fpu)minX - x1)) / ((Fpu)x2 - x1)) + y1).F;
            if (y1 > minY && y1 < maxY)
            {
                return false;
            }
        }

        if ((start & 2) != 0)
        {
            x1 = (((((Fpu)minY - y1) * ((Fpu)x2 - x1)) / ((Fpu)y2 - y1)) + x1).F;
            if (x1 > minX && x1 < maxX)
            {
                return false;
            }
        }

        if ((start & 4) != 0)
        {
            y1 = (((((Fpu)maxX - x1) * ((Fpu)y2 - y1)) / ((Fpu)x2 - x1)) + y1).F;
            if (y1 > minY && y1 < maxY)
            {
                return false;
            }
        }

        if ((start & 8) == 0)
        {
            return true;
        }

        float x = (((((Fpu)maxY - y1) * ((Fpu)x2 - x1)) / ((Fpu)y2 - y1)) + x1).F;
        return !(x > minX && x < maxX);

        // Left, below, right, above: the sign bits of the four differences, a zero difference counting as inside.
        int Outcode(float px, float py) =>
            (float.IsNegative(((Fpu)px - minX).F) ? 1 : 0)
            | (float.IsNegative(((Fpu)py - minY).F) ? 2 : 0)
            | (float.IsNegative(((Fpu)maxX - px).F) ? 4 : 0)
            | (float.IsNegative(((Fpu)maxY - py).F) ? 8 : 0);
    }

    private bool Ground(Igi2LineTrace trace, int node, int level, Region region, float px, float py)
    {
        Igi2ForestBspNode n = _index.Nodes[node];
        Vector3f at = _index.Positions[n.Tree];
        int axis = level & 1;
        bool below = Side(at, axis, px, py);

        Region near = region.Split(axis, keepLow: !below, Coordinate(at, axis));
        int nearChild = below ? n.High : n.Low;
        if (nearChild != Igi2ForestBsp.NoNode && near.Holds(px, py, _marginX, _marginY)
            && Ground(trace, nearChild, level + 1, near, px, py))
        {
            return true;
        }

        if (FootprintHolds(n.Tree, at, px, py))
        {
            Place(n.Tree, at);
            _tree.NextZ(trace);
            Igi2Intersection first = trace.Intersections[0];
            if (first.IsHit && ReferenceEquals(first.HitObject, _tree))
            {
                first.HitObject = this;
                return true;
            }
        }

        Region far = region.Split(axis, keepLow: below, Coordinate(at, axis));
        int farChild = below ? n.Low : n.High;
        return farChild != Igi2ForestBsp.NoNode && far.Holds(px, py, _marginX, _marginY)
            && Ground(trace, farChild, level + 1, far, px, py);
    }

    private bool Lines(Igi2LineTrace trace, int node, int level, Region region, float px, float py, Rectangle lines, ref int struck)
    {
        Igi2ForestBspNode n = _index.Nodes[node];
        Vector3f at = _index.Positions[n.Tree];
        int axis = level & 1;
        bool below = Side(at, axis, px, py);
        double reach = ((Fpu)_treeRadius * 1.5).Value;

        Region near = region.Split(axis, keepLow: !below, Coordinate(at, axis));
        int nearChild = below ? n.High : n.Low;
        if (nearChild != Igi2ForestBsp.NoNode && near.Meets(lines, reach)
            && Lines(trace, nearChild, level + 1, near, px, py, lines, ref struck))
        {
            return true;
        }

        if (lines.Meets(at.X - reach, at.Y - reach, reach + at.X, reach + at.Y))
        {
            Place(n.Tree, at);
            _tree.NextZ(_scratch);
            for (int i = 0; i < trace.Count; i++)
            {
                Igi2Intersection copy = _scratch.Intersections[i];
                if (!copy.IsHit || !ReferenceEquals(copy.HitObject, _tree))
                {
                    continue;
                }

                copy.LocalPosition = new Vector3f(
                    ((Fpu)at.X + copy.LocalPosition.X).F,
                    ((Fpu)at.Y + copy.LocalPosition.Y).F,
                    ((Fpu)at.Z + copy.LocalPosition.Z).F);
                copy.HitObject = this;
                trace.Intersections[i].CopyFrom(copy);
                struck++;
            }

            if (struck == trace.Count)
            {
                return true;
            }
        }

        Region far = region.Split(axis, keepLow: below, Coordinate(at, axis));
        int farChild = below ? n.Low : n.High;
        return farChild != Igi2ForestBsp.NoNode && far.Meets(lines, reach)
            && Lines(trace, farChild, level + 1, far, px, py, lines, ref struck);
    }

    /// <summary>Whether the walk's point lies past the node's split, the sign of <c>node - point</c> on the axis.</summary>
    private static bool Side(Vector3f at, int axis, float px, float py)
    {
        float view = axis == 0 ? ((Fpu)at.X - px).F : ((Fpu)at.Y - py).F;
        return float.IsNegative(view);
    }

    private static float Coordinate(Vector3f at, int axis) => axis == 0 ? at.X : at.Y;

    private bool FootprintHolds(int tree, Vector3f at, float px, float py)
    {
        Vector3f scale = _scales[tree];
        float reachX = ((Fpu)scale.X * _treeRadius).F;
        float reachY = ((Fpu)scale.Y * _treeRadius).F;
        return px >= ((Fpu)at.X - reachX).Value && px <= ((Fpu)reachX + at.X).Value
            && ((Fpu)at.Y - reachY).Value <= py && ((Fpu)reachY + at.Y).Value >= py;
    }

    /// <summary>Moves the stand's one solid model onto a tree: position, orientation and scale.</summary>
    private void Place(int tree, Vector3f at)
    {
        _tree.Position = new Vector3d(
            ((Fpu)at.X + Position.X).Value,
            ((Fpu)at.Y + Position.Y).Value,
            ((Fpu)at.Z + Position.Z).Value);
        _orientations[tree].CopyTo(_tree.Orientation, 0);
        _tree.Scale = _scales[tree];
    }

    /// <summary>The lines' bounding rectangle, stand-local.</summary>
    private readonly record struct Rectangle(float MinX, float MinY, float MaxX, float MaxY)
    {
        /// <summary>Whether the rectangle overlaps a box, strictly.</summary>
        public bool Meets(double minX, double minY, double maxX, double maxY) =>
            MaxX > minX && MinX < maxX && MaxY > minY && MinY < maxY;
    }

    /// <summary>A node's region, the original's <c>avCoords</c>: largest and smallest X, then Y.</summary>
    private readonly record struct Region(float MaxX, float MinX, float MaxY, float MinY)
    {
        /// <summary>The region on one side of a split: the low side keeps everything up to it, the high side from it.</summary>
        public Region Split(int axis, bool keepLow, float at) => (axis, keepLow) switch
        {
            (0, true) => this with { MaxX = at },
            (0, false) => this with { MinX = at },
            (_, true) => this with { MaxY = at },
            _ => this with { MinY = at },
        };

        /// <summary>Whether the region, widened by the margins, holds a point.</summary>
        public bool Holds(float x, float y, float marginX, float marginY) =>
            ((Fpu)MinX - marginX).Value <= x && ((Fpu)MaxX + marginX).Value >= x
            && ((Fpu)MinY - marginY).Value <= y && ((Fpu)MaxY + marginY).Value >= y;

        /// <summary>Whether the region, widened by a reach, overlaps the lines' rectangle, strictly.</summary>
        public bool Meets(Rectangle lines, double reach) =>
            lines.Meets(((Fpu)MinX - reach).Value, ((Fpu)MinY - reach).Value, ((Fpu)MaxX + reach).Value, ((Fpu)MaxY + reach).Value);
    }
}
