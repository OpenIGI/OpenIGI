using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>The light a trace reads where it strikes an unlit surface: the level's ambient and its sun.</summary>
public interface IWorldSceneLighting
{
    /// <summary>Gets the ambient colour the level's global light sets (the globals <c>igi2 0x7F9166C</c>–<c>0x7F91674</c>).</summary>
    Vector3f Ambient { get; }

    /// <summary>Gets the sun, <c>SkyDome_ptDirlight</c>, if the level has one.</summary>
    /// <param name="direction">The direction the sun's light shines toward a surface it lights, <c>tDir</c>.</param>
    /// <param name="colour">The colour of a lit face, <c>tFrontColor</c>.</param>
    /// <returns>Whether there is a sun.</returns>
    bool TryGetSun(out Vector3f direction, out Vector3f colour);
}
