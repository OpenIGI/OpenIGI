using OpenIGI.Game.World;
using OpenIGI.Host.Services;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The world's effects: the impact bursts, the original's own impact effects, the fireball and the smoke.
    private readonly WorldEffects _effects;

    /// <summary>The host as the world's effects read it.</summary>
    /// <param name="host">The host.</param>
    private sealed class WorldEffectsHost(GameHost host) : IWorldEffectsHost
    {
        public bool HasPlayer => host._player is not null;

        public WorldRayCast? Walls => host._effectWalls ?? host._player?.Body.Walls;

        public BulletMarks? Marks => host._player?.Marks;

        public WorldRayCast? MarkProbe => host._player?.MarkProbe;

        public GameMaterialRegistry? Materials => host._worldMaterials;

        public WorldTraceCast? Trace => host._worldTrace;

        public bool BloodWanted => host._configuration.Active?.BloodEnabled ?? true;

        public Camera3D Eye => host._inspectorEye;
    }
}
