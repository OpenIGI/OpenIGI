using OpenIGI.Rendering;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The fog shaders size two arrays from constants the host fills them by, and the two have to agree.
/// </summary>
/// <remarks>
/// The block is written once in <c>Source/Common/FogParams.slang</c> and its array lengths arrive as
/// preprocessor defines rather than as literals, so the numbers live in one place -- the C# constants
/// below -- and the manifest carries them to the compiler. Nothing else checks that carriage: a
/// shader sized smaller than the host writes would have the host scribbling past the end of its own
/// uniform block, and a shader sized larger would read whatever followed.
/// </remarks>
public sealed class FogArraySizeTests
{
    /// <summary>The fog modules, which share the block.</summary>
    public static TheoryData<ShaderModuleId> FogModules => [ShaderModuleId.FogScatter, ShaderModuleId.FogIntegrate];

    /// <summary>Each fog module is compiled with the array sizes the host fills the block by.</summary>
    /// <param name="module">The module.</param>
    [Theory]
    [MemberData(nameof(FogModules))]
    public void The_block_is_sized_by_the_constants_the_host_fills_it_by(ShaderModuleId module)
    {
        ShaderVariantEntry variant = Assert.Single(ShaderCatalog.Manifest.Module(module).Variants);

        Assert.Equal(
            LocalFogVolume.MaxPerLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            variant.Defines["OPENIGI_MAX_FOG_VOLUMES"]);
        Assert.Equal(
            DeferredLocalLight.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture),
            variant.Defines["OPENIGI_MAX_LAMPS"]);
    }
}
