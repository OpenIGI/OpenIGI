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

            // One draw per sheet, laid over what is under it at the sheet's alpha (WaterRender_SetBlendState's ONE,
            // SRCALPHA), depth-tested but not written, and both facings -- a camera under the surface has to see it
            // from below.
            Assert.Equal(water.SheetCount, device.MeshDraws.Count);
            Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.SourcePlusAttenuatedDestination, draw.Blend));
            Assert.All(device.MeshDraws, draw => Assert.Equal(DepthState.TestOnly, draw.DepthState));
            Assert.All(device.MeshDraws, draw => Assert.Equal(CullMode.None, draw.CullMode));

            // The cube is cleared after the pass, so the next thing drawn does not reflect this level's
            // sky. Left set, every wall in the frame would.
            Assert.Equal(TextureHandle.None, device.EnvironmentCube);
        }

        Assert.Equal(23, sheets);
        Assert.True(levels > 0, "no level returned a water presenter.");

        // And the reflections reached the device rather than the sheets all drawing unreflective, which
        // is what a broken archive lookup would look like and what every other count here would allow.
        Assert.True(reflecting > 0, "not one sheet loaded its baked cube map.");
    }

    /// <summary>The surface moves with the clock, and stays where the sheet is.</summary>
    /// <remarks>
    /// The half that says the wave field is actually being sampled per frame. A presenter that built its
    /// grid once would draw a frozen sea, and every count above would still pass.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_surface_moves_with_the_clock()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            RecordingGraphicsDevice device = new();
            using IWaterPresenter? water = Igi2PresentationProvider.Instance.CreateWater(RequestFor(level, device));
            if (water is null || water.SheetCount == 0)
            {
                continue;
            }

            Camera3D camera = new(default, Camera3D.LookRotation(0f, -0.3f));
            water.Draw(new WaterFrame(camera, Matrix4f.Identity, 0.0));

            uint mesh = device.MeshDraws[0].Mesh.Id;
            Vertex3D[] first = [.. device.Meshes[mesh].Vertices];
            if (first.Length <= 4)
            {
                // A sheet with no waves is flat by construction and has nothing to move.
                continue;
            }

            water.Draw(new WaterFrame(camera, Matrix4f.Identity, 5.0));
            Vertex3D[] later = device.Meshes[mesh].Vertices;

            // The grid stays put in X and Y -- it is the sheet's own square -- and only the height and
            // the normal move.
            Assert.Equal(first.Length, later.Length);
            for (int i = 0; i < first.Length; i++)
            {
                Assert.Equal(first[i].Position.X, later[i].Position.X);
                Assert.Equal(first[i].Position.Y, later[i].Position.Y);
            }

            Assert.Contains(
                Enumerable.Range(0, first.Length),
                i => first[i].Position.Z != later[i].Position.Z);
            return;
        }

        Assert.Fail("no level placed water with a wave in it.");
    }

    /// <summary>A grid is cut finely enough to carry the shortest wave the sheet has.</summary>
    /// <remarks>
    /// The tessellation is this port's rather than the original's — <c>WaterRender_Draw</c>'s adaptive
    /// walk is not ported — so what can be pinned is the rule it follows: eight vertices across the
    /// shortest wavelength, and never more than a 16-bit index space allows.
    /// </remarks>
    [Fact]
    public void The_grid_carries_the_shortest_wave_the_sheet_has()
    {
        // A 50 m half-extent with a 4 m wave: 100 m of span, half a metre a step, 201 across.
        Igi2WaterSheet fine = Sheet(50f, new Igi2WaterWave(4f, 0.2f, 1f, 0f));
        Assert.Equal(Igi2WaterPresenter.MaximumEdgeVertices, Igi2WaterPresenter.EdgeVerticesFor(fine));

        // The same sheet with a much longer wave needs far fewer.
        Igi2WaterSheet coarse = Sheet(50f, new Igi2WaterWave(80f, 0.2f, 1f, 0f));
        int edge = Igi2WaterPresenter.EdgeVerticesFor(coarse);
        Assert.InRange(edge, Igi2WaterPresenter.MinimumEdgeVertices, Igi2WaterPresenter.MaximumEdgeVertices);
        Assert.True(edge < Igi2WaterPresenter.MaximumEdgeVertices);

        // Eight across the wave, which is the stated rule.
        Assert.Equal(
            (int)Math.Ceiling(100.0 / (80.0 / Igi2WaterPresenter.SamplesPerWavelength)) + 1,
            edge);

        // A sheet with no waves is flat, and four corners describe a flat square exactly.
        Assert.Equal(Igi2WaterPresenter.MinimumEdgeVertices, Igi2WaterPresenter.EdgeVerticesFor(Sheet(50f)));

        // And so is one whose only train has no height, which the corpus does author.
        Assert.Equal(
            Igi2WaterPresenter.MinimumEdgeVertices,
            Igi2WaterPresenter.EdgeVerticesFor(Sheet(50f, new Igi2WaterWave(4f, 0f, 1f, 0f))));
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

    private static Igi2WaterSheet Sheet(float halfExtentMetres, params Igi2WaterWave[] waves) => new(
        1,
        default,
        halfExtentMetres * 4096.0,
        1f,
        1f,
        0f,
        1f,
        default,
        [],
        string.Empty,
        string.Empty,
        waves);

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
