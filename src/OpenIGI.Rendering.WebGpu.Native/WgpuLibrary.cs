using System.Reflection;
using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.WebGpu.Native;

/// <summary>
/// Finds the wgpu-native binary the generated bindings call into.
/// </summary>
/// <remarks>
/// <para>
/// The bindings name one library, <c>wgpu_native</c>, and every platform spells its file differently.
/// A resolver is how that is answered once rather than in two hundred attributes, and it is also how
/// this loads from a folder beside the game rather than from wherever the system happens to have one:
/// the release the bindings were generated against is the release that has to be loaded, and a
/// different one will not fail at load — it will fail in the middle of a frame, at a structure whose
/// layout has moved.
/// </para>
/// <para>
/// The binary is not carried in this repository. It is another project's redistributable, fetched onto
/// a developer's machine, so the places looked in below are ones a fetch can reasonably put it.
/// </para>
/// </remarks>
public static class WgpuLibrary
{
    /// <summary>The environment variable that names the library file outright.</summary>
    public const string OverrideVariable = "OPENIGI_WGPU_NATIVE";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Makes the generated bindings resolvable. Safe to call more than once.</summary>
    /// <remarks>
    /// Called before the first entry point rather than by a static constructor on the bindings: the
    /// bindings are generated, and a hand-written hook inside generated code is a hook that vanishes on
    /// the next regeneration.
    /// </remarks>
    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(WgpuLibrary).Assembly, Resolve);
            _registered = true;
        }
    }

    /// <summary>The file name wgpu-native has on this platform.</summary>
    /// <returns>The name, including its extension and any prefix.</returns>
    public static string FileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "wgpu_native.dll";
        }

        return OperatingSystem.IsMacOS() ? "libwgpu_native.dylib" : "libwgpu_native.so";
    }

    /// <summary>The runtime identifier wgpu-native publishes its binaries under.</summary>
    /// <returns>The identifier, as the download's own folders name it.</returns>
    public static string RuntimeIdentifier()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture other => other.ToString().ToLowerInvariant(),
        };

        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }

        return OperatingSystem.IsMacOS() ? $"osx-{architecture}" : $"linux-{architecture}";
    }

    /// <summary>Where the library is looked for, in the order it is looked for.</summary>
    /// <param name="baseDirectory">The directory the application was loaded from.</param>
    /// <returns>The candidate paths.</returns>
    /// <remarks>
    /// Beside the application first, because that is where a published build puts it and what a player
    /// has. Then the fetched folder a developer's machine keeps, by runtime identifier and then without,
    /// so a single-platform fetch works as well as a full one.
    /// </remarks>
    public static IEnumerable<string> Candidates(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);

        string file = FileName();
        string rid = RuntimeIdentifier();

        yield return Path.Combine(baseDirectory, file);
        yield return Path.Combine(baseDirectory, "runtimes", rid, "native", file);
        yield return Path.Combine(baseDirectory, "third-party", "wgpu-native", rid, file);
        yield return Path.Combine(baseDirectory, "third-party", "wgpu-native", file);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, WebGpu.LibraryName, StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        string? overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden) && NativeLibrary.TryLoad(overridden, out nint chosen))
        {
            return chosen;
        }

        foreach (string candidate in Candidates(AppContext.BaseDirectory))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint found))
            {
                return found;
            }
        }

        // Nothing found where this knows to look. Zero hands the question back to the runtime's own
        // search, which is what makes a system-wide install work; the failure past that names the
        // library, which is the message a developer needs.
        return nint.Zero;
    }
}
