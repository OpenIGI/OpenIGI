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
        public bool HasPlayer => host.HostedPlayer is not null;

        public WorldRayCast? Walls => host._mission.EffectWalls ?? host.HostedPlayer?.Body.Walls;

        public BulletMarks? Marks => host.HostedPlayer?.Marks;

        public WorldRayCast? MarkProbe => host.HostedPlayer?.MarkProbe;

        public GameMaterialRegistry? Materials => host._mission.Materials;

        public WorldTraceCast? Trace => host._mission.Trace;

        public bool BloodWanted => host._configuration.Active?.BloodEnabled ?? true;

        public Camera3D Eye => host._inspectorEye;
    }
}
