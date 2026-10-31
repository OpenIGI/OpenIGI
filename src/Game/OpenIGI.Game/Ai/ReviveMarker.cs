using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>A fallen teammate a player can revive, as the display marks it in the level.</summary>
/// <param name="AnchorMetres">Where the mark stands, in metres: just over the body.</param>
/// <param name="Name">The fallen player's name.</param>
/// <param name="Progress">How far the revival has gone, in <c>[0,1]</c>.</param>
public readonly record struct ReviveMarker(Vector3d AnchorMetres, string Name, float Progress);
