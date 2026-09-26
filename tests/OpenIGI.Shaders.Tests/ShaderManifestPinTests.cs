using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The compiler the committed output was produced with, and which other compilers count as it.
/// </summary>
/// <remarks>
/// The build tool holds a check to this: two releases of slangc do not agree byte for byte about SPIR-V
/// they both accept, so a check run against the wrong one reports every artefact stale and says nothing
/// about the sources. Getting the rule wrong in either direction is quiet -- too strict and a correct
/// checkout fails, too loose and a real drift is compiled over.
/// </remarks>
public sealed class ShaderManifestPinTests
{
    /// <summary>The library states which compiler produced it.</summary>
    [Fact]
    public void The_manifest_pins_a_compiler()
    {
        string pinned = ShaderCatalog.Manifest.SlangVersion;

        Assert.False(string.IsNullOrWhiteSpace(pinned));
        Assert.False(string.IsNullOrWhiteSpace(ShaderManifest.ReleaseOf(pinned)));
        Assert.True(ShaderCatalog.Manifest.Accepts(pinned));
    }

    /// <summary>A release is what a version string says before its build.</summary>
    [Theory]
    [InlineData("2026.13.1", "2026.13.1")]
    [InlineData("2026.13.1-1-g84792eb15", "2026.13.1")]
    [InlineData("  2026.13.1  ", "2026.13.1")]
    [InlineData("2026.13.1-", "2026.13.1")]
    public void A_release_is_everything_before_the_build(string version, string release) =>
        Assert.Equal(release, ShaderManifest.ReleaseOf(version));

    /// <summary>Any build of the pinned release is accepted; another release is not.</summary>
    /// <remarks>
    /// Measured rather than assumed: the Vulkan SDK's build and the tagged download of one release were
    /// run over the whole library and produced byte-identical output for every artefact.
    /// </remarks>
    [Fact]
    public void Any_build_of_the_pinned_release_counts_as_it()
    {
        ShaderManifest manifest = new("2026.13.1-1-g84792eb15", ShaderCatalog.Manifest.Modules);

        Assert.True(manifest.Accepts("2026.13.1-1-g84792eb15"));
        Assert.True(manifest.Accepts("2026.13.1"));
        Assert.True(manifest.Accepts("2026.13.1-7-gdeadbeef"));

        Assert.False(manifest.Accepts("2026.13.2"));
        Assert.False(manifest.Accepts("2026.14.0-1-g84792eb15"));
        Assert.False(manifest.Accepts("2025.13.1"));
    }
}
