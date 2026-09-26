using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The material record a traced hit is walked back to, and the two shaders that reach into it.
/// </summary>
/// <remarks>
/// One record serves both the sun shadow's cut-out test and the reflection's hit shading, and each
/// reaches into it by word offset. Landing on the same word does not fail loudly: a cut-out surface
/// would test its alpha against a texture layer number, so a fence would come out solid or absent
/// depending on which layer its colour happened to get. The offsets are named in one include now,
/// which is most of the guard; these are the rest of it.
/// </remarks>
public sealed class HitRecordLayoutTests
{
    private const string Layout = "Common/HitRecords.slang";

    /// <summary>The mask's layer and the albedo's layer are different words.</summary>
    [Fact]
    public void The_mask_and_the_albedo_are_different_words()
    {
        string source = ShaderRepository.ReadSource(Layout);

        Assert.Contains("kHitMaskLayerWord = 5u;", source, StringComparison.Ordinal);
        Assert.Contains("kHitAlbedoLayerWord = 6u;", source, StringComparison.Ordinal);
    }

    /// <summary>Each shader reads the word that belongs to it, by name rather than by number.</summary>
    /// <remarks>
    /// A literal offset in either shader would be invisible to the test above: it could agree with the
    /// constant today and drift from it silently. Asserting the NAME is what ties the two together.
    /// </remarks>
    [Fact]
    public void Each_shader_reads_its_own_word_by_name()
    {
        string shadow = ShaderRepository.ReadSource("Deferred/SunShadow.slang");
        string material = ShaderRepository.ReadSource("Common/TracedMaterial.slang");

        Assert.Contains("records[at + kHitMaskLayerWord]", shadow, StringComparison.Ordinal);
        Assert.DoesNotContain("kHitAlbedoLayerWord", shadow, StringComparison.Ordinal);

        Assert.Contains("records[at + kHitAlbedoLayerWord]", material, StringComparison.Ordinal);
        Assert.DoesNotContain("kHitMaskLayerWord", material, StringComparison.Ordinal);
    }
}
