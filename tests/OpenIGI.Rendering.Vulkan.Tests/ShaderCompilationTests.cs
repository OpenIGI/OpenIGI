using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Deferred;
using OpenIGI.Rendering.Vulkan.RayTracing;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Compiles the deferred shading fragment, so a syntax error in it is a failing test rather than a
/// black frame.
/// </summary>
/// <remarks>
/// It is the last shader still assembled at runtime out of several strings -- the wetness description
/// shared with the pass that reflects in the water, the atmosphere, the lamp upsample -- so nothing at
/// build time reads it. Every other shader here is compiled by the shader library's own build and needs
/// no such test; handing this one to <c>glslc</c> is the only way a broken splice is caught before a
/// level is loaded.
/// </remarks>
public sealed class ShaderCompilationTests
{
    [RequiresGlslcTheory]
    [InlineData("shading.frag", "frag")]
    [InlineData("shading-rayquery.frag", "frag")]
    public void TheShadersThatReadTheShelterMapCompile(string name, string stage)
    {
        string compiler = Glslc.Path;

        string source = name == "shading-rayquery.frag"
            ? DeferredShaderSource.LightingFragment(rayQuery: true)
            : DeferredShaderSource.LightingFragment(rayQuery: false);

        // The SPIR-V goes to a file rather than to standard output, and only standard error is
        // redirected. Reading one pipe to its end while the other fills is how a compile that produces
        // a few hundred kilobytes deadlocks instead of failing, which reads as a test that hangs.
        string directory = Path.Combine(Path.GetTempPath(), "openigi-shader-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string output = path + ".spv";
        File.WriteAllText(path, source);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(compiler)
            {
                ArgumentList = { $"-fshader-stage={stage}", "--target-env=vulkan1.2", "-o", output, path },
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{name} did not compile:{Environment.NewLine}{errors}");
    }
    internal static string FindGlslc()
    {
        string executable = OperatingSystem.IsWindows() ? "glslc.exe" : "glslc";

        if (Environment.GetEnvironmentVariable("VULKAN_SDK") is { Length: > 0 } sdk)
        {
            string candidate = Path.Combine(sdk, "Bin", executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips when the Vulkan SDK's compiler is not installed.</summary>
public sealed class RequiresGlslcTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresGlslcTheoryAttribute"/> class.</summary>
    public RequiresGlslcTheoryAttribute()
    {
        if (Glslc.Path.Length == 0)
        {
            Skip = "Install the Vulkan SDK, or put glslc on PATH, to compile the shaders in a test.";
        }
    }
}

/// <summary>Where the shader compiler is, looked for once.</summary>
internal static class Glslc
{
    /// <summary>The path to glslc, or empty when it is not installed.</summary>
    public static string Path { get; } = ShaderCompilationTests.FindGlslc();
}
