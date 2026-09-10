using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>What I.G.I. 2 can present, and what it says about the rest.</summary>
/// <remarks>
/// These need no game data: the point is the shape of the seam, not the content behind it.
/// </remarks>
public sealed class Igi2PresentationProviderTests
{
    /// <summary>The title presents its world and admits it presents nothing else.</summary>
    /// <remarks>
    /// <para>
    /// A census, and it is meant to shrink. Each null is a milestone that has not happened — the arms
    /// and the held weapon at M2-4 and M2-5, the HUD and the computers at M2-7 — so a passing
    /// assertion here is a statement about how far the port has got, and the test fails the moment one
    /// arrives without this being updated.
    /// </para>
    /// <para>
    /// The ground is null on purpose rather than stubbed. Its request carries a
    /// <c>TerrainSurface</c> built from Project I.G.I.'s chunked octree, and the sequel's flat grid of
    /// <c>.thm</c> tiles does not produce one; a presenter that drew nothing would be a worse answer
    /// than none.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_provider_supplies_a_world_and_nothing_else_yet()
    {
        // Through the interface, because the thirteen it does not implement are default interface
        // members and are reachable only that way -- which is itself the point: the concrete type's
        // surface is the one member it actually has.
        ITitlePresentationProvider provider = Igi2PresentationProvider.Instance;
        ResourceRegistry resources = new(new VirtualFileSystem());
        NullLogger logger = NullLogger.Instance;

        Assert.Null(provider.CreateViewModel(default));
        Assert.Null(provider.CreateHeldWeapon(default));
        Assert.Null(provider.CreateGrenades(default));
        Assert.Null(provider.CreateMissiles(default));
        Assert.Null(provider.CreateGunPickups(default));
        Assert.Null(provider.CreateBulletMarks(default));
        Assert.Null(provider.CreateCameraStatusLights(default));
        Assert.Null(provider.CreateHud(default));
        Assert.Null(provider.CreateRemasteredHud(default));
        Assert.Null(provider.CreateMapComputer(default));
        Assert.Null(provider.CreateTacticalComputer(default));
        Assert.Null(provider.CreateComputerHighlights(default));
        Assert.Null(provider.CreateTerrain(default));

        // The one it does supply is real, and it is the renderer Project I.G.I. draws through.
        using IWorldPresenter? world = provider.CreateWorld(new WorldPresentationRequest(
            new RecordingGraphicsDevice(),
            resources,
            logger));

        Assert.NotNull(world);
        Assert.Equal(0, world.LastDrawCount);
    }

    /// <summary>The provider reads and draws a real level's ground.</summary>
    /// <remarks>
    /// <para>
    /// End to end through the seam the host uses: a resource registry with the level's own archives
    /// mounted, in, and a presenter that issues additive draws, out. <c>CreateTerrain</c> stays null —
    /// the sequel's ground is not a <c>TerrainSurface</c> — and <c>CreateGround</c> is what answers.
    /// </para>
    /// <para>
    /// <b>The ground is textured.</b> A terrain material names a full path with its own extension —
    /// <c>MISSION:textures/k_grass.jpg</c> — resolving to a loose JPEG beside the level's archive, and
    /// the loader reads those now. So the draws come out with a real diffuse handle rather than
    /// <see cref="TextureHandle.None"/>, which is the difference between a lit shape and a surface.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_provider_reads_and_draws_a_levels_ground()
    {
        ITitlePresentationProvider provider = Igi2PresentationProvider.Instance;
        string root = GameDataSource.Igi2.RequireRoot();

        foreach (Igi2Level level in Igi2Level.All())
        {
            string archive = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
            string textures = Path.Combine(level.Directory, "textures");
            if (!File.Exists(archive) || !Directory.Exists(textures))
            {
                continue;
            }

            // The real tree behind the registry, because a name the level does not ship falls through
            // to the file system -- which in the host always has a device and here must too.
            VirtualFileSystem files = new();
            files.RegisterDevice(new LocalFileDevice(root));
            files.SetAlias(
                VirtualFileSystem.MissionAlias,
                "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");
            ResourceRegistry resources = new(files);
            resources.Register(Igi2TerrainSurface.ArchiveResourceName, File.ReadAllBytes(archive));
            foreach (string res in Directory.EnumerateFiles(textures, "*.res"))
            {
                foreach (IresEntry entry in IresArchive.Read(File.ReadAllBytes(res)).Entries)
                {
                    resources.Register(entry.Name, entry.Data, replaceExisting: true);
                }
            }

            RecordingGraphicsDevice device = new();
            using ITerrainPresenter? ground = provider.CreateGround(
                new GroundPresentationRequest(device, level.Root, resources, NullLogger.Instance));

            if (ground is null)
            {
                // A level that declares no terrain gets no presenter, which is the honest answer.
                continue;
            }

            ground.Draw(new TerrainFrame(default, Matrix4f.Identity, 1f, 1.6f, 0, false, false));

            Assert.NotEmpty(device.MeshDraws);
            // An opaque base per triangle and the other materials' shares added over it -- see
            // Igi2TerrainPass.BaseIndices.
            Assert.All(
                device.MeshDraws,
                draw => Assert.True(
                    draw.Blend is BlendMode.Opaque or BlendMode.Additive,
                    $"The ground drew with {draw.Blend}."));

            // Chunked at the original's 10,000-index budget, not one draw per pass.
            Assert.All(device.MeshDraws, draw => Assert.InRange(draw.IndexCount, 3, 9999));

            // And textured: the terrain materials resolve to handles the device really uploaded.
            Assert.All(device.MeshDraws, draw => Assert.NotEqual(TextureHandle.None, draw.Diffuse));
            return;
        }

        Assert.Fail("No level shipped both terrain and textures.");
    }

    /// <summary>The ground's textures tile, and carry the mip chain their file ships.</summary>
    /// <remarks>
    /// <para>
    /// A terrain material is laid across the ground rather than fitted to it, so its U and V run into
    /// the hundreds. <c>TerrainRender_SetTextureState</c> (<c>igi2ed 0x66B810</c>) sets
    /// <c>D3DTSS_ADDRESSU</c> and <c>ADDRESSV</c> to <c>D3DTADDRESS_WRAP</c> on stage 0 and stage 1 —
    /// the material and the detail map — and clamps only stage 2.
    /// </para>
    /// <para>
    /// The loader took <see cref="TextureDescription"/>'s two-argument constructor, which is the one 2D
    /// drawing uses: a single mip level, clamped. Clamping a coordinate of 300 samples the same edge
    /// texel three hundred times, so every material was drawn as a streak of its own border pulled down
    /// the hillside — and because it is a plausible-looking mess rather than a failure, nothing said so.
    /// </para>
    /// <para>
    /// Both halves are asserted. The mip count is the quieter one: ground seen at a kilometre is
    /// minified hard, and a single level aliases into a shimmer rather than resolving.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_grounds_textures_tile_rather_than_clamp()
    {
        ITitlePresentationProvider provider = Igi2PresentationProvider.Instance;
        string root = GameDataSource.Igi2.RequireRoot();

        foreach (Igi2Level level in Igi2Level.All())
        {
            string archive = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
            string textures = Path.Combine(level.Directory, "textures");
            if (!File.Exists(archive) || !Directory.Exists(textures))
            {
                continue;
            }

            VirtualFileSystem files = new();
            files.RegisterDevice(new LocalFileDevice(root));
            files.SetAlias(
                VirtualFileSystem.MissionAlias,
                "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");
            ResourceRegistry resources = new(files);
            resources.Register(Igi2TerrainSurface.ArchiveResourceName, File.ReadAllBytes(archive));
            foreach (string res in Directory.EnumerateFiles(textures, "*.res"))
            {
                foreach (IresEntry entry in IresArchive.Read(File.ReadAllBytes(res)).Entries)
                {
                    resources.Register(entry.Name, entry.Data, replaceExisting: true);
                }
            }

            RecordingGraphicsDevice device = new();
            using ITerrainPresenter? ground = provider.CreateGround(
                new GroundPresentationRequest(device, level.Root, resources, NullLogger.Instance));

            if (ground is null || device.Textures.Count == 0)
            {
                continue;
            }

            Assert.All(
                device.Textures,
                texture => Assert.Equal(TextureAddressMode.Repeat, texture.AddressMode));

            Assert.Contains(device.Textures, texture => texture.MipLevels > 1);
            return;
        }

        Assert.Fail("No level shipped a ground whose materials resolved to textures.");
    }

    /// <summary>The ground is drawn in metres, where the camera and everything else already is.</summary>
    /// <remarks>
    /// <para>
    /// The sequel's terrain is stored in engine units, like every other authored coordinate, and the
    /// renderer works in metres. The presenter is where the two spaces meet, so the conversion is its
    /// job — and it was not doing it, which put the whole ground 4096 times too large and a few million
    /// metres away. Nothing failed; the level simply had no ground, and a missing ground looks exactly
    /// like a level that ships none.
    /// </para>
    /// <para>
    /// The assertion is made from a camera standing at the tile's own centre. If the presenter draws in
    /// metres, what it hands the device is a translation near zero and vertices spanning the tile's
    /// width in metres; if it draws in units, both are larger by 4096 and this fails by a margin no
    /// tolerance could hide.
    /// </para>
    /// <para>
    /// The quadtree is <em>not</em> converted, and that is deliberate rather than an omission: the
    /// level's <c>Detail</c> and <c>Adaption</c> scale a squared edge length the original measures in
    /// its own units, so the eye is converted into that space instead — the same division of labour
    /// Project I.G.I.'s octree traversal uses.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_ground_is_drawn_in_metres()
    {
        ITitlePresentationProvider provider = Igi2PresentationProvider.Instance;
        string root = GameDataSource.Igi2.RequireRoot();

        foreach (Igi2Level level in Igi2Level.All())
        {
            string archive = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
            if (!File.Exists(archive))
            {
                continue;
            }

            VirtualFileSystem files = new();
            files.RegisterDevice(new LocalFileDevice(root));
            files.SetAlias(
                VirtualFileSystem.MissionAlias,
                "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");
            ResourceRegistry resources = new(files);
            resources.Register(Igi2TerrainSurface.ArchiveResourceName, File.ReadAllBytes(archive));

            Igi2TerrainSurface surface = Igi2TerrainSurface.Build(level.Root, IresArchive.Read(File.ReadAllBytes(archive)), NullLogger.Instance);
            if (surface.IsEmpty)
            {
                continue;
            }

            RecordingGraphicsDevice device = new();
            using ITerrainPresenter? ground = provider.CreateGround(
                new GroundPresentationRequest(device, level.Root, resources, NullLogger.Instance));

            if (ground is null)
            {
                continue;
            }

            // Standing at the first tile's centre, converted -- which is where a camera that can see
            // this ground would actually be.
            Vector3d eye = WorldScene.ToMetres(surface.Tiles[0].Centre);
            ground.Draw(new TerrainFrame(
                new Camera3D(eye, Camera3D.LookRotation(0f, 0f)),
                Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.05f, 4000f),
                MathF.PI / 3f,
                16f / 9f,
                0,
                false,
                false));

            if (device.MeshDraws.Count == 0)
            {
                continue;
            }

            // A tile's own extent, in metres, is what a vertex stored relative to its centre can
            // reach -- and it is read off the tile rather than picked, so the bound means something on
            // any level rather than being a number that happens to hold on this one.
            Igi2TerrainTile tile = surface.Tiles[0];
            double halfExtent = Math.Max(tile.Width * tile.TexelWidth, tile.Height * tile.TexelHeight)
                * 0.5 / WorldScene.UnitsPerMetre;

            Assert.True(halfExtent > 1.0, "the tile has no extent, so the bound below would prove nothing.");

            // Twice it, because a tile's drawn geometry reaches a little past the span of its own
            // samples -- the quadtree stitches to its neighbours -- and because the thing being
            // separated here differs by a factor of 4096. A bound loose by two is still decisive by
            // three orders of magnitude, and a bound tight enough to argue about would be measuring
            // the tessellation rather than the units.
            double bound = halfExtent * 2.0;

            int checkedVertices = 0;
            foreach ((Vertex3D[] vertices, _) in device.Meshes.Values)
            {
                foreach (Vertex3D vertex in vertices)
                {
                    checkedVertices++;
                    Assert.InRange(MathF.Abs(vertex.Position.X), 0f, (float)bound);
                    Assert.InRange(MathF.Abs(vertex.Position.Y), 0f, (float)bound);
                }
            }

            Assert.True(checkedVertices > 0, "the ground drew nothing, so nothing here is measured.");

            // And the chunk origins, which carry the other half of the position. A tile centred away
            // from the world origin is the case that catches a missed conversion here; a tile centred
            // ON it -- which the first mission's are -- cannot, which is why the vertices above are the
            // assertion that does the work.
            foreach (RecordingGraphicsDevice.MeshDrawCall draw in device.MeshDraws)
            {
                float distance = MathF.Sqrt(
                    (draw.ModelView.M41 * draw.ModelView.M41)
                    + (draw.ModelView.M42 * draw.ModelView.M42)
                    + (draw.ModelView.M43 * draw.ModelView.M43));

                Assert.True(
                    distance < 100_000f,
                    $"{level} drew a terrain chunk {distance:F0} from an eye standing on it, which is engine units rather than metres.");
            }

            return;
        }

        Assert.Fail("No level shipped a ground that drew.");
    }

    /// <summary>Every shipped level's sky comes back through the seam, and draws.</summary>
    /// <remarks>
    /// <para>
    /// The wiring's own test, and it would have failed before the seam existed for a reason no counter
    /// would have shown: the host built <c>SkyRenderer</c> directly, and that type finds its sky by
    /// walking for a <c>DeclaredTask</c> named <c>FlatSky</c> — the node a title with no class for the
    /// type carries. I.G.I. 2 has a class for it, so the walk found nothing and the sequel drew no sky
    /// at all, silently, with every level loading and rendering otherwise correctly.
    /// </para>
    /// <para>
    /// So what this asserts is that the factory returns something for every level that declares a sky,
    /// and that the something draws a dome. Both halves matter: a factory that returned null for all 24
    /// would reproduce the original bug exactly.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_levels_sky_comes_back_through_the_seam()
    {
        ITitlePresentationProvider provider = Igi2PresentationProvider.Instance;
        string root = GameDataSource.Igi2.RequireRoot();
        int levels = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            VirtualFileSystem files = new();
            files.RegisterDevice(new LocalFileDevice(root));
            files.SetAlias(
                VirtualFileSystem.MissionAlias,
                "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");

            RecordingGraphicsDevice device = new();
            using ISkyPresenter? sky = provider.CreateSky(new SkyPresentationRequest(
                device,
                level.Root,
                files,
                new ResourceRegistry(files),
                160f,
                NullLogger.Instance));

            if (sky is null)
            {
                continue;
            }

            levels++;
            Assert.True(sky.HasSky);

            sky.Draw(new SkyFrame(new Camera3D(default, Camera3D.LookRotation(0.4f, -0.1f)), Matrix4f.Identity, 2f));

            // The dome, and one draw per cloud layer over it, all with the depth buffer out of the way.
            Assert.NotEmpty(device.MeshDraws);
            Assert.All(device.MeshDraws, draw => Assert.Equal(DepthState.Disabled, draw.DepthState));
            Assert.Equal(BlendMode.Opaque, device.MeshDraws[0].Blend);
        }

        // Every shipped level declares one, which is what makes the silence before this so complete.
        Assert.Equal(24, levels);
    }

    /// <summary>The optional factories default to null, so a title states only what it has.</summary>
    /// <remarks>
    /// The reason <see cref="Igi2PresentationProvider"/> is thirty lines rather than two hundred. A
    /// provider that implemented nothing is still a legal provider, which is what lets a title arrive
    /// one pass at a time instead of all at once.
    /// </remarks>
    [Fact]
    public void A_provider_that_implements_nothing_presents_nothing()
    {
        ITitlePresentationProvider empty = new SilentProvider();

        Assert.Null(empty.CreateWorld(default));
        Assert.Null(empty.CreateTerrain(default));
        Assert.Null(empty.CreateGround(default));
        Assert.Null(empty.CreateHud(default));
    }

    private sealed class SilentProvider : ITitlePresentationProvider;
}
