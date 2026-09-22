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
/// stages away from the mistake. The shaders that cut out have all moved to the shader library, where
/// CutOutThresholdTests checks each of them declares the constant at the same identifier and default;
/// what is left here is the number itself and the two the games ask for.
/// </para>
/// </remarks>
public sealed class AlphaReferenceShaderTests
{
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
