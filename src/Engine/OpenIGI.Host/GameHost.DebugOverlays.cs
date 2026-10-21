using OpenIGI.Engine.Resources;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The tools over the frame: the sun gizmo, the F12 overlay and the r_perflog panel.
    private readonly DebugOverlays _debugOverlays;

    /// <summary>The host as the developer overlays read it.</summary>
    /// <param name="host">The host.</param>
    private sealed class DebugOverlaysHost(GameHost host) : IDebugOverlaysHost
    {
        public IWorldPresenter? World => host._world;

        public FreeCamera? Camera => host._camera;

        public ITerrainPresenter? Terrain => host._terrain;

        public ObjectCollisionIndex? Solids => host._mission.Solids;

        public ISoldierBodies? Soldiers => host._soldiers;

        public bool PlayerMode => host._playerMode;

        public IMissionViewpoint? Viewpoint => host.Viewpoint;

        public DebugOverlayRenderer? Overlay => host._debugOverlay;

        public SunGizmo SunGizmo => host._sunGizmo;

        public ResourceOverrides Overrides => host._overrides;

        public ResourceRegistry Resources => host._resources;

        public int SafeWindowWidth => host.SafeWindowWidth;

        public int SafeWindowHeight => host.SafeWindowHeight;

        public string? DescribeAimedLamp() => host._lamps.DescribeAimed();
    }
}
