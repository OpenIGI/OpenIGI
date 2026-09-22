using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The forward mesh shader's two title-scoped compositing terms — the lightmap gain and the
/// fixed-function sun — and the cut-out threshold it shares with the passes that draw the same
/// geometry.
/// </summary>
/// <remarks>
/// <para>
/// Both terms ride slots of a push block that is exactly the 128 bytes Vulkan guarantees, and both
/// are read only by the draws they are meant for: a world draw's exposure slot is its lightmap gain,
/// and the three spare <c>w</c> components are its sun direction. The rig keeps its exposure and its
/// key colour in the same places. What these check is the contract that keeps the two apart — the
/// gating on the marker and the terrain flag.
/// </para>
/// <para>
/// These came from <c>OpenIGI.Rendering.Vulkan.Tests</c> with the shader itself. The specialization
/// constants are now checked against the compiled module's reflection rather than against the text
/// that declares them, which is the stronger statement: a constant the shader declares but the
/// compiler drops would pass a text check and still be silently ignored at pipeline creation.
/// </para>
/// </remarks>
public sealed class Mesh3DShaderTests
{
    private static string Source => ShaderRepository.ReadSource("World/Mesh3D.slang");

    /// <summary>The sun's six colour channels arrive as constants at the ids the specialization writes.</summary>
    [Theory]
    [InlineData(6, "cSunAmbientRed")]
    [InlineData(7, "cSunAmbientGreen")]
    [InlineData(8, "cSunAmbientBlue")]
    [InlineData(9, "cSunRed")]
    [InlineData(10, "cSunGreen")]
    [InlineData(11, "cSunBlue")]
    public void The_sun_constants_survive_to_the_compiled_module(int id, string name)
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D);

        ShaderSpecializationConstant constant = Assert.Single(
            reflection.SpecializationConstants,
            candidate => candidate.Name == name);
        Assert.Equal(id, constant.Id);
    }

    /// <summary>
    /// The cut-out threshold is the constant the device specializes, declared at id zero and
    /// defaulting to the half this port has always tested at.
    /// </summary>
    [Fact]
    public void The_cut_out_threshold_is_the_constant_at_id_zero()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D);

        ShaderSpecializationConstant constant = Assert.Single(
            reflection.SpecializationConstants,
            candidate => candidate.Name == "cAlphaReference");
        Assert.Equal(0, constant.Id);
        Assert.Contains("const float cAlphaReference = 0.5019608;", Source, StringComparison.Ordinal);
    }

    /// <summary>Whether a draw reflects a cube is a constant of a run of draws, not of one.</summary>
    [Fact]
    public void Reflectivity_is_the_constant_at_id_one()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D);

        ShaderSpecializationConstant constant = Assert.Single(
            reflection.SpecializationConstants,
            candidate => candidate.Name == "cReflective");
        Assert.Equal(1, constant.Id);
    }

    /// <summary>The push block is exactly the 128 bytes Vulkan guarantees, and no more.</summary>
    [Fact]
    public void The_push_block_is_the_guaranteed_128_bytes()
    {
        ShaderPushBlock push = Assert.IsType<ShaderPushBlock>(
            ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D).PushBlock);

        Assert.Equal(128, push.Size);
    }

    /// <summary>The sun is gated on a world draw that is not terrain and has a direction.</summary>
    [Fact]
    public void The_sun_lights_world_draws_only()
    {
        Assert.Contains(
            "if (abs(push.markerExposure.x) > 0.5 && terrain < 0.5 && dot(sunModel, sunModel) > 0.25)",
            Source,
            StringComparison.Ordinal);
        Assert.Contains("output.color.rgb *= clamp(sunlight, 0.0, 1.0);", Source, StringComparison.Ordinal);
    }

    /// <summary>The lightmap is multiplied by the gain a world draw carries, and by one for the rig.</summary>
    [Fact]
    public void The_fragment_stage_applies_the_lightmap_gain()
    {
        Assert.Contains(
            "float lightmapGain = abs(marker) > 0.5 ? push.markerExposure.y : 1.0;",
            Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "float3 base = SampledDisplay(diffuse.rgb) * lightmap * lightmapGain * input.color.rgb * input.fade * modelTint;",
            Source,
            StringComparison.Ordinal);
    }

    /// <summary>The fragment stage fogs towards the frame's own air, through the factor the vertex stage sent.</summary>
    /// <remarks>
    /// The mechanism this replaced is gone rather than half-present, which is the thing a reader of
    /// the shader would otherwise trip over: <c>cFogTarget</c> is absent because the target is push
    /// data now, not because it is unimplemented.
    /// </remarks>
    [Fact]
    public void The_fragment_stage_fogs_towards_the_frames_air()
    {
        Assert.Contains(
            "return float4(lerp(push.normalKey2.xyz, base, input.fog), alpha);",
            Source,
            StringComparison.Ordinal);

        Assert.DoesNotContain("cFogTarget", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("fogDepth", Source, StringComparison.Ordinal);
    }

    /// <summary>The unlit first-person rig is not fogged; the world and translucent draws are.</summary>
    /// <remarks>
    /// Marker 2 is an unlit first-person draw. World (1) and translucent (-1) remain fogged; the
    /// marker range here excludes only that rig, while the dynamic first-person marker (0) was
    /// already excluded.
    /// </remarks>
    [Fact]
    public void The_unlit_first_person_marker_is_not_fogged()
    {
        Assert.Contains(
            "abs(push.markerExposure.x) > 0.5 && push.markerExposure.x < 1.5",
            Source,
            StringComparison.Ordinal);
        Assert.Contains("abs(marker) > 0.5 && marker < 1.5", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mesh set reserves the detail texture's slots although this shader never samples it, so that
    /// every pipeline sharing the set agrees about its shape.
    /// </summary>
    [Fact]
    public void The_mesh_set_binds_image_and_sampler_apart()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D);

        Assert.Collection(
            reflection.Bindings,
            binding => AssertBinding(binding, "uDiffuse", 0, 0, ShaderBindingKind.Texture),
            binding => AssertBinding(binding, "uDiffuseSampler", 0, 1, ShaderBindingKind.Sampler),
            binding => AssertBinding(binding, "uLightmap", 0, 2, ShaderBindingKind.Texture),
            binding => AssertBinding(binding, "uLightmapSampler", 0, 3, ShaderBindingKind.Sampler),
            binding => AssertBinding(binding, "uEnvironment", 1, 0, ShaderBindingKind.Texture),
            binding => AssertBinding(binding, "uEnvironmentSampler", 1, 1, ShaderBindingKind.Sampler));
    }

    private static void AssertBinding(
        ShaderBinding binding,
        string name,
        int group,
        int slot,
        ShaderBindingKind kind)
    {
        Assert.Equal(name, binding.Name);
        Assert.Equal(group, binding.Group);
        Assert.Equal(slot, binding.Binding);
        Assert.Equal(kind, binding.Kind);
    }
}
