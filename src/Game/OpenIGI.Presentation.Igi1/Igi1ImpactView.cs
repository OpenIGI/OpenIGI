using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Project I.G.I.'s own impact effects and explosions, drawn by the retail impact renderer and the explosion drawer from
/// the mission's runtimes.
/// </summary>
/// <param name="renderer">The impact renderer: the dust, the chips and the blood, and every particle system.</param>
/// <param name="explosions">The explosion drawer: the FireBalls and the fragments.</param>
/// <param name="gameplay">The mission, whose effects are drawn.</param>
internal sealed class Igi1ImpactView(RetailImpactRenderer renderer, RetailExplosionEffects explosions, Igi1Gameplay gameplay)
    : IMissionImpactView
{
    /// <inheritdoc/>
    public int MistFrameCount => renderer.MistFrameCount;

    /// <inheritdoc/>
    public bool SpritesAvailable => renderer.SpritesAvailable;

    /// <inheritdoc/>
    public int Load(LoadedMission mission)
    {
        _ = explosions.Load(mission);
        return renderer.Load(mission);
    }

    /// <inheritdoc/>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
    {
        renderer.Draw(gameplay.Impacts.Runtime, camera, projection, blend);
        explosions.Draw(gameplay.Impacts.Explosions, camera, projection, blend);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        explosions.Release();
        renderer.Dispose();
    }
}
