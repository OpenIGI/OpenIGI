using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>One thing the bone debug view draws, in world metres.</summary>
/// <param name="From">A bone's joint, or one end of a capsule.</param>
/// <param name="To">The joint of the bone above it, or the capsule's other end.</param>
/// <param name="Radius">A capsule's radius in metres; zero for a bone.</param>
/// <param name="Label">The bone's name, or <see langword="null"/> for a capsule.</param>
public readonly record struct BoneDebugMark(Vector3d From, Vector3d To, double Radius, string? Label);
