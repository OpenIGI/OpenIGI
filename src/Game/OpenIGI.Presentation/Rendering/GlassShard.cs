using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One shard record of a shattered pane, in world units, as <see cref="GlassShatterBuilder"/> lays it.</summary>
/// <param name="Centre">The shard's centre.</param>
/// <param name="A">The first corner.</param>
/// <param name="B">The second corner.</param>
/// <param name="C">The third corner.</param>
/// <param name="Normal">The shard's world-space unit normal.</param>
/// <param name="SizeMetres">The particle size the shard is drawn at.</param>
/// <param name="Velocity">The kick away from the impact, per tick.</param>
/// <param name="LifeTicks">How long the shard lives.</param>
internal readonly record struct GlassShard(
    Vector3d Centre,
    Vector3d A,
    Vector3d B,
    Vector3d C,
    Vector3d Normal,
    float SizeMetres,
    Vector3f Velocity,
    int LifeTicks);
