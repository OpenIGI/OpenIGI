using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Game.Igi2;

/// <summary>Builds the renderers I.G.I. 2 draws with.</summary>
/// <remarks>
/// <para>
/// <b>One member, and that is the honest surface.</b> The sequel can draw its placed world — every
/// milestone up to here was about getting it to the point where it could — and it has no first-person
/// arms, no held weapon, no HUD and no map computer, because none of those systems exist for it yet.
/// Every other factory is left at the interface's default of null, which the host reads as "this title
/// does not present that pass" and skips.
/// </para>
/// <para>
/// The world presenter itself is <c>WorldRenderer</c>, shared with Project I.G.I. It named no task type,
/// no model id and no asset path, so it moved into the presentation layer and both titles draw through
/// it; what differs is only how a level becomes placements, which is
/// <see cref="World.Igi2DrawableScene"/>'s job.
/// </para>
/// <para>
/// The ground is deliberately absent rather than stubbed. <see cref="TerrainPresentationRequest"/>
/// carries a <c>TerrainSurface</c> built from Project I.G.I.'s chunked octree, and the sequel's ground is
/// a flat grid of <c>.thm</c> tiles that does not produce one. Returning null says that truthfully;
/// returning something that drew nothing would not.
/// </para>
/// </remarks>
public sealed class Igi2PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, request.Logger);
}
