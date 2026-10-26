using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>One of a soldier model's lower levels of detail, the instance named after it with its last character counted on.</summary>
/// <param name="Model">The level's model, skinned through the same skeleton as the first.</param>
/// <param name="Geometry">Its geometry.</param>
/// <param name="Instance">Its instance in its pack, which names its textures.</param>
/// <param name="Pack">The pack it came from.</param>
internal sealed record SoldierLevel(MefModel Model, MeshGeometry Geometry, MtpInstance? Instance, LoadedPack? Pack);
