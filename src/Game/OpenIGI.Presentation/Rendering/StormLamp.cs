using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A lamp the storm's drops are lit by, as the deferred lighting has it this frame.</summary>
/// <param name="Position">Where it is, in metres from the eye, in world axes.</param>
/// <param name="Reach">How far its light reaches, in metres; nothing past it.</param>
/// <param name="Radiance">Its colour times its intensity, before distance.</param>
/// <param name="Facing">The way a directional fitting points, or nought for one that shines all round.</param>
public readonly record struct StormLamp(Vector3f Position, float Reach, Vector3f Radiance, Vector3f Facing);
