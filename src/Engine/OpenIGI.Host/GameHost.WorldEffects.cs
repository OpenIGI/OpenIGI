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
        public IMissionGameplay Gameplay => host._igi1;

        public void ResetTitleImpacts(int mistFrames) => host._igi1.Impacts.Reset(mistFrames);
    }
}
