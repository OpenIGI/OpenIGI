namespace OpenIGI.Rendering.Rhi;

/// <summary>Whether a backend names its passes to the driver, for a GPU profiler or frame debugger to show.</summary>
/// <remarks>
/// Off unless <c>OPENIGI_GPU_MARKERS=1</c>: every pass then opens a named group in the API's own debug-label
/// mechanism, so a trace breaks the frame down by pass the same way on every backend, and two backends can be
/// compared pass for pass. A profiler wants the names without the validation layers that would otherwise bring
/// them, whose checks would be in what it measures.
/// </remarks>
public static class RhiPassMarkers
{
    /// <summary>Gets whether pass markers were asked for in the environment.</summary>
    public static bool Requested { get; } =
        Environment.GetEnvironmentVariable("OPENIGI_GPU_MARKERS")?.Trim().ToUpperInvariant() is "1" or "TRUE" or "ON";

    /// <summary>Gets the name a pass is shown under: its label, or a placeholder where it has none.</summary>
    /// <param name="label">The pass's label.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string? label) => string.IsNullOrEmpty(label) ? "pass" : label;
}
