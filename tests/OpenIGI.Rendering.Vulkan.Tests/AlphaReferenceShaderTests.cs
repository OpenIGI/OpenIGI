using System.Diagnostics;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The three shaders that cut a fragment out on its alpha, and the constant they now share.
/// </summary>
/// <remarks>
/// <para>
/// The threshold used to be the literal <c>0.5</c> written into each of them. It is a specialization
/// constant now, because I.G.I. 2 asks for two other values — 64 out of 255 for cut-outs generally and
/// 32 for foliage — and there is no push-constant room left in either mesh block to send it as data.
/// </para>
/// <para>
/// What makes this worth a test is that a specialization constant does not fail loudly. Declare it in
/// two of the three shaders and the third silently keeps the default: the G-buffer and the shadow pass
/// then cut at different alphas, and the symptom is a surface shadowing itself along one edge, three
/// stages away from the mistake. So the constant is checked in every shader that tests alpha, and the
/// shaders are actually compiled.
/// </para>
/// </remarks>
public sealed class AlphaReferenceShaderTests
{
    /// <summary>Every shader that cuts out declares the constant, at the same id and the same default.</summary>
    /// <remarks>
    /// Runs with no compiler installed, which is the point: it is the half of the claim that a build
    /// machine without the Vulkan SDK still checks.
    /// </remarks>
    [Theory]
    [InlineData("mesh3d.frag")]
    [InlineData("gbuffer.frag")]
    [InlineData("shadow.frag")]
    public void Every_cut_out_shader_declares_the_same_constant(string name)
    {
        string source = SourceOf(name);

        Assert.Contains("layout(constant_id = 0) const float cAlphaReference = 0.5019608;", source, StringComparison.Ordinal);

        // And tests against it rather than against a literal. A shader that declared the constant and
        // went on comparing with 0.5 would pass every other check here.
        Assert.Contains("cAlphaReference", source.Replace(
            "layout(constant_id = 0) const float cAlphaReference = 0.5019608;",
            string.Empty,
            StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>The default is the half the port always tested at, to within a texel value.</summary>
    /// <remarks>
    /// 128 out of 255 is 0.50196. Making the threshold settable had to change no picture until something
    /// asked it to, and this is the arithmetic that says it did not.
    /// </remarks>
    [Fact]
    public void The_default_is_the_half_the_port_always_used()
    {
        Assert.Equal(128, IGraphicsDevice.DefaultAlphaReference);
        Assert.Equal(0.5019608f, IGraphicsDevice.DefaultAlphaReference / 255f, 6);
        Assert.True(Math.Abs((IGraphicsDevice.DefaultAlphaReference / 255f) - 0.5f) < 1f / 255f);
    }

    /// <summary>I.G.I. 2's own two references land where the binary puts them.</summary>
    /// <remarks>
    /// <c>Direct3DRender_SetBlendStateAlphaTest</c> (<c>igi2ed 0x56C960</c>) sets <c>ALPHAREF</c> to 64
    /// with <c>ALPHAFUNC = D3DCMP_GREATEREQUAL</c>; <c>ForestRender_SetStates_Foliage</c>
    /// (<c>0x57BDF0</c>) lowers it to 32 so a leaf sprite's thin edges survive. Both are well under the
    /// half this port tested at, which is why the constant had to become settable at all.
    /// </remarks>
    [Fact]
    public void The_games_own_references_are_below_the_ports_default()
    {
        const byte CutOut = 64;
        const byte Foliage = 32;

        Assert.True(Foliage < CutOut);
        Assert.True(CutOut < IGraphicsDevice.DefaultAlphaReference);

        Assert.Equal(0.2509804f, CutOut / 255f, 6);
        Assert.Equal(0.1254902f, Foliage / 255f, 6);
    }

    /// <summary>Each of the three compiles, with the constant left at its default and with one supplied.</summary>
    /// <remarks>
    /// Compiled both ways because a specialization constant is only half-checked by compiling once: the
    /// default path exercises the declaration, and <c>-fauto-map-locations</c>-free compilation with the
    /// value replaced exercises that the constant is usable where it is used. A shader that used it in a
    /// place a constant may not appear — an array size, say — compiles with the default and fails when
    /// specialized.
    /// </remarks>
    [RequiresGlslcTheory]
    [InlineData("mesh3d.frag")]
    [InlineData("gbuffer.frag")]
    [InlineData("shadow.frag")]
    public void Every_cut_out_shader_compiles(string name)
    {
        string source = SourceOf(name);
        Compile(name, source);

        // The same shader with the constant pinned to I.G.I. 2's foliage value, which is what a
        // specialized pipeline is.
        Compile(
            "specialized-" + name,
            source.Replace(
                "layout(constant_id = 0) const float cAlphaReference = 0.5019608;",
                "layout(constant_id = 0) const float cAlphaReference = 0.1254902;",
                StringComparison.Ordinal));
    }

    private static string SourceOf(string name) => name switch
    {
        "mesh3d.frag" => Mesh3DShaderSource.Fragment,
        "gbuffer.frag" => DeferredShaderSource.GBufferFragment,
        "shadow.frag" => DeferredShaderSource.ShadowFragment,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a cut-out shader."),
    };

    private static void Compile(string name, string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "openigi-alpha-reference-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string output = path + ".spv";
        File.WriteAllText(path, source);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(Glslc.Path)
            {
                ArgumentList = { "-fshader-stage=frag", "--target-env=vulkan1.2", "-o", output, path },
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
