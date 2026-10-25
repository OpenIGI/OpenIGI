using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// Lines in any direction against the ground: <c>Terrain_TraceLinesHandler1</c> (<c>igi2 0x5730B0</c>), with the
/// client's FPU arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// Every tile is walked for every line, in the tiles' declared order. The line is taken into the tile's sample grid,
/// clipped to it, and stepped along its longer axis a column (or row) at a time; each step tests the cell the line is
/// in and, where it crosses into the next row, that cell too. A cell is two triangles split along its flatter
/// diagonal, and the first of them the line crosses nearer than anything found so far, outside the discard boxes,
/// is the tile's hit.
/// </para>
/// <para>
/// A tile's hit replaces the line's result only when nearer, and only when no later tile owns the point: the last
/// tile in declared order whose interior, half a sample in from its edges, holds the point is the only one whose hit
/// counts there, so a finer tile laid over a coarser one answers for the ground it covers.
/// </para>
/// </remarks>
internal static class Igi2TerrainLineTrace
{
    /// <summary>The plane test's tolerance, <c>0x3727C5AC</c>: a line this close to parallel misses.</summary>
    private const float ParallelTolerance = 1e-5f;

    private const float HalfSample = 0.5f;

    /// <summary>Traces every line of a trace against every tile.</summary>
    /// <param name="ground">The terrain the lines hit, reported as the struck object.</param>
    /// <param name="surface">The level's ground.</param>
    /// <param name="trace">The lines and their running results.</param>
    public static void Trace(Igi2TerrainCollider ground, Igi2TerrainSurface surface, WorldLineTrace trace)
    {
        for (int tile = 0; tile < surface.Tiles.Count; tile++)
        {
            Tile frame = new(surface.Tiles[tile], surface.Origin);
            for (int i = 0; i < trace.Count; i++)
            {
                if (frame.Trace(surface, trace.Starts[i], trace.Ends[i], out Vector3f local))
                {
                    Record(ground, surface, tile, frame, local, trace.Starts[i], trace.Intersections[i]);
                }
            }
        }
    }

    /// <summary>Turns a tile's hit into the line's result, when it is nearer and the tile owns the point.</summary>
    private static void Record(
        Igi2TerrainCollider ground,
        Igi2TerrainSurface surface,
        int tile,
        in Tile frame,
        Vector3f local,
        Vector3d start,
        WorldIntersection hit)
    {
        Vector3d origin = surface.Origin;
        double x = (((((Fpu)local.X + HalfSample) / frame.ScaleX) + frame.Source.Centre.X) - frame.HalfWidth).Value;
        double y = (((((Fpu)local.Y + HalfSample) / frame.ScaleY) + frame.Source.Centre.Y) - (double)frame.HalfHeight).Value;
        double z = ((Fpu)local.Z + origin.Z).Value;
        Fpu dx = (Fpu)x - start.X, dy = (Fpu)y - start.Y, dz = (Fpu)z - start.Z;
        Fpu distance = Fpu.Sqrt((dy * dy) + (dx * dx) + (dz * dz));
        if (hit.IsHit && !(distance.Value < hit.Distance))
        {
            return;
        }

        if (!Owns(surface, tile, x, y))
        {
            return;
        }

        hit.HitObject = ground;
        hit.Position = new Vector3d(x, y, z);
        hit.IsHit = true;
        hit.LocalPosition = new Vector3f(((Fpu)x - origin.X).F, ((Fpu)y - origin.Y).F, ((Fpu)z - origin.Z).F);
        hit.IsDoubleFace = false;
        hit.EnterMaterial = null;
        hit.LeaveMaterial = null;
        hit.Distance = distance.Value;

        if (!Igi2TerrainVertex.TrySample(surface.Tiles, ((Fpu)x - origin.X).F, ((Fpu)y - origin.Y).F, out Igi2TerrainSample sample))
        {
            return;
        }

        hit.EnterMaterial = ground.LevelMaterial(sample.Material);
        Vector3f n = sample.Normal;
        if (n.X != 0f || n.Y != 0f || n.Z != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((Fpu)n.X * n.X) + ((Fpu)n.Y * n.Y) + ((Fpu)n.Z * n.Z));
            n = new Vector3f(((Fpu)n.X * inverse).F, ((Fpu)n.Y * inverse).F, (inverse * n.Z).F);
        }

        hit.Normal = n;
        hit.LocalNormal = n;
    }

    /// <summary>Whether a tile owns a point: no later tile's interior holds it.</summary>
    private static bool Owns(Igi2TerrainSurface surface, int tile, double x, double y)
    {
        Vector3d origin = surface.Origin;
        Fpu across = (Fpu)x - origin.X;
        bool owns = true;
        for (int m = 0; m < surface.Tiles.Count; m++)
        {
            Igi2TerrainTile other = surface.Tiles[m];
            Fpu centreX = (Fpu)other.Centre.X - origin.X;
            float centreY = ((Fpu)other.Centre.Y - origin.Y).F;
            Fpu halfWidth = (Fpu)other.WorldWidth * 2048f;
            float halfHeight = ((Fpu)other.WorldHeight * 2048f).F;
            Fpu sampleWidth = (Fpu)1f / (((Fpu)(double)other.Width / halfWidth) * 0.5f);
            float sampleHeight = ((Fpu)1f / (((Fpu)(double)other.Height / halfHeight) * 0.5f)).F;
            Fpu halfSampleWidth = sampleWidth * 0.5f;
            if (!(across.Value > ((centreX - halfWidth) + halfSampleWidth).Value)
                || !(across.Value < ((halfWidth + centreX) - halfSampleWidth).Value))
            {
                continue;
            }

            Fpu along = (Fpu)y - origin.Y;
            Fpu halfSampleHeight = (Fpu)sampleHeight * 0.5f;
            if (along.Value > (((Fpu)centreY - halfHeight) + halfSampleHeight).Value
                && along.Value < (((Fpu)halfHeight + centreY) - halfSampleHeight).Value)
            {
                owns = m == tile;
            }
        }

        return owns;
    }

    /// <summary>
    /// A line against a triangle, <c>Terrain_LineVSTriangle</c> (<c>igi2 0x56FCE0</c>): where it crosses the
    /// triangle's plane, when that is between its ends and inside the triangle.
    /// </summary>
    private static bool LineVsTriangle(Vector3f v0, Vector3f v1, Vector3f v2, Vector3f start, Vector3f end, out Vector3f point)
    {
        point = default;
        Vector3f edge0 = Difference(v1, v0);
        Vector3f edge1 = Difference(v2, v0);
        Vector3f cross = new(
            (((Fpu)edge1.Z * edge0.Y) - ((Fpu)edge1.Y * edge0.Z)).F,
            (-(((Fpu)edge1.Z * edge0.X) - ((Fpu)edge1.X * edge0.Z))).F,
            (((Fpu)edge1.Y * edge0.X) - ((Fpu)edge1.X * edge0.Y)).F);
        Vector3f normal = cross;
        if (cross.X != 0f || cross.Y != 0f || cross.Z != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((Fpu)cross.X * cross.X) + ((Fpu)cross.Y * cross.Y) + ((Fpu)cross.Z * cross.Z));
            normal = new Vector3f(((Fpu)cross.X * inverse).F, ((Fpu)cross.Y * inverse).F, ((Fpu)cross.Z * inverse).F);
        }

        Vector3f delta = Difference(end, start);
        Vector3f relative = Difference(start, v0);
        if (!ProjectToPlane(relative, delta, normal, out Vector3f onPlane))
        {
            return false;
        }

        point = new Vector3f(((Fpu)onPlane.X + v0.X).F, ((Fpu)onPlane.Y + v0.Y).F, ((Fpu)onPlane.Z + v0.Z).F);
        return (point.X > start.X) != (point.X >= end.X)
            && (point.Y > start.Y) != (point.Y >= end.Y)
            && (point.Z > start.Z) != (point.Z >= end.Z)
            && OnTriangle(onPlane, edge1, edge0);
    }

    /// <summary>
    /// Where a line crosses a plane through the origin, <c>Real32x3_ProjectPointToPlane</c> (<c>igi2 0x561A70</c>).
    /// </summary>
    private static bool ProjectToPlane(Vector3f relative, Vector3f delta, Vector3f normal, out Vector3f onPlane)
    {
        onPlane = default;
        Fpu denominator = ((Fpu)delta.Y * normal.Y) + ((Fpu)delta.X * normal.X) + ((Fpu)normal.Z * delta.Z);
        if (!(denominator.Value > ParallelTolerance) && !(denominator.Value < -ParallelTolerance))
        {
            return false;
        }

        Fpu along = (((Fpu)relative.Z * normal.Z) + ((Fpu)relative.X * normal.X) + ((Fpu)relative.Y * normal.Y)) / denominator;
        onPlane = new Vector3f(
            ((Fpu)relative.X - (along * delta.X)).F,
            ((Fpu)relative.Y - (along * delta.Y)).F,
            ((Fpu)relative.Z - (along * delta.Z)).F);
        return true;
    }

    /// <summary>
    /// Whether a point on a triangle's plane is inside it, <c>Real32x3_PointOnTriangle</c> (<c>igi2 0x561B70</c>):
    /// the triangle is flattened onto the axis plane its normal leans on most and solved in two dimensions.
    /// </summary>
    /// <param name="point">The point, relative to the triangle's first corner.</param>
    /// <param name="a">The edge to the third corner.</param>
    /// <param name="b">The edge to the second corner.</param>
    private static bool OnTriangle(Vector3f point, Vector3f a, Vector3f b)
    {
        float nx = (((Fpu)a.Z * b.Y) - ((Fpu)b.Z * a.Y)).F;
        float ny = (-(((Fpu)b.X * a.Z) - ((Fpu)b.Z * a.X))).F;
        float nz = (((Fpu)b.X * a.Y) - ((Fpu)a.X * b.Y)).F;
        float ax = nx < 0f ? -nx : nx, ay = ny < 0f ? -ny : ny, az = nz < 0f ? -nz : nz;
        if (ax > ay && ax > az)
        {
            return OnTriangle(point.Y, point.Z, b.Y, b.Z, a.Y, a.Z);
        }

        if (ay > az)
        {
            return OnTriangle(point.X, point.Z, b.X, b.Z, a.X, a.Z);
        }

        return az != 0f && OnTriangle(point.X, point.Y, b.X, b.Y, a.X, a.Y);
    }

    /// <summary>
    /// The two-dimensional test, <c>Real32x2_PointOnTriangle</c> (<c>igi2 0x5616B0</c>) over
    /// <c>Real32x4_InvertSafe</c> (<c>igi2 0x562590</c>): the point in the two edges' coordinates, strictly inside.
    /// </summary>
    private static bool OnTriangle(float px, float py, float bx, float by, float ax, float ay)
    {
        Fpu determinant = ((Fpu)ay * bx) - ((Fpu)ax * by);
        if (determinant.Value == 0.0)
        {
            return false;
        }

        Fpu inverse = (Fpu)1f / determinant;
        float m00 = ((Fpu)ay * inverse).F, m01 = (-((Fpu)by * inverse)).F;
        float m10 = (-((Fpu)ax * inverse)).F, m11 = ((Fpu)bx * inverse).F;
        float u = (((Fpu)m00 * px) + ((Fpu)m10 * py)).F;
        float v = (((Fpu)m01 * px) + ((Fpu)m11 * py)).F;
        return u > 0f && v > 0f && ((Fpu)v + u).Value < 1.0;
    }

    private static Vector3f Difference(Vector3f a, Vector3f b) =>
        new(((Fpu)a.X - b.X).F, ((Fpu)a.Y - b.Y).F, ((Fpu)a.Z - b.Z).F);

    /// <summary>One tile as the line handler frames it, and the walk of a line over its samples.</summary>
    private readonly struct Tile
    {
        public Tile(Igi2TerrainTile tile, Vector3d origin)
        {
            Source = tile;
            Fpu halfWidth = (Fpu)tile.WorldWidth * 2048f;
            HalfWidth = halfWidth.Value;
            HalfHeight = ((Fpu)tile.WorldHeight * 2048f).F;
            ScaleX = ((Fpu)(double)tile.Width / (halfWidth + halfWidth)).F;
            ScaleY = ((Fpu)(double)tile.Height / ((Fpu)HalfHeight + HalfHeight)).F;
            MapX = (((Fpu)tile.Centre.X - origin.X) - halfWidth).F;
            MapY = (((Fpu)tile.Centre.Y - origin.Y) - HalfHeight).F;
        }

        public Igi2TerrainTile Source { get; }

        /// <summary>Gets half the tile's width, held at the FPU's precision.</summary>
        public double HalfWidth { get; }

        public float HalfHeight { get; }

        public float ScaleX { get; }

        public float ScaleY { get; }

        /// <summary>Gets the tile's west edge, relative to the terrain.</summary>
        public float MapX { get; }

        /// <summary>Gets the tile's south edge, relative to the terrain.</summary>
        public float MapY { get; }

        /// <summary>Walks one line over the tile; the nearest kept crossing, in sample space.</summary>
        public bool Trace(Igi2TerrainSurface surface, Vector3d lineStart, Vector3d lineEnd, out Vector3f hit)
        {
            hit = default;
            // The tile's own Z is the terrain's: TerrainMap_UpdateInternalDataHandler (igi2 0x57567F, 0x57580F) copies
            // it into the tile's position when the tile loads, whatever the script placed it at.
            float sx = ToColumn(lineStart.X), sy = ToRow(lineStart.Y), sz = ((Fpu)lineStart.Z - Source.BaseHeight).F;
            float ex = ToColumn(lineEnd.X), ey = ToRow(lineEnd.Y), ez = ((Fpu)lineEnd.Z - Source.BaseHeight).F;
            float lastColumn = Source.Width - 1, lastRow = Source.Height - 1;

            // The start, then the end, clipped to the grid; a line wholly off one side misses.
            if (!ClipBelow(ref sx, ref sy, ref sz, ex, ey, ez)
                || !ClipAbove(ref sx, ref sy, ref sz, ex, ey, ez, lastColumn)
                || !ClipBelow(ref sy, ref sx, ref sz, ey, ex, ez)
                || !ClipAbove(ref sy, ref sx, ref sz, ey, ex, ez, lastRow)
                || !ClipBelow(ref ex, ref ey, ref ez, sx, sy, sz)
                || !ClipAbove(ref ex, ref ey, ref ez, sx, sy, sz, lastColumn)
                || !ClipBelow(ref ey, ref ex, ref ez, sy, sx, sz)
                || !ClipAbove(ref ey, ref ex, ref ez, sy, sx, sz, lastRow))
            {
                return false;
            }

            Vector3f start = new(sx, sy, sz), end = new(ex, ey, ez);
            Search search = new(this, surface, start, end);
            Fpu dx = (Fpu)ex - sx, dy = (Fpu)ey - sy;
            Fpu sizeX = dx.Value < 0.0 ? -dx : dx, sizeY = dy.Value < 0.0 ? -dy : dy;
            if (sizeX.Value > sizeY.Value)
            {
                search.AlongColumns(dx, dy);
            }
            else
            {
                search.AlongRows(dx, dy);
            }

            hit = search.Hit;
            return search.IsHit;
        }

        /// <summary>Where a cell's two triangles are crossed, in sample space; the first kept crossing wins.</summary>
        public bool Cell(Igi2TerrainSurface surface, int column, int row, Vector3f start, Vector3f end, ref float nearest, out Vector3f hit)
        {
            hit = default;
            int lastColumn = Source.Width - 1, lastRow = Source.Height - 1;
            int x0 = Math.Min(column, lastColumn), x1 = Math.Min(column + 1, lastColumn);
            int y0 = Math.Min(row, lastRow), y1 = Math.Min(row + 1, lastRow);
            Span<Vector3f> corner =
            [
                new(x0, y0, Source.Heights.GetHeight(x0, y0)),
                new(x1, y0, Source.Heights.GetHeight(x1, y0)),
                new(x0, y1, Source.Heights.GetHeight(x0, y1)),
                new(x1, y1, Source.Heights.GetHeight(x1, y1)),
            ];

            // Split along the flatter diagonal: 0-3 when its rise is no more than 1-2's.
            Fpu rise03 = (Fpu)corner[0].Z - corner[3].Z, rise12 = (Fpu)corner[1].Z - corner[2].Z;
            Fpu size03 = rise03.Value < 0.0 ? -rise03 : rise03, size12 = rise12.Value < 0.0 ? -rise12 : rise12;
            ReadOnlySpan<int> triangles = size03.Value <= size12.Value ? [0, 1, 3, 0, 3, 2] : [0, 1, 2, 2, 1, 3];
            for (int t = 0; t < 6; t += 3)
            {
                if (!LineVsTriangle(corner[triangles[t]], corner[triangles[t + 1]], corner[triangles[t + 2]], start, end, out Vector3f point))
                {
                    continue;
                }

                Fpu ox = (Fpu)point.X - start.X, oy = (Fpu)point.Y - start.Y, oz = (Fpu)point.Z - start.Z;
                float distance = Fpu.Sqrt((ox * ox) + (oy * oy) + (oz * oz)).F;
                if (!(distance < nearest))
                {
                    continue;
                }

                float relativeX = ((((Fpu)point.X + HalfSample) / ScaleX) + MapX).F;
                float relativeY = ((((Fpu)point.Y + HalfSample) / ScaleY) + MapY).F;
                if (Igi2DiscardBox.AnyContains(surface.DiscardBoxes, relativeX, relativeY))
                {
                    continue;
                }

                nearest = distance;
                hit = point;
                return true;
            }

            return false;
        }

        private float ToColumn(double x) => (((((Fpu)x - Source.Centre.X) + HalfWidth) * ScaleX) - HalfSample).F;

        private float ToRow(double y) => (((((Fpu)y - Source.Centre.Y) + (double)HalfHeight) * ScaleY) - HalfSample).F;

        /// <summary>Clips one end below zero on an axis, sliding the other two coordinates with it.</summary>
        private static bool ClipBelow(ref float axis, ref float other, ref float z, float farAxis, float farOther, float farZ)
        {
            if (!(axis < 0f))
            {
                return true;
            }

            if (farAxis < 0f)
            {
                return false;
            }

            Fpu t = -(Fpu)axis / ((Fpu)farAxis - axis);
            axis = 0f;
            other = ((((Fpu)farOther - other) * t) + other).F;
            z = ((((Fpu)farZ - z) * t) + z).F;
            return true;
        }

        /// <summary>Clips one end beyond the last sample on an axis.</summary>
        private static bool ClipAbove(ref float axis, ref float other, ref float z, float farAxis, float farOther, float farZ, float last)
        {
            if (!(axis > last))
            {
                return true;
            }

            if (farAxis > last)
            {
                return false;
            }

            Fpu t = ((Fpu)last - axis) / ((Fpu)farAxis - axis);
            axis = last;
            other = ((((Fpu)farOther - other) * t) + other).F;
            z = ((((Fpu)farZ - z) * t) + z).F;
            return true;
        }
    }

    /// <summary>The walk of one line over a tile's cells, keeping the nearest crossing.</summary>
    private ref struct Search
    {
        private readonly Tile _tile;
        private readonly Igi2TerrainSurface _surface;
        private readonly Vector3f _start;
        private readonly Vector3f _end;
        private float _nearest;

        public Search(Tile tile, Igi2TerrainSurface surface, Vector3f start, Vector3f end)
        {
            _tile = tile;
            _surface = surface;
            _start = start;
            _end = end;
            _nearest = float.MaxValue;
        }

        public bool IsHit { get; private set; }

        public Vector3f Hit { get; private set; }

        /// <summary>Steps a column at a time, testing the row the line is in and the next one it reaches.</summary>
        public void AlongColumns(Fpu dx, Fpu dy)
        {
            int step = dx.Value < 0.0 ? -1 : 1;
            int column = Fpu.ToInt32(_start.X);
            Fpu slope = dy / dx;
            float row = ((((Fpu)(double)column - _start.X) * slope) + _start.Y).F;
            float stepF = step;
            float rowStep = ((Fpu)stepF * slope).F;
            int stop = Fpu.ToInt32(_end.X) + step;
            if (column == stop)
            {
                return;
            }

            float nextRowStep = ((Fpu)stepF * rowStep).F;
            do
            {
                int at = Fpu.ToInt32(row);
                at = at < 0 ? 0 : Math.Min(at, _tile.Source.Height);
                column = column < 0 ? 0 : Math.Min(column, _tile.Source.Width);
                Test(column, at);

                int next = Fpu.ToInt32((Fpu)nextRowStep + row);
                if (next != at && next >= 0 && next < _tile.Source.Height)
                {
                    Test(column, next);
                }

                row = ((Fpu)row + rowStep).F;
                column += step;
            }
            while (column != stop);
        }

        /// <summary>Steps a row at a time, testing the column the line is in and the next one it reaches.</summary>
        public void AlongRows(Fpu dx, Fpu dy)
        {
            int step = dy.Value < 0.0 ? -1 : 1;
            Fpu slope = dy.Value != 0.0 ? dx / dy : (Fpu)0f;
            int row = Fpu.ToInt32(_start.Y);
            float column = ((((Fpu)(double)row - _start.Y) * slope) + _start.X).F;
            float stepF = step;
            float columnStep = (slope * stepF).F;
            int stop = Fpu.ToInt32(_end.Y) + step;
            if (row == stop)
            {
                return;
            }

            float nextColumnStep = ((Fpu)columnStep * stepF).F;
            do
            {
                int at = Fpu.ToInt32(column);
                row = row < 0 ? 0 : Math.Min(row, _tile.Source.Height);
                at = at < 0 ? 0 : Math.Min(at, _tile.Source.Width);
                Test(at, row);

                int next = Fpu.ToInt32((Fpu)column + nextColumnStep);
                if (next != at && next >= 0 && next < _tile.Source.Width)
                {
                    Test(next, row);
                }

                column = ((Fpu)column + columnStep).F;
                row += step;
            }
            while (row != stop);
        }

        private void Test(int column, int row)
        {
            if (_tile.Cell(_surface, column, row, _start, _end, ref _nearest, out Vector3f point))
            {
                IsHit = true;
                Hit = point;
            }
        }
    }
}
