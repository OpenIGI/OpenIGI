using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A glow sprite a title draws over the world at a point: a lamp, a flare.</summary>
/// <param name="PositionUnits">Where it is, in world units.</param>
/// <param name="HalfSizeUnits">Half its width and height, in world units.</param>
/// <param name="Colour">Its colour, added to what is behind it.</param>
public readonly record struct WorldGlow(Vector3d PositionUnits, float HalfSizeUnits, Bgra32 Colour);
