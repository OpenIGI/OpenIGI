using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>A model-local axis-aligned box used only for conservative visibility testing.</summary>
public readonly record struct OcclusionBounds(Vector3f Minimum, Vector3f Maximum);
