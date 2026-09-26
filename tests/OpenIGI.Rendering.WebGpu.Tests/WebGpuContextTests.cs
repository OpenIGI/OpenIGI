using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;
using Xunit.Abstractions;
using Wgpu = OpenIGI.Rendering.WebGpu.Native.WebGpu;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Acquiring a real device from the real library.
/// </summary>
/// <remarks>
/// The smallest thing that can be run on a new machine, and the first thing worth running: an adapter
/// that refuses, a driver that never answers, and a library from the wrong release all fail here rather
/// than somewhere in a frame. Skipped where the library has not been fetched — see
/// <see cref="WebGpuAvailable"/>.
/// </remarks>
public sealed class WebGpuContextTests(ITestOutputHelper output)
{
    [WebGpuFact]
    public void A_device_can_be_had()
    {
        using WebGpuContext context = WebGpuContext.Create();

        Assert.True(context.Instance.IsValid);
        Assert.True(context.Adapter.IsValid);
        Assert.True(context.Device.IsValid);
        Assert.True(context.Queue.IsValid);
    }

    [WebGpuFact]
    public void The_adapter_says_what_it_is_and_what_it_can_do()
    {
        using WebGpuContext context = WebGpuContext.Create();
        WebGpuAdapterReport report = context.Report;

        output.WriteLine($"adapter  {report.Name}");
        output.WriteLine($"backend  {report.Backend}");
        output.WriteLine($"features {report.Features}");
        output.WriteLine($"storage  tier1={report.ExtendedStorageFormats}");
        output.WriteLine($"limits   {report.Limits}");

        Assert.NotEmpty(report.Name);
        Assert.NotEqual(WGPUBackendType.Undefined, report.Backend);
    }

    [WebGpuFact]
    public void The_device_meets_the_floor_every_pass_is_written_against()
    {
        // If this ever fails, a real adapter offers less than the WebGPU specification guarantees, and
        // RhiLimits.WebGpuDefaults is not the floor it claims to be.
        using WebGpuContext context = WebGpuContext.Create();

        Assert.True(
            context.Report.Limits.Meets(RhiLimits.WebGpuDefaults),
            $"The device reports {context.Report.Limits}, which is under the guaranteed floor.");
    }

    [WebGpuFact]
    public void The_loaded_library_is_the_release_the_bindings_were_generated_from()
    {
        // wgpu-native packs its version as one byte per component. A library from another release does
        // not fail at load; it fails in the middle of a frame at a structure whose layout has moved,
        // which is the failure this exists to convert into a sentence.
        uint packed = WebGpuContext.LoadedVersion;
        string loaded = $"{(packed >> 24) & 0xFF}.{(packed >> 16) & 0xFF}.{(packed >> 8) & 0xFF}.{packed & 0xFF}";

        output.WriteLine($"loaded   {loaded}");
        output.WriteLine($"bindings {Wgpu.Version}");

        Assert.Equal(Wgpu.Version.TrimStart('v'), loaded);
    }

    [WebGpuFact]
    public void Polling_an_idle_device_says_it_is_idle()
    {
        using WebGpuContext context = WebGpuContext.Create();

        context.ProcessEvents();

        Assert.True(context.Poll());
    }

    [WebGpuFact]
    public void Disposing_twice_releases_once()
    {
        // Every WebGPU object is reference counted, and releasing one twice drops a count that was not
        // taken. What that corrupts is the library's own table, so the report arrives somewhere else.
        WebGpuContext context = WebGpuContext.Create();

        context.Dispose();
        context.Dispose();

        Assert.False(context.Device.IsValid);
    }

    [Fact]
    public void The_reason_a_device_cannot_be_had_is_said_out_loud()
    {
        // Not skipped: on a machine with no library this is the test that explains the other five, and
        // on a machine with one it asserts the probe agrees with itself.
        if (WebGpuAvailable.Unavailable is { } reason)
        {
            output.WriteLine(reason);
            Assert.NotEmpty(reason);
            return;
        }

        Assert.True(WebGpuAvailable.Yes);
    }
}
