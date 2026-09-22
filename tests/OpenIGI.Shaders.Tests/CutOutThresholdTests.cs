using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// Every pass that draws the same geometry has to cut its holes at the same threshold.
/// </summary>
/// <remarks>
/// A shadow pass that cuts on less than the G-buffer keeps casters the frame never draws, and one
/// that cuts on more drops shadows from things you can see; either way the two disagree and the
/// ground goes patchy along the edge. The device specializes the constant per draw, so what matters
/// is that each module exposes it under the same name at the same identifier — a pass that spelled
/// it differently would silently keep its default while the others moved.
/// </remarks>
public sealed class CutOutThresholdTests
{
    /// <summary>The modules that draw world geometry and therefore share the threshold.</summary>
    public static TheoryData<ShaderModuleId> CutOutModules => [ShaderModuleId.Mesh3D, ShaderModuleId.Shadow];

    /// <summary>Each cut-out module declares the threshold at identifier zero.</summary>
    /// <param name="module">The module.</param>
    [Theory]
    [MemberData(nameof(CutOutModules))]
    public void Every_cut_out_module_declares_the_threshold_at_id_zero(ShaderModuleId module)
    {
        ShaderSpecializationConstant constant = Assert.Single(
            ShaderCatalog.GetReflection(module).SpecializationConstants,
            candidate => candidate.Name == "cAlphaReference");

        Assert.Equal(0, constant.Id);
    }

    /// <summary>And they all default to the half this port has always tested at.</summary>
    [Fact]
    public void Every_cut_out_module_defaults_to_the_same_half()
    {
        foreach (ShaderModuleId module in (ShaderModuleId[])[ShaderModuleId.Mesh3D, ShaderModuleId.Shadow])
        {
            string source = ShaderRepository.ReadSource(SourceOf(module));
            Assert.Contains("const float cAlphaReference = 0.5019608;", source, StringComparison.Ordinal);
        }
    }

    private static string SourceOf(ShaderModuleId module) =>
        ShaderCatalog.Manifest.Module(module).Source;
}
