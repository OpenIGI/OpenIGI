using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The one finiteness test every effect renderer runs before it turns a world-space point or axis
/// into vertices. A NaN or infinity that slips into a quad's corners stretches it across the whole
/// screen, so each emitter drops the particle instead of drawing it.
/// </summary>
internal static class FiniteChecks
{
    /// <summary>True when every component of a single-precision vector is a finite number.</summary>
    internal static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>True when every component of a double-precision vector is a finite number.</summary>
    internal static bool IsFinite(Vector3d v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
