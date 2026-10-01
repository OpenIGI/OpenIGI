using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>Whether this machine has both backends the agreement tests compare.</summary>
/// <remarks>
/// An agreement test draws the same thing through Vulkan and through WebGPU and compares the pictures, so
/// a machine with only one of them (a CI runner has neither a GPU nor wgpu-native's Linux build) has
/// nothing to compare. Such a test is skipped there with the reason, the way the single-backend suites
/// skip, instead of failing on something that says nothing about the code.
/// </remarks>
internal static class BothBackends
{
    private static readonly Lazy<string?> Reason = new(Probe, isThreadSafe: true);

    /// <summary>Gets why the comparison cannot run here, or <see langword="null"/> when it can.</summary>
    public static string? Unavailable => Reason.Value;

    private static string? Probe()
    {
        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(4, 4, GraphicsDevice.DefaultPresentFormat);
        }
        catch (Exception error)
        {
            return $"No Vulkan device here: {error.Message}";
        }

        try
        {
            using WebGpuRhiDevice webgpu = WebGpuRhiDevice.Create();
        }
        catch (Exception error)
        {
            return $"No WebGPU device here: {error.Message}";
        }

        return null;
    }
}

/// <summary>A fact that compares the two backends, run only where both exist.</summary>
public sealed class BothBackendsFactAttribute : FactAttribute
{
    /// <summary>Initialises a new instance of the <see cref="BothBackendsFactAttribute"/> class.</summary>
    public BothBackendsFactAttribute() => Skip = BothBackends.Unavailable;
}

/// <summary>A theory that compares the two backends, run only where both exist.</summary>
public sealed class BothBackendsTheoryAttribute : TheoryAttribute
{
    /// <summary>Initialises a new instance of the <see cref="BothBackendsTheoryAttribute"/> class.</summary>
    public BothBackendsTheoryAttribute() => Skip = BothBackends.Unavailable;
}
