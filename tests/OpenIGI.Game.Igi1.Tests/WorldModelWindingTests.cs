using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// A model is wound by its stored triangle order, the order the original culls to, whatever its normals say (issue #50).
/// </summary>
/// <remarks>
/// Wound to its normals instead, a model whose normals point in -- every ladder, the doors, the watchtower -- came out inside
/// out, and the preset that culls back faces drew its inside.
/// </remarks>
public sealed class WorldModelWindingTests
{
    /// <summary>
    /// Mission 1's ladder, door, watchtower and a run of chain fence: each is built of exactly its stored triangles turned
    /// over, and the first three carry their normals into the model, so a build that followed the normals turned them inside
    /// out.
    /// </summary>
    [RequiresGameDataTheory]
    [InlineData("314_01_1", true)]
    [InlineData("304_01_1", true)]
    [InlineData("405_01_1", true)]
    [InlineData("303_01_1", false)]
    public void Mission_ones_models_are_wound_as_stored(string name, bool normalsPointIn)
    {
        MefModel model = Model(name);
        MeshGeometry geometry = MefMeshBuilder.Build(model)!;

        List<(int, int, int)> stored = [];
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> run = record.Indices.Span;
            for (int t = 0; t + 2 < run.Length; t += 3)
            {
                stored.Add(Canonical(record.ToVertexIndex(run[t]), record.ToVertexIndex(run[t + 2]), record.ToVertexIndex(run[t + 1])));
            }
        }

        List<(int, int, int)> built = [];
        int facingNormals = 0;
        for (int t = 0; t + 2 < geometry.Indices.Length; t += 3)
        {
            int a = geometry.Indices[t], b = geometry.Indices[t + 1], c = geometry.Indices[t + 2];
            built.Add(Canonical(a, b, c));
            Vertex3D[] v = geometry.Vertices;
            Vector3f face = Vector3f.Cross(v[b].Position - v[a].Position, v[c].Position - v[a].Position);
            if (Vector3f.Dot(face, v[a].Normal + v[b].Normal + v[c].Normal) > 0f)
            {
                facingNormals++;
            }
        }

        // Coincident skins are moved to the end of their subset, so the triangles are compared as a set, not in order.
        Assert.Equal(stored.Order(), built.Order());
        Assert.Equal(normalsPointIn ? 0 : built.Count, facingNormals);
    }

    private static (int, int, int) Canonical(int a, int b, int c) =>
        a <= b && a <= c ? (a, b, c) : b <= c ? (b, c, a) : (c, a, b);

    private static MefModel Model(string name)
    {
        string path = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "models", "level1.res");
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
        IresEntry entry = archive.Entries.Single(e => Path.GetFileNameWithoutExtension(e.Name.Replace('\\', '/')) == name);
        return MefModel.Read(entry.Data);
    }
}
