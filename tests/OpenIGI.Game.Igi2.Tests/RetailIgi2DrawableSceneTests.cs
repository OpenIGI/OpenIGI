using OpenIGI.Engine.Resources;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Every shipped I.G.I. 2 level, turned into the scene the shared world renderer draws.
/// </summary>
/// <remarks>
/// <para>
/// The end of the M2-3 data chain. Four milestones established the pieces separately — the placements,
/// the meshes, the materials, the lightmaps — and this is the one test that puts all of them through the
/// type a renderer actually consumes.
/// </para>
/// <para>
/// It also proves the claim the move rests on: <c>WorldRenderer</c> is title-agnostic. If it were not,
/// the sequel's scene could not be expressed in the placement record it draws.
/// </para>
/// </remarks>
public sealed class RetailIgi2DrawableSceneTests
{
    /// <summary>Every level builds a drawable scene, and its geometry is real.</summary>
    /// <remarks>
    /// Counted exactly. A regression anywhere upstream — the pack order, the mesh resolve, the lightmap
    /// binding — moves one of these numbers, and which one moves says which stage broke.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_level_builds_a_scene_the_shared_renderer_can_draw()
    {
        int levels = 0;
        int placements = 0;
        int lightmapped = 0;
        int unresolved = 0;
        long triangles = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            levels++;
            Igi2DrawableSceneResult built = Build(level);
            placements += built.Scene.Placements.Count;
            lightmapped += built.LightmappedCount;
            unresolved += built.UnresolvedCount;

            foreach (WorldPlacement placement in built.Scene.Placements)
            {
                Assert.NotNull(placement.Model);
                Assert.NotNull(placement.Pack);
                Assert.False(string.IsNullOrEmpty(placement.Instance.Name));
                triangles += placement.Model.TriangleCount;
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(16_612, placements);
        Assert.Equal(12_239, lightmapped);
        Assert.Equal(2_650_200L, triangles);

        // The two are `soldier_1`, which is in no archive anywhere in the installation -- the residue
        // RetailIgi2ModelTableTests names. A scene that silently dropped more than that would still look
        // fine on screen, which is why the number is pinned rather than tolerated.
        Assert.Equal(2, unresolved);
    }

    /// <summary>A lightmapped placement names an entry the level's archive really holds.</summary>
    /// <remarks>
    /// <para>
    /// The renderer looks a bake up by bare entry name, which is why <see cref="WorldPlacement"/> now
    /// carries one rather than only the pieces to format one. Project I.G.I. computes
    /// <c>&lt;set&gt;_&lt;%05d&gt;.olm</c>; the sequel reads the name out of the archive, because one of
    /// its 24 levels registers full paths where the rest register bare names.
    /// </para>
    /// <para>
    /// So this checks the thing the renderer will do: take the name off the placement, look it up the
    /// way the renderer keys its dictionary, and find the bytes.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_lightmapped_placement_names_an_entry_the_archive_holds()
    {
        int checkedEntries = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            if (!level.HasLightmaps)
            {
                continue;
            }

            Dictionary<string, ReadOnlyMemory<byte>> byBareName = new(StringComparer.OrdinalIgnoreCase);
            foreach (Formats.Ilff.IresEntry entry in level.LightmapArchive.Entries)
            {
                int slash = entry.Name.LastIndexOfAny(['/', '\\']);
                byBareName[slash >= 0 ? entry.Name[(slash + 1)..] : entry.Name] = entry.Data;
            }

            foreach (WorldPlacement placement in Build(level).Scene.Placements)
            {
                if (!placement.HasLightmap)
                {
                    continue;
                }

                string name = placement.LightmapEntryName();
                Assert.True(byBareName.TryGetValue(name, out ReadOnlyMemory<byte> bytes), $"'{name}' is not in the archive.");

                ObjectLightmap map = ObjectLightmap.Read(bytes, AssetFormatProfile.Igi2);
                Assert.NotEmpty(map.Images);
                checkedEntries++;
            }
        }

        Assert.Equal(12_239, checkedEntries);
    }

    /// <summary>The world scene carries no characters.</summary>
    /// <remarks>
    /// A character is drawn by a character presenter from a skinned mesh, not by the world pass. The
    /// filter is <see cref="Igi2WorldScene.CharacterTypes"/>, and this is what says it is actually
    /// applied: the difference between everything placed and everything drawable is exactly the
    /// characters plus whatever the packs do not resolve.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_world_scene_leaves_the_characters_to_a_character_presenter()
    {
        int placed = 0;
        int characters = 0;
        int drawable = 0;
        int attached = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2DrawableSceneResult built = Build(level);
            placed += built.Placed.Placements.Count;
            characters += built.Placed.Placements.Count(static p => Igi2WorldScene.CharacterTypes.Contains(p.TypeName));
            drawable += built.Scene.Placements.Count;
            attached += built.AttachedCount;

            foreach (WorldPlacement placement in built.Scene.Placements)
            {
                Assert.DoesNotContain(placement.TaskName, Igi2WorldScene.CharacterTypes);
            }
        }

        Assert.Equal(11_699, placed);
        // 1,210 rather than 1,229: the nineteen the levels declare as HumanPlayer are no longer
        // placements at all, having become the player the mission stands up.
        Assert.Equal(1_210, characters);
        // Everything placed, less the characters, less the two the shipped data cannot resolve, plus
        // every part attached to one of them -- each of which is a placement in its own right.
        Assert.Equal(placed - characters - 2 + attached, drawable);
        Assert.Equal(6_125, attached);
    }

    /// <summary>A drawable placement is in metres, and the walk it came from is in engine units.</summary>
    /// <remarks>
    /// <para>
    /// <c>Game_Open</c> registers <c>WORLD_METER</c> as a double — <c>IGI.exe 0x539558</c>,
    /// <c>igi2.exe 0x640B80</c> — and both games hold <b>4096</b>. So an authored position is in
    /// sixteenths of a millimetre and a <see cref="WorldPlacement"/> is in metres, and the division
    /// between them is a step the walk owes the renderer.
    /// </para>
    /// <para>
    /// Worth its own test because the symptom lies. A walk that skips the conversion does not fail or
    /// draw the world wrongly; it pushes every object four thousand times further out than the far
    /// plane, and the frame comes back as an empty sky — which reads as "nothing was placed" rather
    /// than as a scale error, and sends you looking in the wrong file.
    /// </para>
    /// <para>
    /// The measurement is a ratio rather than a bound, so it says nothing about how big a level is:
    /// however far out the sequel's first mission puts its aeroplane, the drawable placement is that
    /// position divided by 4096 and not some other number.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_drawable_placement_is_the_authored_position_in_metres()
    {
        Igi2DrawableSceneResult built = Build(Igi2Level.All().First());

        // Asked of the AUTHORED placements rather than of the drawable ones, because the drawable list
        // holds more than the level authors: an object's attached parts are placements of their own, at
        // their parent's position plus a model-space offset, so a drawable position is not in general an
        // authored one. What still holds -- and is the claim about the conversion -- is that every
        // authored placement the walk keeps turns up among them, at its own position over 4096.
        HashSet<(double X, double Y, double Z)> drawableMetres = [.. built.Scene.Placements.Select(static p =>
            (p.Position.X, p.Position.Y, p.Position.Z))];

        Assert.NotEmpty(built.Scene.Placements);
        int authored = built.Placed.StaticPlacements.Count();
        int found = built.Placed.StaticPlacements.Count(p => drawableMetres.Contains(
            (p.Position.X / WorldScene.UnitsPerMetre,
             p.Position.Y / WorldScene.UnitsPerMetre,
             p.Position.Z / WorldScene.UnitsPerMetre)));

        Assert.Equal(authored - built.UnresolvedCount, found);

        // And there are more drawn than authored, which is the attachments.
        Assert.True(
            built.Scene.Placements.Count > authored,
            $"{built.Scene.Placements.Count} drawable is no more than {authored} authored, so nothing attached.");

        // The negative half: the authored units are NOT the drawable ones, so the assertion above is
        // about a conversion rather than about two collections that happen to agree.
        HashSet<(double X, double Y, double Z)> inUnits = [.. built.Placed.Placements.Select(static p =>
            (p.Position.X, p.Position.Y, p.Position.Z))];

        Assert.DoesNotContain(
            (built.Scene.Placements[0].Position.X,
             built.Scene.Placements[0].Position.Y,
             built.Scene.Placements[0].Position.Z),
            inUnits);

        // And the level comes out the size of a level. The sequel's first mission opens in an aeroplane
        // some four and a half kilometres up, so the assertion is a hundred kilometres rather than a
        // tight box -- but an unconverted walk lands at sixty-two million, which this catches by four
        // orders of magnitude.
        double furthest = built.Scene.Placements.Max(static p =>
            Math.Max(Math.Abs(p.Position.X), Math.Max(Math.Abs(p.Position.Y), Math.Abs(p.Position.Z))));

        Assert.InRange(furthest, 1_000.0, 100_000.0);
    }

    /// <summary>The shared renderer reads the sequel's baked lightmaps, and refuses them at the first game's stride.</summary>
    /// <remarks>
    /// <para>
    /// The wiring, not the format. <see cref="ObjectLightmap"/> could already read an I.G.I. 2 entry
    /// when told which game wrote it, and the shared <see cref="WorldRenderer"/> did not tell it — so
    /// every level drew unlit with a readable archive sitting beside it, one refused entry per object.
    /// The reader's own tests could not catch that, because from inside the reader nothing was wrong.
    /// </para>
    /// <para>
    /// So the assertion is made through the renderer, from a camera that is actually looking at
    /// something: pages uploaded and sub-meshes reported as lightmapped. The second half draws the same
    /// scene at the first game's stride and asserts NOTHING lands, which is what says the first half is
    /// about the profile rather than about the archive merely being present.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_shared_renderer_reads_the_sequels_baked_lightmaps()
    {
        (Igi2DrawableSceneResult built, ResourceRegistry resources) = BuildWithResources(Igi2Level.All().First());

        WorldPlacement lit = built.Scene.Placements.First(static p => p.HasLightmap);
        Assert.NotEmpty(lit.LightmapEntryName());

        Assert.True(Lightmapped(built.Scene, resources, lit, AssetFormatProfile.Igi2) > 0,
            "the renderer drew the level with no lightmapped sub-mesh, under the sequel's own profile.");
        Assert.Equal(0, Lightmapped(built.Scene, resources, lit, AssetFormatProfile.Igi1));
    }

    /// <summary>Draws one lightmapped placement head-on and reports how many sub-meshes sampled a bake.</summary>
    private static int Lightmapped(
        WorldScene scene,
        ResourceRegistry resources,
        WorldPlacement subject,
        AssetFormatProfile formats)
    {
        RecordingGraphicsDevice device = new();
        using WorldRenderer renderer = new(device, resources, formats);

        // Twenty metres back along -Y and a little up, which is a look at the object rather than a
        // survey of the level: the count is of what was drawn, so the camera has to be able to see it.
        Camera3D camera = new(
            new Vector3d(subject.Position.X, subject.Position.Y - 20.0, subject.Position.Z + 2.0),
            Camera3D.LookRotation(0f, 0f));

        renderer.BeginFrame(1f, smooth: false);
        renderer.Draw(scene, camera, Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.05f, 400f));

        Assert.True(renderer.LastDrawCount > 0, "the camera saw nothing at all, so the count says nothing.");
        return renderer.LastLightmappedCount;
    }

    private static Igi2DrawableSceneResult Build(Igi2Level level) => Igi2LevelScene.Build(level).Built;

    /// <summary>The same walk, keeping the registry the lightmap archive is read back through.</summary>
    private static (Igi2DrawableSceneResult Built, ResourceRegistry Resources) BuildWithResources(Igi2Level level) =>
        Igi2LevelScene.Build(level);
}
