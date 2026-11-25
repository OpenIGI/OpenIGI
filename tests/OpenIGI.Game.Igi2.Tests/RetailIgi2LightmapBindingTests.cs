using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Binding a level's baked object lightmaps to the objects they light.
/// </summary>
/// <remarks>
/// The name carries a task id and nothing else identifying — no object name, no model name — so the
/// binding stands or falls on that id being the one <c>Task_New</c> was given. These check it against
/// every level rather than reasoning about it.
/// </remarks>
public sealed class RetailIgi2LightmapBindingTests
{
    /// <summary>Every lightmap names a task the level really declares.</summary>
    /// <remarks>
    /// <para>
    /// The load-bearing claim. <c>Level_GetLevelQTaskIDFromQTask</c> (<c>igi2ed 0x43CF90</c>) returns a
    /// slot in <c>Level_atLevelQTaskID</c>, and <c>Level_QTaskNew</c> (<c>0x4410C0</c>) fills that slot
    /// from <b>argument 0 of <c>Task_New</c></b> — before the type name, before the label. So the number
    /// in a lightmap's name is an id the level script wrote, and if that reading were wrong the ids would
    /// mostly miss.
    /// </para>
    /// <para>
    /// They do not miss at all: every one of the corpus's lightmapped objects is a task the level
    /// declares.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_lightmap_names_a_task_the_level_declares()
    {
        int levels = 0;
        int maps = 0;
        int objects = 0;
        int orphans = 0;

        foreach (Igi2Level level in Levels())
        {
            levels++;
            maps += level.Lightmaps.Count;

            foreach (int taskId in level.Lightmaps.TaskIds)
            {
                objects++;
                if (!level.TaskIds.Contains(taskId))
                {
                    orphans++;
                }
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(30_994, maps);
        Assert.Equal(7_986, objects);

        // Exactly one, and it is shipped-data residue rather than a misreading: id 4011 of
        // location1/level2 carries a single map, and 4010 and 4012 either side of it are both declared
        // tasks. An object was deleted after the level was baked. 7,985 of 7,986 is the measurement that
        // says argument 0 of Task_New is the right number; demanding zero would be asserting the retail
        // data is tidy, which this corpus has repeatedly shown it is not.
        Assert.Equal(1, orphans);
    }

    /// <summary>Only a lightmapped mesh with triangles gets a baked lightmap.</summary>
    /// <remarks>
    /// <para>
    /// <c>LightmapInfo_AddLightmapVirModelToEnumeration</c> creates an entry only when the model's mesh
    /// format is 3 — <see cref="MefMeshFormat.Lightmapped"/> — and it has faces. That gate is the reason
    /// a placement without lightmaps is ordinary rather than a missing binding, so it is worth holding
    /// the port to it: <b>every placement this binds a lightmap to has a lightmapped mesh.</b>
    /// </para>
    /// <para>
    /// <b>Asked of the model slot the placement wears, not of its task.</b> A task may own several models
    /// and draw only one — a <c>Switch</c> that starts off wears its off model — so a task can hold bakes
    /// while the model on show has none. Matching <see cref="Igi2Placement.ModelVariant"/> is the same
    /// question <c>Igi2DrawableScene</c> asks when it picks a bake, and asking the looser one instead
    /// found 33 placements in the corpus wearing a diffuse mesh under another model's light.
    /// </para>
    /// <para>
    /// The converse is deliberately not asserted. A lightmapped mesh whose object was excluded from the
    /// bake — the <c>Exclude from lightmaps</c> parameter I.G.I. 2's editor emits on <c>Container</c> —
    /// legitimately has none, so requiring one in both directions would be asserting a stronger rule than
    /// the original follows.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Only_a_lightmapped_mesh_is_given_a_lightmap()
    {
        int bound = 0;
        int diffuse = 0;

        foreach (Igi2Level level in Levels())
        {
            foreach (Igi2Placement placement in level.Scene.StaticPlacements)
            {
                if (!level.Lightmaps.TryGetBindings(placement, out IReadOnlyList<Igi2LightmapBinding>? bindings)
                    || !bindings.Any(binding => placement.ModelVariant == 0 || binding.Variant == placement.ModelVariant)
                    || !level.Table.TryResolveMesh(placement.Model, out string? mesh)
                    || !level.TryReadModel(mesh, out MefModel? model))
                {
                    continue;
                }

                bound++;
                if (model.Format != MefMeshFormat.Lightmapped)
                {
                    diffuse++;
                }
            }
        }

        Assert.True(bound > 3_000, $"Only {bound} placements resolved to a mesh and a lightmap.");
        Assert.Equal(0, diffuse);
    }

    /// <summary>An object's levels of detail are numbered from one with no gaps.</summary>
    /// <remarks>
    /// The writer's loop runs <c>iLOD</c> from zero to the model's LOD count and emits <c>iLOD + 1</c>,
    /// so a group's maps are <c>1..n</c> exactly. A gap would mean the level ships a partial bake, or
    /// that the third number is not what it is read as.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Levels_of_detail_run_from_one_without_gaps()
    {
        int groups = 0;

        foreach (Igi2Level level in Levels())
        {
            foreach (int taskId in level.Lightmaps.TaskIds)
            {
                Assert.True(level.Lightmaps.TryGetBindings(Place(taskId), out IReadOnlyList<Igi2LightmapBinding>? bindings));

                foreach (IGrouping<(int Variant, int Model), Igi2LightmapBinding> group in bindings
                    .GroupBy(static b => (b.Variant, b.ModelIndex)))
                {
                    groups++;
                    int[] lods = [.. group.Select(static b => b.Lod).Order()];
                    Assert.Equal(Enumerable.Range(1, lods.Length), lods);
                }
            }
        }

        Assert.Equal(12_597, groups);
    }

    /// <summary>The name builder reproduces every shipped entry name exactly.</summary>
    /// <remarks>
    /// Parsing and rebuilding the whole corpus is what makes <see cref="Igi2LightmapIndex.ResourceName"/>
    /// usable for a lookup rather than only for a diagnostic. A single mis-padded field — <c>%04d</c>
    /// against <c>%d</c>, or the lettered form taken when the group is zero — would show up here and
    /// nowhere else until a renderer silently found no lightmap.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_name_round_trips_through_the_builder()
    {
        int checked_ = 0;
        SortedSet<string> prefixes = new(StringComparer.Ordinal);

        foreach (Igi2Level level in Levels())
        {
            foreach (int taskId in level.Lightmaps.TaskIds)
            {
                Assert.True(level.Lightmaps.TryGetBindings(Place(taskId), out IReadOnlyList<Igi2LightmapBinding>? bindings));

                foreach (Igi2LightmapBinding binding in bindings)
                {
                    checked_++;
                    string bare = binding.ResourceName[(binding.ResourceName.LastIndexOf('/') + 1)..];
                    Assert.Equal(
                        bare,
                        Igi2LightmapIndex.EntryName(taskId, binding.Variant, binding.ModelIndex, binding.Lod));

                    // 23 levels register their entries under LOCAL:lightmaps/, and location3/level1
                    // registers full paths. Both are the same bare name, which is what the original keys
                    // its hash table on -- and why a renderer looks a binding up instead of building one.
                    prefixes.Add(binding.ResourceName[..binding.ResourceName.LastIndexOf('/')]);
                }
            }
        }

        Assert.Equal(30_994, checked_);
        Assert.Equal(
            ["LOCAL:lightmaps", "LOCAL:missions/location3/level1/lightmaps"],
            prefixes.Order(StringComparer.Ordinal));
    }

    /// <summary>A lightmap bound to a placement decodes under the sequel's profile.</summary>
    /// <remarks>
    /// The binding is only worth having if the bytes at the end of it parse, so this closes the loop from
    /// a placement to pixels. Under the first game's profile the same entries are refused, which is
    /// <see cref="AssetFormatProfile.ObjectLightmapDescriptorStride"/> doing its job.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_bound_lightmap_decodes_to_pixels()
    {
        int decoded = 0;

        foreach (Igi2Level level in Levels())
        {
            foreach (Igi2Placement placement in level.Scene.StaticPlacements.Take(40))
            {
                if (!level.Lightmaps.TryGetBindings(placement, out IReadOnlyList<Igi2LightmapBinding>? bindings))
                {
                    continue;
                }

                foreach (Igi2LightmapBinding binding in bindings)
                {
                    Assert.True(level.LightmapArchive.TryGetEntry(binding.ResourceName, out IresEntry? entry));

                    ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data, AssetFormatProfile.Igi2);
                    Assert.NotEmpty(lightmap.Images);
                    Assert.True(lightmap.Images[0].Width > 0 && lightmap.Images[0].Height > 0);
                    decoded++;
                }
            }
        }

        Assert.True(decoded > 500, $"Only {decoded} lightmaps were decoded.");
    }

    /// <summary>A drawn part is bound the bake made for its own mesh, and the original's loader would take it.</summary>
    /// <remarks>
    /// <para>
    /// The model index in a lightmap's name is a running count over the object's tree, and the count runs in
    /// <c>RigidDynCubeObj_EnumerateLightmapVirModelsHandler</c>'s order: the node's own model, its magic container,
    /// its sector container, its task children. So a building counts its rooms after all its other parts --
    /// <c>SectorObject_CreateAttachObjects</c> moves each sector into the sector container -- and a security
    /// camera counts its lens last. Counting the attach points in file order instead bound 400_51_1, the
    /// dormitory room of location 1 level 1, to a bake of another model's size, and the stretched page drew the
    /// room's floor and ceiling past white. Hundreds of parts in every level were bound that way.
    /// </para>
    /// <para>
    /// <c>LightmapInfo_CreateLightmapList</c> takes a file only when it records the mesh's version, is newer than
    /// the mesh and holds as many pages; <see cref="ObjectLightmap.IsBakeOf"/> is that rule, and what it turns
    /// down is drawn white, as the original draws it. All five it turns down are the data's, not the count's: four
    /// parts of building 4038 in location 2 level 3 whose meshes were exported again the day after the bake with
    /// no lightmap pages at all, and 3786_0000_1 in location 3 level 1, whose mesh postdates its bake by three months.
    /// </para>
    /// <para>
    /// The loader never compares the pages' sizes, so the 32 files it takes at a size other than the mesh's --
    /// 200_01_1 at model index zero in location 3 level 3 among them, which no ordering can reach -- are drawn at
    /// their own, as the port draws them.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_drawn_part_is_bound_the_bake_of_its_own_mesh()
    {
        Igi2MagicObjTypes magic = Igi2Session.Start().Session.Services.Require<Igi2MagicObjTypes>();
        int bound = 0;
        int refused = 0;
        int resized = 0;

        foreach (Igi2Level level in Levels())
        {
            Dictionary<string, IresEntry> entries = new(StringComparer.OrdinalIgnoreCase);
            foreach (IresEntry entry in level.LightmapArchive.Entries)
            {
                entries[Path.GetFileName(entry.Name)] = entry;
            }

            foreach (Game.World.WorldPlacement placement in Igi2LevelScene.Build(level, magic).Built.Scene.Placements)
            {
                if (placement.LightmapEntry.Length == 0 || !entries.TryGetValue(placement.LightmapEntry, out IresEntry? entry))
                {
                    continue;
                }

                bound++;
                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data, AssetFormatProfile.Igi2);
                if (!lightmap.IsBakeOf(placement.Model))
                {
                    refused++;
                    continue;
                }

                // Taken, but not at the mesh's sizes.
                IReadOnlyList<MefLightmapPage> pages = placement.Model.LightmapPages;
                for (int i = 0; i < pages.Count; i++)
                {
                    if (lightmap.Images[i].Width != pages[i].Width || lightmap.Images[i].Height != pages[i].Height)
                    {
                        resized++;
                        break;
                    }
                }
            }
        }

        // With mission 24's fuel pump switch, placed on its destroyed model and bound that model's bake (0352e).
        Assert.Equal(12_241, bound);
        Assert.Equal(5, refused);
        Assert.Equal(32, resized);
    }

    private static Igi2Placement Place(int taskId) => new(taskId, "probe", "probe", default, default, 0);

    private static IEnumerable<Igi2Level> Levels() => Igi2Level.All().Where(static l => l.HasLightmaps);
}
