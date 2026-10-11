using OpenIGI.Shaders.Dxil;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The DXIL forms Direct3D 12 runs: one signed blob per stage, and a register for everything the module
/// binds, so a root signature can be built from the reflection alone.
/// </summary>
public sealed class DxilFormTests
{
    [Theory]
    [MemberData(nameof(ShaderCatalogTests.Variants), MemberType = typeof(ShaderCatalogTests))]
    public void Every_stage_of_a_dxil_variant_is_a_signed_container(ShaderModuleId module, string variant)
    {
        if (!ShaderCatalog.Compiles(module, new ShaderVariant(variant), ShaderTarget.Dxil))
        {
            return;
        }

        foreach (ShaderEntryPoint entryPoint in ShaderCatalog.GetReflection(module, new ShaderVariant(variant)).EntryPoints)
        {
            ReadOnlySpan<byte> dxil = DxilCatalog.Get(module, new ShaderVariant(variant), entryPoint.Stage).Span;

            // "DXBC", then the 16-byte hash a validator writes. A zero hash is a container nothing signed,
            // which the runtime refuses to make a pipeline from.
            Assert.Equal("DXBC"u8.ToArray(), dxil[..4].ToArray());
            Assert.Contains(dxil[4..20].ToArray(), b => b != 0);
        }
    }

    [Theory]
    [MemberData(nameof(ShaderCatalogTests.Variants), MemberType = typeof(ShaderCatalogTests))]
    public void Every_binding_of_a_dxil_variant_has_a_register(ShaderModuleId module, string variant)
    {
        if (!ShaderCatalog.Compiles(module, new ShaderVariant(variant), ShaderTarget.Dxil))
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(module, new ShaderVariant(variant));

        Assert.All(reflection.Bindings, binding => Assert.NotNull(binding.Hlsl));
        Assert.True(reflection.PushBlock is null || reflection.PushBlock.Hlsl is { Class: ShaderHlslClass.ConstantBuffer });
        Assert.All(reflection.SpecializationConstants, constant => Assert.NotNull(constant.HlslOffset));
        Assert.Equal(reflection.SpecializationConstants.Count > 0, reflection.HlslConstants is not null);

        // One register per binding: two resources of a class sharing one would be one descriptor read twice.
        Assert.Equal(
            reflection.Bindings.Count,
            reflection.Bindings.Select(binding => binding.Hlsl).Distinct().Count());
    }

    /// <summary>A block set by name says where every field sits in HLSL, and those places fit in its HLSL size.</summary>
    /// <remarks>
    /// HLSL packs a constant buffer tighter than SPIR-V after an array of scalars, so the GI modules' parameters
    /// past <c>uClipmapRange[4]</c> sit twelve bytes earlier in DXIL. Written at the SPIR-V offsets, Direct3D 12
    /// read its screen size and pass number from the wrong bytes and drew no indirect light at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShaderCatalogTests.Variants), MemberType = typeof(ShaderCatalogTests))]
    public void Every_named_parameter_of_a_dxil_variant_has_its_hlsl_place(ShaderModuleId module, string variant)
    {
        if (!ShaderCatalog.Compiles(module, new ShaderVariant(variant), ShaderTarget.Dxil)
            || ShaderCatalog.GetReflection(module, new ShaderVariant(variant)).Globals is not { } globals)
        {
            return;
        }

        Assert.NotNull(globals.HlslSize);
        Assert.All(globals.Fields, field =>
        {
            Assert.NotNull(field.HlslOffset);
            Assert.InRange(field.HlslOffset!.Value, 0, globals.HlslSize!.Value - 1);
        });

        // Two fields at one HLSL place would be two parameters written over each other.
        Assert.Equal(globals.Fields.Count, globals.Fields.Select(field => field.HlslOffset).Distinct().Count());
    }

    [Fact]
    public void A_field_past_an_array_of_scalars_sits_earlier_in_hlsl()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.CardsScreenGatherTemporal, new ShaderVariant("software"));
        ShaderUniformField after = Assert.Single(reflection.Globals!.Fields, field => field.Name == "uShadowLevels");

        Assert.Equal(after.Offset - 12, after.OffsetFor(ShaderTarget.Dxil));
        Assert.Equal(after.Offset, after.OffsetFor(ShaderTarget.Spirv));
    }
}
