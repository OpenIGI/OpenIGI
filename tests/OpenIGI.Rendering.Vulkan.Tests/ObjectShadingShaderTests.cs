using System.Diagnostics;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The forward mesh shader's two title-scoped compositing terms: the lightmap gain and the
/// fixed-function sun.
/// </summary>
/// <remarks>
/// <para>
/// Both ride slots of a push block that is exactly the 128 bytes Vulkan guarantees, and both are
/// read only by the draws they are meant for: a world draw's exposure slot is its lightmap gain, and
/// the three spare <c>w</c> components are its sun direction. The rig keeps its exposure and its key
/// colour in the same places. What these check is the contract that keeps the two apart — the gating
/// on the marker and the terrain flag — and that the constants the sun's colours arrive by are
/// declared where the specialization writes them.
/// </para>
/// </remarks>
public sealed class ObjectShadingShaderTests
{
    /// <summary>The vertex stage declares the six sun constants at the ids the specialization writes, defaulting to black.</summary>
    /// <remarks>
    /// A specialization constant the shader does not declare is silently ignored, and a black default
    /// is what makes a title without the light pay nothing: black over black lights nothing, and the
    /// gate on the direction keeps even that multiply out of every draw that is not lit.
    /// </remarks>
    [Theory]
    [InlineData(6, "cSunAmbientRed")]
    [InlineData(7, "cSunAmbientGreen")]
    [InlineData(8, "cSunAmbientBlue")]
    [InlineData(9, "cSunRed")]
    [InlineData(10, "cSunGreen")]
    [InlineData(11, "cSunBlue")]
    public void The_vertex_stage_declares_the_sun_constants(int id, string name)
    {
        Assert.Contains(
            $"layout(constant_id = {id}) const float {name} = 0.0;",
            Mesh3DShaderSource.Vertex,
            StringComparison.Ordinal);
    }

    /// <summary>The sun is gated on a world draw that is not terrain and has a direction.</summary>
    [Fact]
    public void The_sun_lights_world_draws_only()
    {
        Assert.Contains(
            "if (abs(push.uMarkerExposure.x) > 0.5 && terrain < 0.5 && dot(sunModel, sunModel) > 0.25)",
            Mesh3DShaderSource.Vertex,
            StringComparison.Ordinal);
        Assert.Contains("vColor.rgb *= clamp(sunlight, 0.0, 1.0);", Mesh3DShaderSource.Vertex, StringComparison.Ordinal);
    }

    /// <summary>The fragment stage multiplies the lightmap by the gain a world draw carries, and by one for the rig.</summary>
    [Fact]
    public void The_fragment_stage_applies_the_lightmap_gain()
    {
        Assert.Contains(
            "float lightmapGain = abs(marker) > 0.5 ? push.uMarkerExposure.y : 1.0;",
            Mesh3DShaderSource.Fragment,
            StringComparison.Ordinal);
        Assert.Contains(
            "vec3 base = diffuse.rgb * lightmap * lightmapGain * vColor.rgb * vFade * modelTint;",
            Mesh3DShaderSource.Fragment,
            StringComparison.Ordinal);
    }

    /// <summary>The fragment stage fogs towards the frame's own air, through the factor the vertex stage sent.</summary>
    /// <remarks>
    /// <para>
    /// <b>This used to guard a second fog and no longer does.</b> Two were built independently: the
    /// sequel's air as pipeline specialization constants with a per-pass target (<c>cFogTarget</c>,
    /// <c>Fogged</c>), and Project I.G.I.'s retail fog as device state with a per-vertex factor. They
    /// met at the rebase onto develop, and the second was kept: it changes per draw without a pipeline
    /// variant, and <c>SceneFog.ColourFor</c> already knows an additive pass fogs to black.
    /// </para>
    /// <para>
    /// The ground's PER-PASS target came across with it, and by the same route. It had been built as a
    /// specialization constant, <c>cFogTarget</c>, at a time when the fog colour was one too; the colour
    /// became push data and the target did not follow, so the device took it, the pipeline key carried
    /// it, and no shader declared <c>constant_id = 12</c> to read it. It now rides in the push row
    /// beside the colour -- <c>SceneFog.ColourFor(BlendMode, FogTarget)</c> resolves the two together --
    /// which is why <c>cFogTarget</c> is still absent from the shader below, and now correctly so.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_fragment_stage_fogs_towards_the_frames_air()
    {
        Assert.Contains("layout(location = 5) in float vFog;", Mesh3DShaderSource.Fragment, StringComparison.Ordinal);
        Assert.Contains(
            "outColor = vec4(mix(push.uNormalKey2.xyz, base, vFog), alpha);",
            Mesh3DShaderSource.Fragment,
            StringComparison.Ordinal);

        // And the mechanism it replaced is gone rather than half-present, which is the thing a reader
        // of the shader would otherwise trip over. cFogTarget is absent because the target is push data
        // now, not because it is unimplemented.
        Assert.DoesNotContain("cFogTarget", Mesh3DShaderSource.Fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("vFogDepth", Mesh3DShaderSource.Fragment, StringComparison.Ordinal);
    }

    /// <summary>The vertex stage compiles with the constants at their defaults and with a sun supplied.</summary>
    [RequiresGlslcTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_vertex_stage_compiles_both_ways(bool specialized)
    {
        string source = specialized
            ? Mesh3DShaderSource.Vertex
                .Replace("const float cSunAmbientBlue = 0.0;", "const float cSunAmbientBlue = 0.18;", StringComparison.Ordinal)
                .Replace("const float cSunBlue = 0.0;", "const float cSunBlue = 0.25;", StringComparison.Ordinal)
            : Mesh3DShaderSource.Vertex;

        Compile(specialized ? "specialized-mesh3d.vert" : "mesh3d.vert", source);
    }

    private static void Compile(string name, string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "openigi-object-shading-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string output = path + ".spv";
        File.WriteAllText(path, source);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(Glslc.Path)
            {
                ArgumentList = { "-fshader-stage=vert", "--target-env=vulkan1.2", "-o", output, path },
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{name} did not compile:{Environment.NewLine}{errors}");
    }
}
