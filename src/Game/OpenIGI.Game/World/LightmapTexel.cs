using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One lightmap texel's surface: the point on the mesh it covers, or <see cref="IsCovered"/> false.</summary>
/// <param name="WorldPosition">Where on the surface this texel sits, in world metres.</param>
/// <param name="Normal">The surface normal there, world space and unit length.</param>
/// <param name="IsCovered">Whether a triangle covered this texel at all; an uncovered texel is a gutter pixel.</param>
public readonly record struct LightmapTexel(Vector3d WorldPosition, Vector3f Normal, bool IsCovered);
