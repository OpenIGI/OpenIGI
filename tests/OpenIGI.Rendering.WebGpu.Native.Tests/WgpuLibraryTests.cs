using System.Runtime.InteropServices;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Native.Tests;

/// <summary>
/// Where the native library is looked for.
/// </summary>
/// <remarks>
/// The release the bindings were generated against is the release that has to be loaded. A different one
/// does not fail at load; it fails in the middle of a frame at a structure whose layout has moved, which
/// is why loading from a known folder beside the game comes before whatever the system happens to have.
/// </remarks>
public sealed class WgpuLibraryTests
{
    [Fact]
    public void The_file_is_named_the_way_this_platform_names_one()
    {
        string name = WgpuLibrary.FileName();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("wgpu_native.dll", name);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("libwgpu_native.dylib", name);
        }
        else
        {
            Assert.Equal("libwgpu_native.so", name);
        }
    }

    [Fact]
    public void The_runtime_identifier_names_the_platform_and_the_architecture()
    {
        string rid = WgpuLibrary.RuntimeIdentifier();

        string platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        Assert.StartsWith(platform + "-", rid, StringComparison.Ordinal);

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => rid[(platform.Length + 1)..],
        };

        Assert.EndsWith("-" + architecture, rid, StringComparison.Ordinal);
    }

    [Fact]
    public void Beside_the_application_is_looked_at_first()
    {
        string[] candidates = [.. WgpuLibrary.Candidates("/somewhere")];

        Assert.NotEmpty(candidates);
        Assert.Equal(
            Path.Combine("/somewhere", WgpuLibrary.FileName()),
            candidates[0]);
    }

    [Fact]
    public void Every_candidate_ends_in_the_platform_file_name()
    {
        foreach (string candidate in WgpuLibrary.Candidates("/somewhere"))
        {
            Assert.Equal(WgpuLibrary.FileName(), Path.GetFileName(candidate));
        }
    }

    [Fact]
    public void The_fetched_folder_is_looked_at_by_runtime_identifier_and_without_it()
    {
        string[] candidates = [.. WgpuLibrary.Candidates("/somewhere")];

        Assert.Contains(
            Path.Combine("/somewhere", "third-party", "wgpu-native", WgpuLibrary.RuntimeIdentifier(), WgpuLibrary.FileName()),
            candidates);

        Assert.Contains(
            Path.Combine("/somewhere", "third-party", "wgpu-native", WgpuLibrary.FileName()),
            candidates);
    }

    [Fact]
    public void Registering_more_than_once_is_allowed()
    {
        // The resolver may only be set once per assembly, and more than one thing has reason to make
        // sure it has been: the device, a test, a tool. A second call has to be a no-op rather than a
        // throw, or the second caller decides the library is unusable.
        WgpuLibrary.Register();
        WgpuLibrary.Register();
    }
}
