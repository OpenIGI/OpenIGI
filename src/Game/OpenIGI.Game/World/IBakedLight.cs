using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The light a level bakes at a point: what its lightmaps say of the ground or floor there, not what is drawn.</summary>
/// <remarks>
/// <para>
/// The scale is the one both games' lightmaps share as they draw: nought is dark, and one shows a surface as bright as its
/// texture. I.G.I. 2 stores a texel as a byte times 2/255 and draws it doubled, so its own lightmaps reach two; Project I.G.I.
/// multiplies its surfaces by their lightmap once, so its brightest texel is one.
/// </para>
/// <para>
/// I.G.I. 2 asks this of the face under a human's feet as it stands (<c>Human_GroundCollision</c>), and sees its player by it.
/// Project I.G.I. never asks it: its terrain cells hand their pages only to the terrain's draw lists (<c>TerrainLightMap</c>'s
/// event 15 at <c>0x4F82B0</c>, read by <c>0x4D14E0</c>). A level of it answers so that another game's rules can.
/// </para>
/// </remarks>
public interface IBakedLight
{
    /// <summary>Gets the light baked at a point.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>The light, or <see langword="null"/> where this bakes none there.</returns>
    float? LightAt(Vector3d point);
}
