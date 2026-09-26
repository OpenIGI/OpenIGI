using System.Runtime.InteropServices;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Finds the <c>slangc</c> the shader library is compiled with.</summary>
/// <remarks>
/// Looked for in the places it actually is, cheapest first: an explicit override, the Vulkan SDK
/// (which ships it, and is what this repository's contributors already have), then the path. The
/// build does not download a compiler: a silent network fetch inside a build is a worse failure than
/// a clear message saying which tool is missing and where to get it.
/// </remarks>
public static class SlangLocator
{
    /// <summary>The environment variable that names <c>slangc</c> outright.</summary>
    public const string OverrideVariable = "OPENIGI_SLANGC";

    /// <summary>Finds <c>slangc</c>.</summary>
    /// <returns>The full path to the compiler.</returns>
    /// <exception cref="ShaderBuildException">No compiler could be found.</exception>
    public static string Locate()
    {
        List<string> looked = [];

        string? overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            if (File.Exists(overridden))
            {
                return overridden;
            }

            looked.Add($"{OverrideVariable}={overridden}");
        }

        string executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "slangc.exe" : "slangc";

        string? vulkanSdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrWhiteSpace(vulkanSdk))
        {
            string candidate = Path.Combine(vulkanSdk, "Bin", executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            looked.Add(candidate);
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        looked.Add("PATH");
        throw new ShaderBuildException(
            $"Could not find {executable}. It ships with the Vulkan SDK (set VULKAN_SDK), or download a "
            + $"release from https://github.com/shader-slang/slang/releases and set {OverrideVariable} to it."
            + $"{Environment.NewLine}Looked in: {string.Join(", ", looked)}");
    }
}
