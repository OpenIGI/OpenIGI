using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>The DXC a build compiles DXIL with: where its compiler library is, and which release it is.</summary>
/// <param name="LibraryDirectory">The directory holding <c>dxcompiler</c>, which slangc is pointed at.</param>
/// <param name="Version">The version the compiler reports, such as <c>1.9.2609.5</c>.</param>
public sealed record DxcInstall(string LibraryDirectory, string Version);

/// <summary>Finds the pinned DXC release the DXIL forms are compiled with.</summary>
/// <remarks>
/// <para>
/// Pinned, and looked for explicitly, because DXIL is committed and checked byte for byte: the Vulkan SDK
/// carries a DXC of its own, slangc would pick that up from beside itself, and a different build of the
/// same minor version emits different bytes. So the release is named in the manifest
/// (<c>dxcVersion</c>), fetched once by <c>tools/fetch-dxc.ps1</c> into <c>third-party/dxc</c> (or named
/// outright by <see cref="OverrideVariable"/>), and refused if it reports a different version.
/// </para>
/// <para>
/// A release's layout is the one Microsoft publishes: on Windows <c>bin/x64/dxc.exe</c> beside
/// <c>dxcompiler.dll</c>; on Linux <c>bin/dxc</c> and <c>lib/libdxcompiler.so</c>.
/// </para>
/// </remarks>
public static partial class DxcLocator
{
    /// <summary>The environment variable naming the root of an extracted DXC release.</summary>
    public const string OverrideVariable = "OPENIGI_DXC";

    /// <summary>Finds the pinned DXC.</summary>
    /// <param name="repositoryRoot">The repository root, under which <c>third-party/dxc</c> is looked for.</param>
    /// <param name="pinned">The version the manifest pins, which the found compiler's must start with.</param>
    /// <returns>The installation.</returns>
    /// <exception cref="ShaderBuildException">None was found, or it is not the pinned release.</exception>
    public static DxcInstall Locate(string repositoryRoot, string pinned)
    {
        ArgumentException.ThrowIfNullOrEmpty(pinned);

        List<string> roots = [];
        string? overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            roots.Add(overridden);
        }

        roots.Add(Path.Combine(repositoryRoot, "third-party", "dxc"));

        foreach (string root in roots)
        {
            (string executable, string library) = Layout(root);
            if (!File.Exists(executable))
            {
                continue;
            }

            string version = ReadVersion(executable);
            if (!version.StartsWith(pinned, StringComparison.Ordinal))
            {
                throw new ShaderBuildException(
                    $"The DXC at {root} is {version}, but the manifest pins {pinned}. The committed DXIL is "
                    + "checked byte for byte, so run tools/fetch-dxc.ps1 for the pinned release.");
            }

            return new DxcInstall(library, version);
        }

        throw new ShaderBuildException(
            $"Could not find DXC {pinned}, which the DXIL forms are compiled with. Run tools/fetch-dxc.ps1, "
            + $"or extract the release and set {OverrideVariable} to it.{Environment.NewLine}"
            + $"Looked in: {string.Join(", ", roots)}");
    }

    private static (string Executable, string Library) Layout(string root) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Path.Combine(root, "bin", "x64", "dxc.exe"), Path.Combine(root, "bin", "x64"))
            : (Path.Combine(root, "bin", "dxc"), Path.Combine(root, "lib"));

    private static string ReadVersion(string executable)
    {
        ProcessStartInfo start = new()
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--version");

        using Process process = Process.Start(start)
            ?? throw new ShaderBuildException($"Could not start {executable}.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        // "dxcompiler.dll: 1.9(5512-01b62ad4)(1.9.2609.5) - 1.9.2609.5 (01b62ad47)": the last full version.
        Match match = FullVersion().Match(output);
        return match.Success
            ? match.Groups["version"].Value
            : throw new ShaderBuildException($"{executable} --version said nothing a version could be read from: {output.Trim()}");
    }

    [GeneratedRegex(@"- (?<version>\d+\.\d+\.\d+(\.\d+)?)")]
    private static partial Regex FullVersion();
}
