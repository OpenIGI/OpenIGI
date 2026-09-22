using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Whether this machine can actually run a WebGPU test.
/// </summary>
/// <remarks>
/// The native library is another project's redistributable and is fetched rather than carried, so a
/// fresh checkout does not have one until <c>tools/fetch-wgpu-native.ps1</c> is run. A test that needs
/// it is skipped rather than failed — but skipped with a reason that says which of the two it was, an
/// absent library or an adapter that refused, because those want opposite responses.
/// </remarks>
public sealed class WebGpuAvailable
{
    private static readonly Lazy<string?> Reason = new(Probe, isThreadSafe: true);

    /// <summary>Gets why a device cannot be had here, or null when one can.</summary>
    public static string? Unavailable => Reason.Value;

    /// <summary>Gets whether a device can be had here.</summary>
    public static bool Yes => Reason.Value is null;

    private static string? Probe()
    {
        try
        {
            using WebGpuContext context = WebGpuContext.Create();
            return null;
        }
        catch (DllNotFoundException)
        {
            return "wgpu-native is not on this machine. Run tools/fetch-wgpu-native.ps1.";
        }
        catch (EntryPointNotFoundException error)
        {
            return $"The wgpu-native that loaded is not the pinned release: {error.Message}";
        }
        catch (InvalidOperationException error)
        {
            return $"No WebGPU device here: {error.Message}";
        }
        catch (TimeoutException error)
        {
            return $"WebGPU did not answer: {error.Message}";
        }
    }
}

/// <summary>A fact that runs only where a WebGPU device can be had.</summary>
public sealed class WebGpuFactAttribute : FactAttribute
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuFactAttribute"/> class.</summary>
    public WebGpuFactAttribute() => Skip = WebGpuAvailable.Unavailable;
}
