using OpenIGI.Core.Diagnostics;
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
    /// <b>The ground draws untextured, and that is the true state of the port.</b> A terrain material
    /// names a full path with its own extension — <c>MISSION:textures/k_grass.jpg</c> — which resolves
    /// to a loose JPEG beside the level's archive. 76 of those ship, against 24 <c>.tex</c>, and nothing
    /// here decodes one. The pass therefore runs with its baked lighting and no surface, which the test
    /// asserts rather than papers over.
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
            resources.Register(Igi2PresentationProvider.HeightmapArchive, File.ReadAllBytes(archive));
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
            Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.Additive, draw.Blend));

            // Chunked at the original's 10,000-index budget, not one draw per pass.
            Assert.All(device.MeshDraws, draw => Assert.InRange(draw.IndexCount, 3, 9999));

            // And untextured, for now: every terrain texture the corpus names is a JPEG.
            Assert.All(device.MeshDraws, draw => Assert.Equal(TextureHandle.None, draw.Diffuse));
            return;
        }

        Assert.Fail("No level shipped both terrain and textures.");
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
