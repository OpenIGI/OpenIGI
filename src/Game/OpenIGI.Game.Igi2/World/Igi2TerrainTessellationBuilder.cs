using System.Numerics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One traversal of an <see cref="Igi2TerrainLod"/> quadtree: the explicit square stack, the shared-edge split cache
/// and the vertex and index lists a frame's <see cref="Igi2TerrainTessellation"/> is assembled from.
/// </summary>
/// <remarks>
/// A one-off tessellation gets a fresh builder; a frame tessellation keeps one per tile and reuses its buffers, forgetting
/// every decision between runs. The tile, its height bounds and its pass slots are read from the owning quadtree.
/// </remarks>
internal sealed class Igi2TerrainTessellationBuilder
{
    // (0,0) (1,0) (1,1) (0,1), in units of the square's own size. Set up at 0x66869D as two interleaved
    // arrays of four; this is the same table read the same way.
    private static readonly int[] CornerX = [0, 1, 1, 0];
    private static readonly int[] CornerY = [0, 0, 1, 1];

    // The four sides then the diagonal, from the table built at 0x668E3D. The diagonal's midpoint is the
    // square's centre, which is what makes five edges enough to describe a four-way subdivision.
    private static readonly int[] EdgeFrom = [0, 1, 2, 3, 1];
    private static readonly int[] EdgeTo = [1, 2, 3, 0, 3];

    private readonly Igi2TerrainLod _owner;
    private Igi2TerrainView _view;
    private Igi2TerrainLodSettings _settings;
    private readonly bool _reuseBuffers;
    private readonly Stack<Square> _stack = new();
    private readonly List<Igi2TerrainPass> _passes = [];
    private readonly float[][] _passWeights;
    private readonly int[][] _passIndices;
    private int[] _groundBuffer = [];

    private readonly List<Vector3d> _positions = [];
    private readonly List<Vector3f> _normals = [];
    private readonly List<double> _trueZ = [];
    private readonly List<Bgra32> _lights = [];
    private readonly List<bool> _discarded = [];
    private readonly List<float> _weights = [];
    private readonly Dictionary<long, int> _vertices = [];
    private readonly Dictionary<long, EdgeDecision> _edges = [];
    private readonly List<List<int>> _indices = [];
    private readonly List<List<int>> _baseIndices = [];

    // Every triangle drawn, for the terrain map's pass; see Igi2TerrainMesh.GroundIndices.
    private readonly List<int> _ground = [];

    private int _culled;
    private int _visited;
    private int _drawn;
    private int _uncovered;
    private long _covered;
    private long _superseded;

    public Igi2TerrainTessellationBuilder(Igi2TerrainLod owner, bool reuseBuffers)
    {
        _owner = owner;
        _reuseBuffers = reuseBuffers;
        int slots = owner.SlotMaterials.Count;
        _passWeights = new float[slots][];
        _passIndices = new int[slots][];
        for (int i = 0; i < slots; i++)
        {
            _indices.Add([]);
            _baseIndices.Add([]);
            _passWeights[i] = [];
            _passIndices[i] = [];
        }
    }

    public Igi2TerrainTessellation Run(in Igi2TerrainView view, in Igi2TerrainLodSettings settings)
    {
        // pc_terrainrender.c TerrainRender_Draw refills retained render buffers each frame.
        // Reuse storage, but forget every decision: clipping, morphs and detail still run for this view.
        _view = view;
        _settings = settings;
        _positions.Clear();
        _normals.Clear();
        _trueZ.Clear();
        _lights.Clear();
        _discarded.Clear();
        _weights.Clear();
        _vertices.Clear();
        _edges.Clear();
        _ground.Clear();
        _passes.Clear();
        _stack.Clear();
        _culled = _visited = _drawn = _uncovered = 0;
        _covered = _superseded = 0;
        for (int i = 0; i < _indices.Count; i++)
        {
            _indices[i].Clear();
            _baseIndices[i].Clear();
        }

        int n = _owner.RootSize;
        Stack<Square> stack = _stack;
        Push(stack, new Square(0, 0, n, Vertex(0, 0), Vertex(n, 0), Vertex(n, n), Vertex(0, n)));

        Span<int> midpoint = stackalloc int[5];
        Span<float> morph = stackalloc float[5];

        while (stack.Count > 0)
        {
            Square square = stack.Pop();
            _visited++;

            // nSize <= (1 << nShift) at 0x668F48: a square already at the sample grid cannot be
            // subdivided, whatever the detail test would like. nShift is the submap shift, zero for
            // a tile the port loads on its own.
            if (square.Size <= 1)
            {
                EmitLeaf(square);
                continue;
            }

            int splits = 0;
            for (int e = 0; e < 5; e++)
            {
                int a = square.Corner(EdgeFrom[e]);
                int b = square.Corner(EdgeTo[e]);
                EdgeDecision decision = Decide(square, e, a, b);
                morph[e] = decision.Split ? decision.Morph : 0f;
                splits += decision.Split ? 1 : 0;
            }

            if (splits == 0)
            {
                EmitLeaf(square);
                continue;
            }

            for (int e = 0; e < 5; e++)
            {
                int a = square.Corner(EdgeFrom[e]);
                int b = square.Corner(EdgeTo[e]);
                (int mx, int my) = Midpoint(square, e);
                midpoint[e] = SplitVertex(mx, my, a, b, morph[e]);
            }

            int half = square.Size / 2;
            Push(stack, new Square(square.X, square.Y, half, square.V0, midpoint[0], midpoint[4], midpoint[3]));
            Push(stack, new Square(square.X + half, square.Y, half, midpoint[0], square.V1, midpoint[1], midpoint[4]));
            Push(stack, new Square(square.X + half, square.Y + half, half, midpoint[4], midpoint[1], square.V2, midpoint[2]));
            Push(stack, new Square(square.X, square.Y + half, half, midpoint[3], midpoint[4], midpoint[2], square.V3));
        }

        return new Igi2TerrainTessellation(BuildMesh(), _drawn, _culled, _visited, _covered, _superseded);
    }

    private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    private void Push(Stack<Square> stack, Square square)
    {
        // Ahead of the clipping planes, because it is cheaper and because it is not a decision about
        // this frame: a square a finer tile covers is never this tile's to draw, from any camera.
        if (_owner.Tile.IsSuperseded(square.X, square.Y, square.Size))
        {
            _superseded += (long)square.Size * square.Size;
            return;
        }

        if (IsOutOfSight(square))
        {
            _culled++;
            return;
        }

        stack.Push(square);
    }

    private static (int X, int Y) Midpoint(Square square, int edge)
    {
        int ax = square.X + (CornerX[EdgeFrom[edge]] * square.Size);
        int ay = square.Y + (CornerY[EdgeFrom[edge]] * square.Size);
        int bx = square.X + (CornerX[EdgeTo[edge]] * square.Size);
        int by = square.Y + (CornerY[EdgeTo[edge]] * square.Size);
        return ((ax + bx) / 2, (ay + by) / 2);
    }

    private int Vertex(int x, int y)
    {
        long key = ((long)y * (_owner.RootSize + 1)) + x;
        if (_vertices.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int index = _positions.Count;
        _vertices[key] = index;

        Vector3d position = _owner.PositionOf(x, y);
        _positions.Add(position);
        _normals.Add(_owner.NormalAt(x, y));
        _trueZ.Add(position.Z);
        _lights.Add(_owner.LightAt(x, y));
        _discarded.Add(_owner.IsDiscarded(position.X, position.Y));

        byte material = _owner.MaterialAt(x, y);
        int slot = _owner.SlotOf[material];
        for (int i = 0; i < _owner.SlotMaterials.Count; i++)
        {
            _weights.Add(i == slot ? 1f : 0f);
        }

        _uncovered += slot < 0 ? 1 : 0;
        return index;
    }

    private int SplitVertex(int x, int y, int a, int b, float morph)
    {
        long key = ((long)y * (_owner.RootSize + 1)) + x;
        if (_vertices.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int index = _positions.Count;
        _vertices[key] = index;

        double trueZ = _owner.HeightAt(x, y);

        // TerrainRender_CalculateRenderVertex, 0x66A76D: the new vertex starts at the height field
        // and is pulled back toward the straight line between its two parents by a raised cosine,
        // applied to Z alone. At morph zero it lands exactly on that line, which is what lets an
        // unsplit edge carry a vertex without showing one.
        double parentMean = (_positions[a].Z + _positions[b].Z) * 0.5;
        double z = parentMean + (Igi2TerrainLodSettings.MorphFraction(morph) * (trueZ - parentMean));

        _positions.Add(new Vector3d(_owner.EdgeX(x), _owner.EdgeY(y), z));
        _normals.Add(_owner.NormalAt(x, y));
        _trueZ.Add(trueZ);
        _lights.Add(_owner.LightAt(x, y));
        _discarded.Add(_owner.IsDiscarded(_owner.EdgeX(x), _owner.EdgeY(y)));

        // 0x66A7C4: the material weights morph too, and four times as fast -- the vertex takes the
        // mean of its parents' weights scaled by (1 - w) and adds w to its own material, with
        // w = min(1, morph * vMaterialMorphSpeed). So the surface has finished changing by the time
        // the geometry is halfway up.
        float weight = MathF.Min(1f, morph * Igi2TerrainLodSettings.MaterialMorphSpeed);
        int slots = _owner.SlotMaterials.Count;
        int slot = _owner.SlotOf[_owner.MaterialAt(x, y)];
        for (int i = 0; i < slots; i++)
        {
            float inherited = (_weights[(a * slots) + i] + _weights[(b * slots) + i]) * (1f - weight) * 0.5f;
            _weights.Add(i == slot ? inherited + weight : inherited);
        }

        _uncovered += slot < 0 ? 1 : 0;
        return index;
    }

    private EdgeDecision Decide(Square square, int edge, int a, int b)
    {
        long key = EdgeKey(a, b);
        if (_edges.TryGetValue(key, out EdgeDecision cached))
        {
            return cached;
        }

        EdgeDecision decision = Evaluate(square, edge, a, b);
        _edges[key] = decision;
        return decision;
    }

    private EdgeDecision Evaluate(Square square, int edge, int a, int b)
    {
        Vector3d eye = _view.Eye;
        Vector3d p0 = _positions[a];
        Vector3d p1 = _positions[b];

        float ax = (float)(p0.X - eye.X);
        float ay = (float)(p0.Y - eye.Y);
        float az = (float)(p0.Z - eye.Z);
        float dx = (float)(p1.X - p0.X);
        float dy = (float)(p1.Y - p0.Y);
        float dz = (float)(p1.Z - p0.Z);

        // 0x66921F: a near-vertical edge is refused before anything else is computed. Its midpoint
        // is already where the height field wants it, so subdividing spends triangles on nothing.
        float steep = Igi2TerrainLodSettings.SteepEdgeRatio;
        if (MathF.Abs(dz) > steep * MathF.Abs(dx) && MathF.Abs(dz) > steep * MathF.Abs(dy))
        {
            return new EdgeDecision(false, 0f);
        }

        float lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared <= Igi2TerrainLodSettings.MinimumSplitEdgeLengthSquared)
        {
            return new EdgeDecision(false, 0f);
        }

        // 0x669049: the parameter of the point on the segment nearest the eye, clamped to the
        // segment. An edge running away from the viewer is judged by whichever end is closest.
        float t = Math.Clamp(-(((ax * dx) + (ay * dy) + (az * dz)) / lengthSquared), 0f, 1f);
        float cx = ax + (t * dx);
        float cy = ay + (t * dy);
        float cz = az + (t * dz);
        float distanceSquared = (cx * cx) + (cy * cy) + (cz * cz);

        float threshold = Threshold(square, edge, a, b);
        threshold = threshold * threshold * distanceSquared;
        if (lengthSquared <= threshold)
        {
            return new EdgeDecision(false, 0f);
        }

        // 0x6692A4: how far past the threshold the edge went, halved, is the fraction it has morphed
        // in by -- so a vertex is born flat and rises as its square keeps approaching.
        float morph = ((lengthSquared / threshold) - 1f) * Igi2TerrainLodSettings.MorphSpeed;
        if (!(morph > 0f))
        {
            return new EdgeDecision(false, 0f);
        }

        if (morph >= 1f)
        {
            return new EdgeDecision(true, 1f);
        }

        return new EdgeDecision(
            true,
            MathF.Round(morph * Igi2TerrainLodSettings.MorphResolution) / Igi2TerrainLodSettings.MorphResolution);
    }

    private float Threshold(Square square, int edge, int a, int b)
    {
        // 0x669160: how far the height field departs from the straight edge at its midpoint,
        // measured against the square's own width. Zero where the midpoint falls outside the map,
        // which the original reaches through an unsigned compare against nWidth and nHeight.
        (int mx, int my) = Midpoint(square, edge);
        float roughness = 0f;
        if ((uint)mx < (uint)_owner.Tile.Width && (uint)my < (uint)_owner.Tile.Height)
        {
            double mean = (_trueZ[a] + _trueZ[b]) * 0.5;
            double error = Math.Abs(mean - _owner.Tile.HeightAt(mx, my));
            roughness = (float)(error / (square.Size * _owner.Tile.TexelWidth));
        }

        return _settings.ThresholdFor(roughness);
    }

    private bool IsOutOfSight(Square square)
    {
        ReadOnlySpan<Vector3f> planes = _view.PlaneNormals;
        if (planes.IsEmpty)
        {
            return false;
        }

        int level = BitOperations.Log2((uint)square.Size);
        _owner.Bounds.GetBounds(level, square.X >> level, square.Y >> level, out float low, out float high);

        // 0x66AECF folds the four corners into the range as well, which is what covers the row the
        // block index cannot reach and any morph that has moved a corner off the height field.
        for (int corner = 0; corner < 4; corner++)
        {
            float z = (float)_positions[square.Corner(corner)].Z;
            low = MathF.Min(low, z);
            high = MathF.Max(high, z);
        }

        Vector3d eye = _view.Eye;
        float x0 = (float)(_owner.EdgeX(square.X) - eye.X);
        float x1 = (float)(_owner.EdgeX(square.X + square.Size) - eye.X);
        float y0 = (float)(_owner.EdgeY(square.Y) - eye.Y);
        float y1 = (float)(_owner.EdgeY(square.Y + square.Size) - eye.Y);
        float z0 = (float)(low - eye.Z);
        float z1 = (float)(high - eye.Z);

        foreach (Vector3f normal in planes)
        {
            bool allOutside = true;
            for (int corner = 0; corner < 8 && allOutside; corner++)
            {
                float x = (corner & 1) == 0 ? x0 : x1;
                float y = (corner & 2) == 0 ? y0 : y1;
                float z = (corner & 4) == 0 ? z0 : z1;
                allOutside = (x * normal.X) + (y * normal.Y) + (z * normal.Z) > 0f;
            }

            if (allOutside)
            {
                return true;
            }
        }

        return false;
    }

    private void EmitLeaf(Square square)
    {
        _drawn++;
        _covered += (long)square.Size * square.Size;

        // Two triangles across the square's own diagonal, the one its five edges already name. The
        // winding is Igi2TerrainMesh's, so the full-detail and tessellated paths agree; the
        // original's own leaf at 0x669D6B issues (v0, v1, v3) and takes CULLMODE = CW.
        Emit(square.V0, square.V3, square.V1);
        Emit(square.V1, square.V3, square.V2);
    }

    private void Emit(int a, int b, int c)
    {
        // 0x66AB24, ahead of the material loop: a face whose three corners all stand over a
        // DiscardTask is issued into no pass at all. The AND is what leaves the ring of triangles
        // straddling the box's edge in place, so the ground meets the building rather than the hole.
        if (_discarded[a] && _discarded[b] && _discarded[c])
        {
            return;
        }

        // Past that test the face is drawn, whatever material covers it or fails to: the original
        // issues it into the terrain map's own pass after the material loop and outside it. See
        // Igi2TerrainMesh.GroundIndices for the reading of 0x66AAB0 and what it costs to skip.
        _ground.Add(a);
        _ground.Add(b);
        _ground.Add(c);

        int slots = _owner.SlotMaterials.Count;
        bool based = false;
        for (int slot = 0; slot < slots; slot++)
        {
            // TerrainRender_DrawFace's OR (0x66AAB0): a triangle belongs to every pass that has any
            // weight at any of its three corners, so a triangle straddling a boundary is issued once
            // per material and each pass carries weight only where its own corners are.
            if (_weights[(a * slots) + slot] <= 0f
                && _weights[(b * slots) + slot] <= 0f
                && _weights[(c * slots) + slot] <= 0f)
            {
                continue;
            }

            // The first pass to reach the triangle writes it as the ground's opaque base and the
            // rest add over it -- see Igi2TerrainPass.BaseIndices for why the ground needs one.
            List<int> into = based ? _indices[slot] : _baseIndices[slot];
            based = true;
            into.Add(a);
            into.Add(b);
            into.Add(c);
        }
    }

    private Igi2TerrainMesh BuildMesh()
    {
        int slots = _owner.SlotMaterials.Count;
        int count = _positions.Count;
        List<Igi2TerrainPass> passes = _passes;

        for (int slot = 0; slot < slots; slot++)
        {
            if (_indices[slot].Count + _baseIndices[slot].Count == 0)
            {
                continue;
            }

            int id = _owner.SlotMaterials[slot];
            TerrainMaterialTask material = _owner.Materials[id];

            float[] weights;
            if (_reuseBuffers)
            {
                EnsureCapacity(ref _passWeights[slot], count);
                weights = _passWeights[slot];
            }
            else
            {
                weights = new float[count];
            }
            for (int i = 0; i < count; i++)
            {
                weights[i] = _weights[(i * slots) + slot];
            }

            int baseTriangles = _baseIndices[slot].Count / 3;
            List<int> indices = _baseIndices[slot];
            indices.AddRange(_indices[slot]);

            passes.Add(new Igi2TerrainPass(
                id,
                material.TextureScale,
                material.DetailTextureScale,
                _reuseBuffers ? CopyToBuffer(indices, ref _passIndices[slot]) : indices.ToArray(),
                baseTriangles,
                weights.AsMemory(0, count)));
        }

        return Igi2TerrainMesh.Create(
            _positions,
            _normals,
            _lights,
            passes,
            _reuseBuffers ? CopyToBuffer(_ground, ref _groundBuffer) : _ground.ToArray(),
            _owner.Tile.Width,
            _owner.Tile.Height,
            _uncovered);
    }

    private static ReadOnlyMemory<int> CopyToBuffer(List<int> values, ref int[] buffer)
    {
        EnsureCapacity(ref buffer, values.Count);
        values.CopyTo(buffer);
        return buffer.AsMemory(0, values.Count);
    }

    private static void EnsureCapacity<T>(ref T[] buffer, int count)
    {
        if (buffer.Length < count)
        {
            Array.Resize(ref buffer, Math.Max(count, Math.Max(16, buffer.Length * 2)));
        }
    }

    private readonly record struct Square(int X, int Y, int Size, int V0, int V1, int V2, int V3)
    {
        public int Corner(int index) => index switch
        {
            0 => V0,
            1 => V1,
            2 => V2,
            _ => index == 3 ? V3 : throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private readonly record struct EdgeDecision(bool Split, float Morph);
}
