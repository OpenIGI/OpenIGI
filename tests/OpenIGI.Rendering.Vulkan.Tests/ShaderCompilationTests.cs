using System.Diagnostics;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Compiles the shader sources that read the level's shelter map, so a syntax error in them is a
/// failing test rather than a black frame.
/// </summary>
/// <remarks>
/// These strings are assembled at runtime out of several files — the wetness description is shared by
/// the pass that draws the water and the pass that reflects in it, the fog reads the same map from a
/// third, and each splices it into its own uniform layout — so nothing at build time reads them at all.
/// Handing them to <c>glslc</c> is the only way a broken splice is caught before a level is loaded.
/// </remarks>
public sealed class ShaderCompilationTests
{
    [RequiresGlslcTheory]
    [InlineData("shading.frag", "frag")]
    [InlineData("shading-rayquery.frag", "frag")]
    [InlineData("reflect.comp", "comp")]
    [InlineData("fog.scatter.comp", "comp")]
    [InlineData("fog.integrate.comp", "comp")]
    public void TheShadersThatReadTheShelterMapCompile(string name, string stage)
    {
        string compiler = Glslc.Path;

        string source = name switch
        {
            "shading.frag" => DeferredShaderSource.LightingFragment(rayQuery: false),
            "shading-rayquery.frag" => DeferredShaderSource.LightingFragment(rayQuery: true),
            "fog.scatter.comp" => VolumetricFogShaderSource.Scatter,
            "fog.integrate.comp" => VolumetricFogShaderSource.Integrate,
            _ => ReflectionShaderSource.Reflect,
        };

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
