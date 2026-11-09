using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>The heaps buildings brought down leave: a mound of broken concrete on each footprint, strewn with chips of it.</summary>
public sealed partial class BlastDebris
{
    // The mound's mesh: rings out from its crown, and spokes round it.
    private const int MoundRings = 10;
    private const int MoundSpokes = 40;

    // How many chips lie strewn over a heap at the full tier; the light tier strews none.
    private const int HeapChips = 260;

    private readonly List<RubbleHeap> _heaps = [];

    // A heap's mesh and how it is drawn: the mound and the concrete chips with the broken-concrete texture, the rest with
    // the building's own.
    private void AddHeap(RubbleMound mound, ShatterPlan plan)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];
        Mound(mound, vertices, indices);

        var random = new IgiRandom(mound.Seed ^ 0xC41B5u);
        int chips = _budget.CellScale >= 1f ? HeapChips : 0;
        List<ushort> faced = [];
        for (int i = 0; i < chips; i++)
        {
            Chip(mound, random, vertices, (i & 1) == 0 ? indices : faced);
        }

        int buildingOffset = indices.Count;
        indices.AddRange(faced);
        if (indices.Count == 0)
        {
            return;
        }

        MeshHandle mesh = device.CreateMesh([.. vertices], [.. indices]);
        if (!mesh.IsValid)
        {
            return;
        }

        _heaps.Add(new RubbleHeap(mound, mesh, buildingOffset, faced.Count, MainTexture(plan.Source)));
    }

    private void TickHeaps()
    {
        foreach (RubbleHeap heap in _heaps)
        {
            heap.Mound.Tick();
        }
    }

    private void ClearHeaps()
    {
        foreach (RubbleHeap heap in _heaps)
        {
            device.DestroyMesh(heap.Mesh);
        }

        _heaps.Clear();
    }

    private void DrawHeaps(in Camera3D camera, float blend)
    {
        foreach (RubbleHeap heap in _heaps)
        {
            float risen = heap.Mound.Risen(blend);
            if (risen <= 0.002f)
            {
                continue;
            }

            Matrix3f rise = Matrix3f.FromRows(new Vector3f(1f, 0f, 0f), new Vector3f(0f, 1f, 0f), new Vector3f(0f, 0f, risen));
            Matrix4f modelView = camera.CreateModelView(heap.Mound.GroundUnits / WorldUnits.PerMetre, rise);
            device.SetMaterialOverride(new SurfaceMaterial(Roughness: 1f));
            device.DrawMesh(heap.Mesh, 0, heap.BuildingOffset, modelView, _concrete, TextureHandle.None, BlendMode.Opaque);
            if (heap.BuildingCount > 0)
            {
                TextureHandle faces = heap.Texture.IsValid ? heap.Texture : _concrete;
                device.DrawMesh(heap.Mesh, heap.BuildingOffset, heap.BuildingCount, modelView, faces, TextureHandle.None, BlendMode.Opaque);
            }
        }

        device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    // The mound: a crown and rings out to its ragged edge, which dips a little under the ground so it meets it.
    private static void Mound(RubbleMound mound, List<Vertex3D> vertices, List<ushort> indices)
    {
        int first = vertices.Count;
        vertices.Add(MoundVertex(mound, 0.0, 0.0, 0f));
        for (int ring = 1; ring <= MoundRings; ring++)
        {
            double r = ring / (double)MoundRings;
            for (int spoke = 0; spoke < MoundSpokes; spoke++)
            {
                float angle = spoke / (float)MoundSpokes * MathF.Tau;
                double reach = mound.RadiusUnits * mound.EdgeAt(angle) * Math.Min(r, 0.995);
                float sink = ring == MoundRings ? -0.08f * UnitsPerMetre : 0f;
                vertices.Add(MoundVertex(mound, MathF.Cos(angle) * reach, MathF.Sin(angle) * reach, sink));
            }
        }

        for (int spoke = 0; spoke < MoundSpokes; spoke++)
        {
            int next = (spoke + 1) % MoundSpokes;
            indices.Add((ushort)first);
            indices.Add((ushort)(first + 1 + spoke));
            indices.Add((ushort)(first + 1 + next));
        }

        for (int ring = 1; ring < MoundRings; ring++)
        {
            int inner = first + 1 + ((ring - 1) * MoundSpokes);
            int outer = inner + MoundSpokes;
            for (int spoke = 0; spoke < MoundSpokes; spoke++)
            {
                int next = (spoke + 1) % MoundSpokes;
                indices.Add((ushort)(inner + spoke));
                indices.Add((ushort)(outer + spoke));
                indices.Add((ushort)(outer + next));
                indices.Add((ushort)(inner + spoke));
                indices.Add((ushort)(outer + next));
                indices.Add((ushort)(inner + next));
            }
        }

    }

    // A point of the mound, in metres about its middle on the ground, shaded darker in its hollows.
    private static Vertex3D MoundVertex(RubbleMound mound, double dx, double dy, float sinkUnits)
    {
        const double Step = 48.0;
        double h = mound.HeightAt(dx, dy);
        double sx = (mound.HeightAt(dx + Step, dy) - mound.HeightAt(dx - Step, dy)) / (2.0 * Step);
        double sy = (mound.HeightAt(dx, dy + Step) - mound.HeightAt(dx, dy - Step)) / (2.0 * Step);
        Vector3f normal = new Vector3f((float)-sx, (float)-sy, 1f).Normalized();
        float metresX = (float)(dx / UnitsPerMetre);
        float metresY = (float)(dy / UnitsPerMetre);
        float metresZ = (float)((h + sinkUnits) / UnitsPerMetre);

        // Hollows darker and dustier, crests lighter.
        double crown = h / Math.Max(mound.HeightUnits, 1.0);
        byte shade = (byte)Math.Clamp(150 + (90 * crown) + (normal.Z * 20f), 0, 255);
        return new Vertex3D(new Vector3f(metresX, metresY, metresZ), normal, metresX * 0.45f, metresY * 0.45f, colour: new Bgra32(shade, shade, shade, 255));
    }

    // One chip on the heap: an irregular little block, turned any way, half sunk in the mound.
    private static void Chip(RubbleMound mound, IgiRandom random, List<Vertex3D> vertices, List<ushort> indices)
    {
        if (vertices.Count + 24 > ushort.MaxValue)
        {
            return;
        }

        float angle = random.NextSingle() * MathF.Tau;
        double r = Math.Sqrt(random.NextSingle()) * 1.15;
        double reach = mound.RadiusUnits * mound.EdgeAt(angle) * r;
        double dx = MathF.Cos(angle) * reach;
        double dy = MathF.Sin(angle) * reach;
        float size = random.NextSingle() < 0.1f ? 0.35f + (random.NextSingle() * 0.3f) : 0.06f + (random.NextSingle() * 0.24f);
        Vector3f half = new(size * (0.6f + (random.NextSingle() * 0.6f)), size * (0.5f + (random.NextSingle() * 0.5f)), size * (0.25f + (random.NextSingle() * 0.4f)));
        Vector3d axis = new(random.NextSingle() - 0.5, random.NextSingle() - 0.5, random.NextSingle() - 0.5);
        Matrix3f turn = Matrix3f.RotationAxis(axis.LengthSquared > 1e-6 ? axis / axis.Length : Vector3d.UnitZ, random.NextSingle() * MathF.Tau);
        Vector3f centre = new((float)(dx / UnitsPerMetre), (float)(dy / UnitsPerMetre), (float)(mound.HeightAt(dx, dy) / UnitsPerMetre) + (half.Z * 0.2f));
        byte shade = (byte)(150 + random.NextBounded(100));
        var colour = new Bgra32(shade, shade, shade, 255);
        float u0 = random.NextSingle() * 4f;
        float v0 = random.NextSingle() * 4f;

        // The six faces, each its own four corners so its normal is its own.
        for (int axisIndex = 0; axisIndex < 3; axisIndex++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3f n = axisIndex switch { 0 => Vector3f.UnitX, 1 => Vector3f.UnitY, _ => Vector3f.UnitZ } * sign;
                Vector3f a = axisIndex switch { 0 => Vector3f.UnitY, 1 => Vector3f.UnitZ, _ => Vector3f.UnitX };
                Vector3f b = Vector3f.Cross(n, a);
                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float ca = corner is 0 or 3 ? -1f : 1f;
                    float cb = corner < 2 ? -1f : 1f;
                    Vector3f local = Scale(n + (a * ca) + (b * cb), half);
                    vertices.Add(new Vertex3D(
                        centre + turn.Transform(local),
                        turn.Transform(n),
                        u0 + (ca * size * 0.5f),
                        v0 + (cb * size * 0.5f),
                        colour: colour));
                }

                indices.Add((ushort)first);
                indices.Add((ushort)(first + 1));
                indices.Add((ushort)(first + 2));
                indices.Add((ushort)first);
                indices.Add((ushort)(first + 2));
                indices.Add((ushort)(first + 3));
            }
        }
    }

    private static Vector3f Scale(Vector3f v, Vector3f by) => new(v.X * by.X, v.Y * by.Y, v.Z * by.Z);

    // The texture most of a building is drawn with: its walls'.
    private static TextureHandle MainTexture(ShatterSource source)
    {
        MeshGeometry mesh = source.Geometry;
        int best = -1;
        int most = 0;
        for (int s = 0; s < mesh.Subsets.Length && s < source.SubsetTextures.Length; s++)
        {
            if (mesh.Subsets[s].IndexCount > most && source.SubsetTextures[s].IsValid)
            {
                most = mesh.Subsets[s].IndexCount;
                best = s;
            }
        }

        return best >= 0 ? source.SubsetTextures[best] : TextureHandle.None;
    }

    private sealed record RubbleHeap(RubbleMound Mound, MeshHandle Mesh, int BuildingOffset, int BuildingCount, TextureHandle Texture);
}
