using OpenIGI.Rendering.Streamline;
using Xunit;

namespace OpenIGI.Rendering.Streamline.Tests;

/// <summary>
/// Pins the signature policy the Streamline folder is held to before anything in it is loaded.
/// </summary>
/// <remarks>
/// Every DLL in the folder must be signed, and by the right publisher: NVIDIA for Streamline and its
/// models, Microsoft for the C++ runtime they import. Anything else in the folder refuses the folder,
/// because loading the interposer by path puts the folder on the search path for what it imports.
/// </remarks>
public sealed class SignedModuleTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("openigi-signed-module").FullName;

    [Theory]
    [InlineData("sl.interposer.dll", "O=NVIDIA Corporation")]
    [InlineData("sl.dlss_g.dll", "O=NVIDIA Corporation")]
    [InlineData("nvngx_dlss.dll", "O=NVIDIA Corporation")]
    [InlineData("NvLowLatencyVk.dll", "O=NVIDIA Corporation")]
    [InlineData("msvcp140.dll", "O=Microsoft Corporation")]
    [InlineData("VCRUNTIME140_1.dll", "O=Microsoft Corporation")]
    [InlineData("anything-else.dll", "O=NVIDIA Corporation")]
    public void Each_file_has_the_publisher_it_must_be_signed_by(string file, string publisher)
    {
        Assert.Equal(publisher, SignedModule.ExpectedPublisher(file));
    }

    [Fact]
    public void An_unsigned_module_refuses_the_whole_folder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllBytes(Path.Combine(_folder, "sl.interposer.dll"), [0x4D, 0x5A, 0, 0]);

        Assert.False(SignedModule.IsFolderTrusted(_folder, out string? refused));
        Assert.Contains("sl.interposer.dll", refused, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_with_no_streamline_module_is_refused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(SignedModule.IsFolderTrusted(_folder, out string? refused));
        Assert.NotNull(refused);
    }

    [Fact]
    public void A_relative_folder_is_refused()
    {
        Assert.False(SignedModule.IsFolderTrusted("relative/folder", out _));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
