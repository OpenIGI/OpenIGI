using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>Another player's name, standing over its head in the level.</summary>
/// <param name="AnchorMetres">Where the tag stands, in metres: just over the drawn body's head, as it is drawn this frame.</param>
/// <param name="Name">The player's name.</param>
public readonly record struct WorldNameTag(Vector3d AnchorMetres, string Name);
