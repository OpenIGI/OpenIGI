using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The sequel's collision rows, measured over every model its levels place.</summary>
/// <remarks>
/// <para>
/// Three of the four rows are wider here than in Project I.G.I., and the widening is the easy half.
/// <c>MultiMesh_Face_t</c> is 12 bytes rather than 8 (an <c>iLightmap</c> and a pad after
/// <c>iMaterial</c>), <c>MultiMesh_Vertex_t</c> 20 rather than 16 (the trailing union carries a
/// lightmap UV pair as well as a bone), and <c>MultiMesh_Material_t</c> 16 rather than 12.
/// </para>
/// <para>
/// <b>The hard half is inside the material row.</b> <c>iBumpMap</c> and <c>iEnvMap</c> were inserted at
/// offsets 8 and 10, which pushes <c>iMaterial</c> — the global game-material row, the field that tells
/// glass from a wall — from offset 8 to <b>12</b>. A reader that widened the stride and left the offset
/// alone would read the bump-map index instead: a small number, indexing a real table, wrong in a way
/// nothing downstream could notice.
/// </para>
/// <para>
/// So both halves are asserted, and both are asserted over the corpus rather than over one model. The
/// stride is what the payload lengths prove; the offset is what the values prove.
/// </para>
/// </remarks>
public sealed class RetailIgi2CollisionLayoutTests
{
    /// <summary>Every collision mesh the corpus ships parses under the sequel's strides.</summary>
    /// <remarks>
    /// A stride that were wrong by any amount would fail here, because the reader checks each chunk's
    /// length against the count the header declares — the same check that made every I.G.I. 2 model
    /// refuse to load under the first game's layout.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_collision_mesh_parses_under_the_sequels_strides()
    {
        int models = 0;
        int withMesh = 0;
        long faces = 0;
        long vertices = 0;

        foreach (MefModel model in Meshes())
        {
            models++;

            if (!MefCollisionMesh.TryRead(
                    model.Container,
                    out MefCollisionMesh? mesh,
                    AssetFormatProfile.Igi2.Collision)
                || mesh is null)
            {
                continue;
            }

            withMesh++;
            faces += mesh.Faces.Length;
            vertices += mesh.Vertices.Count;

            // Every face indexes vertices that exist. The face row is where the widening lands, so a
            // stride one word out would walk into the next record and index past the end.
            foreach (MefCollisionFace face in mesh.Faces)
            {
                Assert.InRange(face.I0, 0, mesh.Vertices.Count - 1);
                Assert.InRange(face.I1, 0, mesh.Vertices.Count - 1);
                Assert.InRange(face.I2, 0, mesh.Vertices.Count - 1);
                Assert.InRange(face.Material, 0, mesh.Materials - 1);
            }
        }

        Assert.True(models > 0, "no I.G.I. 2 model was read.");
        Assert.True(withMesh > 0, "not one I.G.I. 2 model carried collision geometry.");
        Assert.True(faces > 0 && vertices > 0);
    }

    /// <summary>The first game's strides refuse the sequel's models, which is what makes the seam real.</summary>
    /// <remarks>
    /// The negative half. Without it the test above would pass just as well against a reader that
    /// ignored the profile, since a permissive one accepts everything.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_first_games_strides_refuse_the_sequels_models()
    {
        int refused = 0;
        int read = 0;

        foreach (MefModel model in Meshes())
        {
            if (!MefCollisionMesh.TryRead(
                    model.Container,
                    out MefCollisionMesh? sequel,
                    AssetFormatProfile.Igi2.Collision)
                || sequel is null)
            {
                continue;
            }

            read++;

            try
            {
                MefCollisionMesh.TryRead(
                    model.Container, out MefCollisionMesh? _, AssetFormatProfile.Igi1.Collision);
            }
            catch (PackFormatException)
            {
                refused++;
            }
        }

        Assert.True(read > 0, "not one I.G.I. 2 model carried collision geometry.");
        Assert.True(
            refused > 0,
            $"all {read} of the sequel's collision meshes also parsed under the first game's strides.");
    }

    /// <summary>The global material row is read from twelve, and reading it from eight is not the same.</summary>
    /// <remarks>
    /// <para>
    /// The assertion that catches the silent failure. Offset 8 in the sequel's row is <c>iBumpMap</c>,
    /// so a reader stuck on the first game's offset still returns a plausible small number for every
    /// material of every model — this is what says the two are different numbers in shipped data.
    /// </para>
    /// <para>
    /// Read directly out of the material chunk rather than through a second profile, because the first
    /// game's profile would also change the stride, and then it would be two mistakes proving each
    /// other rather than one field being in the right place.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_global_material_row_is_read_from_twelve_not_eight()
    {
        CollisionLayout sequel = AssetFormatProfile.Igi2.Collision;
        CollisionLayout wrong = sequel with { MaterialGlobalOffset = AssetFormatProfile.Igi1.Collision.MaterialGlobalOffset };

        int compared = 0;
        int differed = 0;

        foreach (MefModel model in Meshes())
        {
            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? right, sequel) || right is null)
            {
                continue;
            }

            Assert.True(MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? bumped, wrong));

            for (int i = 0; i < right.Materials; i++)
            {
                compared++;
                if (right.GlobalMaterial(i) != bumped!.GlobalMaterial(i))
                {
                    differed++;
                }
            }
        }

        Assert.True(compared > 0, "no material rows were compared.");
        Assert.True(
            differed > 0,
            $"all {compared} material rows read the same from offset 8 and offset 12, so this proves nothing.");
    }

    /// <summary>The profile carries what the sequel's own symbols declare.</summary>
    /// <remarks>
    /// The sizes are <c>MultiMesh_Face_t</c> (12), <c>MultiMesh_Vertex_t</c> (20),
    /// <c>MultiMesh_Material_t</c> (16) and <c>MultiMesh_Sphere_t</c> (24) as <c>igi2.exe</c>'s symbols
    /// give them; the offsets are <c>iPortal</c> at 4 and <c>iMaterial</c> at 12. The sphere is the one
    /// row that did not change, which is worth pinning too: it is what says the tree's shape carried
    /// over unaltered.
    /// </remarks>
    [Fact]
    public void The_profile_carries_what_the_sequels_symbols_declare()
    {
        CollisionLayout sequel = AssetFormatProfile.Igi2.Collision;

        Assert.Equal(12, sequel.FaceStride);
        Assert.Equal(20, sequel.VertexStride);
        Assert.Equal(16, sequel.MaterialStride);
        Assert.Equal(24, sequel.SphereStride);
        Assert.Equal(4, sequel.MaterialPortalOffset);
        Assert.Equal(12, sequel.MaterialGlobalOffset);

        // And the first game's is what the reader uses when nothing says otherwise, so every caller
        // written before the profile existed reads exactly what it read before.
        CollisionLayout first = AssetFormatProfile.Igi1.Collision;

        Assert.Equal(MefCollisionMesh.FaceStride, first.FaceStride);
        Assert.Equal(MefCollisionMesh.VertexStride, first.VertexStride);
        Assert.Equal(MefCollisionMesh.MaterialStride, first.MaterialStride);
        Assert.Equal(MefCollisionMesh.SphereStride, first.SphereStride);
        Assert.Equal(MefCollisionMesh.MaterialPortalOffset, first.MaterialPortalOffset);
        Assert.Equal(MefCollisionMesh.MaterialGlobalOffset, first.MaterialGlobalOffset);

        // The sphere is the row the sequel left alone, and the portal index with it.
        Assert.Equal(first.SphereStride, sequel.SphereStride);
        Assert.Equal(first.MaterialPortalOffset, sequel.MaterialPortalOffset);
    }

    /// <summary>Every mesh the first level places, read once.</summary>
    /// <remarks>
    /// One level rather than the whole corpus: this reads and parses every model a level places, which
    /// over twenty-four levels is minutes rather than seconds, and a stride is either right for the
    /// format or wrong for it — a second level cannot make a wrong one right.
    /// </remarks>
    private static IEnumerable<MefModel> Meshes()
    {
        Igi2Level level = Igi2Level.All().First();
        HashSet<string> seen = [];

        foreach (Igi2Placement placement in level.Scene.StaticPlacements)
        {
            if (!level.Table.TryResolveMesh(placement.Model, out string? mesh)
                || !seen.Add(mesh)
                || !level.TryReadModel(mesh, out MefModel? model))
            {
                continue;
            }

            yield return model;
        }
    }
}
