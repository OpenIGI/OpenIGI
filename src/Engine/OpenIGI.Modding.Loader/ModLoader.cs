using System.Reflection;
using System.Runtime.Loader;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Modding.Loader;

/// <summary>Finds the mods a run plays with and loads their code.</summary>
/// <remarks>
/// <para>
/// The mods are the ones a <see cref="ModProfile"/> switches on, in its order, as the launcher writes it; or, with no
/// profile, every subdirectory of a folder holding a <c>mod.json</c>, in the order of the directories' names, so the order is
/// the same on every machine. A mod that names an assembly is loaded into its own <see cref="AssemblyLoadContext"/>, which
/// takes the port's own assemblies from the game rather than from the mod, so the mod's <see cref="IOpenIgiMod"/> is the
/// game's; its other dependencies are its own. The mod is the one public class of its assembly that implements
/// <see cref="IOpenIgiMod"/>, made through its public parameterless constructor.
/// </para>
/// <para>
/// A mod that cannot be read or loaded, was written for another API, reads a game that is not installed, needs a mod that
/// is not played, or conflicts with one played before it is reported and left out; the rest are played with. A run without
/// mods is the game as it ships, which is what the parity tests play.
/// </para>
/// </remarks>
public static class ModLoader
{
    private static readonly Dictionary<Guid, Assembly> LoadedBuilds = [];

    private const string LogCategory = "Mods";

    /// <summary>Loads the mods installed in a directory, on a machine with every game installed.</summary>
    /// <param name="directory">The mods' directory; a missing one holds none.</param>
    /// <param name="logger">Where what was loaded, and what was not, is reported.</param>
    /// <returns>The mods, in the order they apply.</returns>
    public static ModSet Load(string directory, IOpenIgiLogger? logger = null) => Load(directory, ModEnvironment.Any, logger);

    /// <summary>Loads the mods installed in a directory.</summary>
    /// <param name="directory">The mods' directory; a missing one holds none.</param>
    /// <param name="environment">What the machine has installed.</param>
    /// <param name="logger">Where what was loaded, and what was not, is reported.</param>
    /// <returns>The mods, in the order they apply.</returns>
    public static ModSet Load(string directory, ModEnvironment environment, IOpenIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(environment);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        if (!Directory.Exists(directory))
        {
            return ModSet.None;
        }

        List<InstalledMod> mods = [];
        foreach (string modDirectory in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(modDirectory, ModManifest.FileName)) && TryRead(modDirectory, null, log) is { } mod)
            {
                mods.Add(mod);
            }
        }

        return Resolve(mods, environment, log);
    }

    /// <summary>Loads the mods a profile switches on, for every game it names.</summary>
    /// <param name="profile">The profile, as the launcher wrote it.</param>
    /// <param name="environment">What the machine has installed.</param>
    /// <param name="logger">Where what was loaded, and what was not, is reported.</param>
    /// <returns>Each game's mods by title id, in the order they apply; a mod on two games' lists is loaded once.</returns>
    public static IReadOnlyDictionary<string, ModSet> Load(ModProfile profile, ModEnvironment environment, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<string, InstalledMod?> loaded = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModSet> sets = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string titleId, IReadOnlyList<ModProfileEntry> entries) in profile.Titles)
        {
            List<InstalledMod> mods = [];
            foreach (ModProfileEntry entry in entries.Where(static entry => entry.Enabled))
            {
                string directory = Path.GetFullPath(entry.Path);
                if (!loaded.TryGetValue(directory, out InstalledMod? mod))
                {
                    loaded[directory] = mod = TryRead(directory, entry.Id, log);
                }

                if (mod is not null)
                {
                    mods.Add(mod);
                }
            }

            sets[titleId] = Resolve(mods, environment, log);
        }

        return sets;
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

        Assembly assembly = Loaded(manifest.Id, assemblyPath);
        Type modType = ModTypeOf(assembly, assemblyName);

        try
        {
            return new InstalledMod(manifest, directory, (IOpenIgiMod)Activator.CreateInstance(modType)!);
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException)
        {
            throw new ModLoadException($"{modType.FullName} could not be made: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    // The mod's entry: the one public class of its assembly that is an IOpenIgiMod and can be made.
    private static Type ModTypeOf(Assembly assembly, string assemblyName)
    {
        Type[] mods =
        [
            .. assembly.GetExportedTypes().Where(static type =>
                type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(IOpenIgiMod).IsAssignableFrom(type)),
        ];
        return mods switch
        {
            [] => throw new ModLoadException($"'{assemblyName}' has no public class that implements {nameof(IOpenIgiMod)}"),
            [Type one] when one.GetConstructor(Type.EmptyTypes) is null =>
                throw new ModLoadException($"{one.FullName} has no public parameterless constructor to be made with"),
            [Type one] => one,
            _ => throw new ModLoadException(
                $"'{assemblyName}' has more than one {nameof(IOpenIgiMod)}: {string.Join(", ", mods.Select(static t => t.FullName))}"),
        };
    }

    // One mod's manifest and code, or nothing when it cannot be played; expectedId is the id a profile listed it under.
    private static InstalledMod? TryRead(string directory, string? expectedId, IOpenIgiLogger log)
    {
        try
        {
            ModManifest manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(directory, ModManifest.FileName)));
            if (expectedId is not null && !string.Equals(manifest.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModLoadException($"its manifest names it '{manifest.Id}', not '{expectedId}'");
            }

            // Before its code loads: code built against another API may not load at all.
            if (manifest.ApiVersion != ModApi.Version)
            {
                throw new ModLoadException($"it was written for modding API {manifest.ApiVersion}; this build offers {ModApi.Version}");
            }

            return LoadOne(directory, manifest);
        }
        catch (Exception ex) when (ex is ModLoadException or IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            log.Warning(LogCategory, $"The mod in '{directory}' is left out: {ex.Message}");
            return null;
        }
    }

    // The mods that can be played together, in order: each needs the games it reads installed, no conflict with a mod
    // before it, and its dependencies played with it. Leaving one out can leave out a mod that needed it, so dependencies
    // are checked until nothing more goes.
    private static ModSet Resolve(List<InstalledMod> mods, ModEnvironment environment, IOpenIgiLogger log)
    {
        List<InstalledMod> played = [];
        foreach (InstalledMod mod in mods)
        {
            ModManifest manifest = mod.Manifest;
            string? reason =
                played.Any(other => string.Equals(other.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
                    ? $"another mod is named '{manifest.Id}' already"
                : manifest.RequiresTitleData.FirstOrDefault(id => !environment.HasTitleData(id)) is { } missing
                    ? $"it reads the data of '{missing}', which is not installed"
                : played.FirstOrDefault(other => Conflict(manifest, other.Manifest)) is { } conflict
                    ? $"it cannot be played with {conflict.Manifest.Name} ({conflict.Manifest.Id})"
                : null;
            if (reason is null)
            {
                played.Add(mod);
            }
            else
            {
                log.Warning(LogCategory, $"{manifest.Name} ({manifest.Id}) is left out: {reason}.");
            }
        }

        for (bool removed = true; removed;)
        {
            removed = false;
            foreach (InstalledMod mod in played.ToArray())
            {
                if (UnmetDependency(mod.Manifest, played) is { } unmet)
                {
                    log.Warning(LogCategory, $"{mod.Manifest.Name} ({mod.Manifest.Id}) is left out: {unmet}.");
                    played.Remove(mod);
                    removed = true;
                }
            }
        }

        foreach (InstalledMod mod in played)
        {
            log.Information(LogCategory, $"Loaded {mod.Manifest.Name} {mod.Manifest.Version} ({mod.Manifest.Id}).");
        }

        return new ModSet(played);
    }

    private static string? UnmetDependency(ModManifest manifest, List<InstalledMod> played)
    {
        foreach ((string id, string requirement) in manifest.Dependencies)
        {
            InstalledMod? needed = played.FirstOrDefault(other => string.Equals(other.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (needed is null)
            {
                return $"it needs '{id}', which is not played";
            }

            if (!ModVersion.Satisfies(needed.Manifest.Version, requirement))
            {
                return $"it needs '{id}' {requirement}, which is {needed.Manifest.Version}";
            }
        }

        return null;
    }

    // A mod's assembly, loaded once a process: the same build met again -- by its module version id, wherever it is
    // installed -- is the one already loaded, so what its module initializer registers (its settings) is registered once.
    private static Assembly Loaded(string id, string assemblyPath)
    {
        Guid build;
        using (FileStream file = File.OpenRead(assemblyPath))
        using (System.Reflection.PortableExecutable.PEReader pe = new(file))
        {
            System.Reflection.Metadata.MetadataReader metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            build = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        }

        lock (LoadedBuilds)
        {
            if (!LoadedBuilds.TryGetValue(build, out Assembly? assembly))
            {
                assembly = new ModLoadContext(id, assemblyPath).LoadFromAssemblyPath(assemblyPath);
                LoadedBuilds[build] = assembly;
            }

            return assembly;
        }
    }

    private static bool Conflict(ModManifest one, ModManifest other) =>
        one.Conflicts.Contains(other.Id, StringComparer.OrdinalIgnoreCase)
        || other.Conflicts.Contains(one.Id, StringComparer.OrdinalIgnoreCase);

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

            // So is any library the game ships: the mod's types then meet the game's, a vector or a logger, as the same
            // types. Only what the game lacks is the mod's own.
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
            }

            return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
