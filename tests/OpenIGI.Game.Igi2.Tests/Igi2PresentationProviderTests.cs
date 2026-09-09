using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation;
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
        Assert.Null(empty.CreateHud(default));
    }

    private sealed class SilentProvider : ITitlePresentationProvider;
}
