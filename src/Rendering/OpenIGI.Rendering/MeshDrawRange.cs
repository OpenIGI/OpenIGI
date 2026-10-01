namespace OpenIGI.Rendering;

/// <summary>One indexed triangle range within an uploaded mesh.</summary>
/// <param name="IndexOffset">The first 16-bit index.</param>
/// <param name="IndexCount">The number of indices, divisible by three.</param>
public readonly record struct MeshDrawRange(int IndexOffset, int IndexCount);
