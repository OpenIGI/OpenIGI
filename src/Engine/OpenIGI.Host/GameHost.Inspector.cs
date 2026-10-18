using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>What the console's inspector reads of the host.</summary>
    /// <param name="host">The host.</param>
    private sealed class InspectedWorld(GameHost host) : IInspectedWorld
    {
        public PlayerCamera? Player => host._player;

        public bool PlayerMode => host._playerMode;

        public bool PlayerDead => host._mission.IsPlayerDead;

        public FreeCamera? Camera => host._camera;

        public WorldScene? Scene => host._scene;

        public IWorldPresenter? World => host._world;

        public ITerrainPresenter? Terrain => host._terrain;

        public IGroundHeightField? Ground => host._ground;

        public ISoldierPresenter? Soldiers => host._soldiers;

        public MissionWorld Mission => host._mission;

        public Camera3D InspectorEye => host._inspectorEye;

        public float InspectorAspect => host._inspectorAspect;

        public float InspectorTangent => host._inspectorTangent;

        public int WindowWidth => host._window.Width;

        public int WindowHeight => host._window.Height;
    }
}
