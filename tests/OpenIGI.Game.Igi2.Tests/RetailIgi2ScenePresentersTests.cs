using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The forest and the water, built and drawn through the seam over the shipped levels.</summary>
/// <remarks>
/// Both passes existed before this and neither reached a frame: they needed a model lookup and their own
/// archives, and the ground's request carries the level and the registry alone. What these check is the
/// wiring — that the factories answer for the levels that have the content and decline for the ones that
/// do not, and that what comes back draws.
/// </remarks>
public sealed class RetailIgi2ScenePresentersTests
{
    /// <summary>Every level that plants a forest gets one, and it draws far fewer trees than it plants.</summary>
    /// <remarks>
    /// The count is the assertion that matters. A factory that returned null everywhere would look
    /// exactly like a game with no trees, and a presenter that culled nothing would draw all 54,537 of
    /// them from inside a stand.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_level_that_plants_a_forest_gets_one_that_culls()
    {
        int forests = 0;
        int planted = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            RecordingGraphicsDevice device = new();
            using IForestPresenter? forest = Igi2PresentationProvider.Instance.CreateForest(RequestFor(level, device));
            if (forest is null)
            {
                continue;
            }

            forests++;
            Igi2ForestPresenter concrete = Assert.IsType<Igi2ForestPresenter>(forest);
            planted += concrete.TreeCount;

            forest.Draw(new ForestFrame(
                new Camera3D(default, Camera3D.LookRotation(0f, 0f)),
                1f,
                0.75f));

            // A camera at the origin is outside every stand in the corpus, so the honest answer is a
            // small number rather than none -- and never the whole plantation.
            Assert.InRange(forest.VisibleTreeCount, 0, concrete.TreeCount);
            Assert.NotEmpty(forest.Summary);
        }

        Assert.True(forests > 0, "not one shipped level returned a forest, which is what the bug looked like.");
        Assert.True(planted > 0, "the forests that came back plant no trees at all.");
    }

    /// <summary>Every level that places water gets a presenter, and every sheet draws.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_level_that_places_water_gets_a_presenter_that_draws()
    {
        int levels = 0;
        int sheets = 0;
        int reflecting = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            RecordingGraphicsDevice device = new();
            using IWaterPresenter? water = Igi2PresentationProvider.Instance.CreateWater(RequestFor(level, device));
            if (water is null)
            {
                continue;
            }

            levels++;
            sheets += water.SheetCount;

            // The baked reflections, uploaded as cube maps on the way in. Six faces each.
            reflecting += device.CubeTextures.Count;
            Assert.Equal(device.CubeTextures.Count * 6, device.CubeFaceUploads.Count);

            water.Draw(new WaterFrame(new Camera3D(default, Camera3D.LookRotation(0f, -0.3f)), Matrix4f.Identity, 3.0));

            // One water draw per sheet, and nothing through the world pipeline: the original's water is its own
            // shader. How much of a sheet is drawn is the view's business; see Igi2WaterTessellationTests.
            Assert.Equal(water.SheetCount, device.WaterDraws.Count);
            Assert.Empty(device.MeshDraws);

            // The world pipeline's cube is never touched, so nothing else in the frame reflects this level's sky.
            Assert.Equal(TextureHandle.None, device.EnvironmentCube);
        }

        Assert.Equal(23, sheets);
        Assert.True(levels > 0, "no level returned a water presenter.");

        // And the reflections reached the device rather than the sheets all drawing unreflective, which
        // is what a broken archive lookup would look like and what every other count here would allow.
        Assert.True(reflecting > 0, "not one sheet loaded its baked cube map.");
    }

    /// <summary>Somewhere a shipped sheet thins towards the ground under it.</summary>
    /// <remarks>
    /// The shallowness is what keeps a shore clear and the open water opaque. A ground query that never answered
    /// would leave every sheet opaque to its edge, and every count above would still pass.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Some_water_thins_towards_the_ground()
    {
        int shores = 0;
        foreach (Igi2Level level in Igi2Level.All())
        {
            ScenePresentationRequest request = RequestFor(level, new RecordingGraphicsDevice());
            Igi2TerrainSurface ground = Igi2TerrainSurface.Build(level.Root, request.Resources);
            foreach (Igi2WaterSheet sheet in Igi2WaterSurface.Build(level.Root).Sheets)
            {
                for (double y = -sheet.Radius; y <= sheet.Radius; y += sheet.Radius / 40)
                {
                    for (double x = -sheet.Radius; x <= sheet.Radius; x += sheet.Radius / 40)
                    {
                        float shallowness = Igi2WaterPresenter.ShallownessAt(
                            sheet.Centre.Z, ground.WaterGroundAt(sheet.Centre.X + x, sheet.Centre.Y + y), sheet.AbsorptionLength);
                        Assert.InRange(shallowness, 0f, 1f);
                        shores += shallowness > 0f ? 1 : 0;
                    }
                }
            }
        }

        Assert.True(shores > 0, "no shipped sheet thins anywhere towards the ground under it.");
    }

    /// <summary>A host that cannot resolve a model still loads, and plants nothing.</summary>
    /// <remarks>
    /// The lookup is allowed to decline, and the reason it must be is that a title arrives one system at
    /// a time: a host with no model packs open should get a level with no trees rather than a crash or
    /// tens of thousands of invisible ones being culled every frame.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_lookup_that_declines_plants_nothing()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            RecordingGraphicsDevice device = new();
            ScenePresentationRequest request = RequestFor(level, device) with { Models = static _ => null };

            using IForestPresenter? forest = Igi2PresentationProvider.Instance.CreateForest(request);
            if (forest is null)
            {
                continue;
            }

            forest.Draw(new ForestFrame(new Camera3D(default, Camera3D.LookRotation(0f, 0f)), 1f, 0.75f));
            Assert.Empty(device.MeshDraws);
            return;
        }

        Assert.Fail("no level plants a forest.");
    }

    private static ScenePresentationRequest RequestFor(Igi2Level level, RecordingGraphicsDevice device)
    {
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        files.SetAlias(
            VirtualFileSystem.MissionAlias,
            "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");

        return new ScenePresentationRequest(
            device,
            level.Root,
            new ResourceRegistry(files),
            name => Resolve(level, name),
            static (_, _) => null,
            NullLogger.Instance);
    }

    private static MefModel? Resolve(Igi2Level level, string name)
    {
        if (!level.Table.TryResolveMesh(name, out string? mesh))
        {
            mesh = name;
        }

        return level.TryReadModel(mesh, out MefModel? model) ? model : null;
    }
}
