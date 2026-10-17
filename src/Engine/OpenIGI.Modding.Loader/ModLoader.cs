using System.Reflection;
using System.Runtime.Loader;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Modding.Loader;

/// <summary>Finds the mods installed in a directory and loads their code.</summary>
/// <remarks>
/// <para>
/// Every subdirectory holding a <c>mod.json</c> is a mod, applied in the order of the directories' names, so the order is
/// the same on every machine. A mod that names an assembly is loaded into its own <see cref="AssemblyLoadContext"/>, which
/// takes the port's own assemblies from the game rather than from the mod, so the mod's <see cref="IOpenIgiMod"/> is the
/// game's; its other dependencies are its own.
/// </para>
/// <para>
/// A mod that cannot be read or loaded is reported and left out; the rest are played with. A run without mods is the game
/// as it ships, which is what the parity tests play.
/// </para>
/// </remarks>
public static class ModLoader
{
    private const string LogCategory = "Mods";

    /// <summary>Loads the mods installed in a directory.</summary>
    /// <param name="directory">The mods' directory; a missing one holds none.</param>
    /// <param name="logger">Where what was loaded, and what was not, is reported.</param>
    /// <returns>The mods, in the order they apply.</returns>
    public static ModSet Load(string directory, IOpenIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        if (!Directory.Exists(directory))
        {
            return ModSet.None;
        }

        List<InstalledMod> mods = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string modDirectory in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            string manifestPath = Path.Combine(modDirectory, ModManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                InstalledMod mod = LoadOne(modDirectory, ModManifest.Parse(File.ReadAllText(manifestPath)));
                if (mod.Manifest.ApiVersion != ModApi.Version)
                {
                    throw new ModLoadException(
                        $"it was written for modding API {mod.Manifest.ApiVersion}; this build offers {ModApi.Version}");
                }

                if (!ids.Add(mod.Manifest.Id))
                {
                    throw new ModLoadException($"another mod is named '{mod.Manifest.Id}' already");
                }

                mods.Add(mod);
                log.Information(LogCategory, $"Loaded {mod.Manifest.Name} {mod.Manifest.Version} ({mod.Manifest.Id}).");
            }
            catch (Exception ex) when (ex is ModLoadException or IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                log.Warning(LogCategory, $"The mod in '{modDirectory}' is left out: {ex.Message}");
            }
        }

        return new ModSet(mods);
    }

    /// <summary>Loads one mod's code, when it has any.</summary>
    /// <param name="directory">The mod's directory.</param>
    /// <param name="manifest">Its manifest.</param>
    /// <returns>The mod.</returns>
    /// <exception cref="ModLoadException">Its assembly is missing, names no mod, or its mod cannot be made.</exception>
    public static InstalledMod LoadOne(string directory, ModManifest manifest)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Assembly is not { Length: > 0 } assemblyName)
        {
            return new InstalledMod(manifest, directory, null);
        }

        string assemblyPath = Path.GetFullPath(Path.Combine(directory, assemblyName));
        if (!File.Exists(assemblyPath))
        {
            throw new ModLoadException($"its assembly '{assemblyName}' is not there");
        }

        ModLoadContext context = new(manifest.Id, assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        if (assembly.GetCustomAttribute<OpenIgiModAttribute>() is not { } named)
        {
            throw new ModLoadException($"'{assemblyName}' names no mod; it needs [assembly: OpenIgiMod(typeof(...))]");
        }

        if (!typeof(IOpenIgiMod).IsAssignableFrom(named.ModType))
        {
            throw new ModLoadException($"{named.ModType.FullName} is not an {nameof(IOpenIgiMod)}");
        }

        try
        {
            return new InstalledMod(manifest, directory, (IOpenIgiMod)Activator.CreateInstance(named.ModType)!);
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException)
        {
            throw new ModLoadException($"{named.ModType.FullName} could not be made: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    /// <summary>A mod's own assemblies, beside the game's.</summary>
    private sealed class ModLoadContext(string id, string assemblyPath) : AssemblyLoadContext($"mod:{id}")
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // The port's own assemblies are the game's: a mod that carried its own copy would have an IOpenIgiMod the
            // game does not know.
            if (assemblyName.Name is { } name && name.StartsWith("OpenIGI.", StringComparison.Ordinal))
            {
                return null;
            }

            return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
