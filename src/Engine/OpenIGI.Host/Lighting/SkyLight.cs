using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Lighting;

/// <summary>The sky's own light, as the frame and the surface cache both fill with it.</summary>
internal static class SkyLight
{
    /// <summary>The fill a sun's sky gives, at the player's sky-light setting.</summary>
    /// <param name="sun">The sun whose ambient the sky carries.</param>
    /// <returns>The colour.</returns>
    public static Vector3f Fill(DirectionalLightSettings sun) => sun.Ambient * SkyCVars.SkyLight.Value;
}
