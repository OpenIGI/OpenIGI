using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>Where the Vulkan SDK's shader compiler is, looked for once.</summary>
/// <remarks>
/// Nothing in the render path is compiled at runtime any more: every shader lives in the shader library
/// and is compiled to SPIR-V by its own build, so a syntax error is a failed build rather than a black
/// frame. What is left needing glslc is the handful of tests that compile a source of their own.
/// </remarks>
internal static class Glslc
{
    /// <summary>The path to glslc, or empty when it is not installed.</summary>
    public static string Path { get; } = Find();

    /// <summary>Looks for the compiler in the Vulkan SDK, then on PATH.</summary>
    /// <returns>Its path, or an empty string.</returns>
    private static string Find()
    {
        string executable = OperatingSystem.IsWindows() ? "glslc.exe" : "glslc";

        if (Environment.GetEnvironmentVariable("VULKAN_SDK") is { Length: > 0 } sdk)
        {
            string candidate = System.IO.Path.Combine(sdk, "Bin", executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = System.IO.Path.Combine(directory, executable);
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

