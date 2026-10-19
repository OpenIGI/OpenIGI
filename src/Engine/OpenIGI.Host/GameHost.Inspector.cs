using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>What the console's inspector reads of the host.</summary>
    /// <param name="host">The host.</param>
    private sealed class InspectedWorld(GameHost host) : IInspectedWorld
    {
        public IHostedPlayer? Player => host.HostedPlayer;

        public bool PlayerMode => host._playerMode;

        public bool PlayerDead => host._mission.IsPlayerDead;

        public FreeCamera? Camera => host._camera;

        public WorldScene? Scene => host._scene;

        public IWorldPresenter? World => host._world;

        public ITerrainPresenter? Terrain => host._terrain;

        public IGroundHeightField? Ground => host._mission.Ground;

        public ISoldierBodies? Soldiers => host._soldiers;

        public IInspectableMission Mission => host._mission;

        public Camera3D InspectorEye => host._inspectorEye;

        public float InspectorAspect => host._inspectorAspect;

        public float InspectorTangent => host._inspectorTangent;

        public int WindowWidth => host._window.Width;

        public int WindowHeight => host._window.Height;
    }
}
