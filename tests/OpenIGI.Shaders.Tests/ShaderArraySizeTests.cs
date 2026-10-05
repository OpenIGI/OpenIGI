using System.Globalization;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// Several shaders size an array in their parameter block from a constant the host fills it by, and
/// the two have to agree.
/// </summary>
/// <remarks>
/// The lengths arrive as preprocessor defines rather than as literals, so the numbers live in one
/// place -- the C# constants below -- and the manifest carries them to the compiler. Nothing else
/// checks that carriage: a shader sized smaller than the host writes would have the host scribbling
/// past the end of its own uniform block, and a shader sized larger would read whatever followed.
/// </remarks>
public sealed class ShaderArraySizeTests
{
    /// <summary>The modules whose block carries a per-lamp array.</summary>
    public static TheoryData<ShaderModuleId> LampModules =>
        [ShaderModuleId.FogScatter, ShaderModuleId.FogIntegrate, ShaderModuleId.LampShadow];

    /// <summary>The modules whose block carries a per-volume array, which is the fog pair.</summary>
    public static TheoryData<ShaderModuleId> FogVolumeModules =>
        [ShaderModuleId.FogScatter, ShaderModuleId.FogIntegrate];

    /// <summary>Each module is compiled with the lamp count the host fills its block by.</summary>
    /// <param name="module">The module.</param>
    [Theory]
    [MemberData(nameof(LampModules))]
    public void The_lamp_array_is_sized_by_the_constant_the_host_fills_it_by(ShaderModuleId module)
    {
        // Every variant: the lamp trace has a traced form and a distance-field one, and the host fills the
        // same block for both.
        foreach (ShaderVariantEntry variant in ShaderCatalog.Manifest.Module(module).Variants)
        {
            Assert.Equal(
                DeferredLocalLight.Maximum.ToString(CultureInfo.InvariantCulture),
                variant.Defines["OPENIGI_MAX_LAMPS"]);
        }
    }

    /// <summary>Each fog module is compiled with the volume count the host fills its block by.</summary>
    /// <param name="module">The module.</param>
    [Theory]
    [MemberData(nameof(FogVolumeModules))]
    public void The_volume_array_is_sized_by_the_constant_the_host_fills_it_by(ShaderModuleId module) =>
        Assert.All(
            ShaderCatalog.Manifest.Module(module).Variants,
            variant => Assert.Equal(
                LocalFogVolume.MaxPerLevel.ToString(CultureInfo.InvariantCulture),
                variant.Defines["OPENIGI_MAX_FOG_VOLUMES"]));
}
