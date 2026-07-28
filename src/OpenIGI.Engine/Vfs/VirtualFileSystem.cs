using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Vfs;

/// <summary>
/// Resolves the original engine's <c>NAMESPACE:path</c> names to files, through runtime-rebindable
/// aliases over a set of named devices.
/// </summary>
/// <remarks>
/// <para>
/// Resolution has exactly two stages, mirroring <c>0x4B1020</c> and <c>0x4B1510</c>:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Alias substitution.</b> The first alias whose name is a prefix of the path wins; its target
/// replaces that prefix and the rest of the path is appended. An alias target is a
/// <em>semicolon-separated search list</em>, so <c>MISSION:</c> can name several directories, tried in
/// order. A path matching no alias passes through unchanged. Substitution happens once — the original
/// does not chain aliases, and neither do we.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Device dispatch.</b> Whatever prefix remains is a device name (<c>LOCAL</c> in the retail game).
/// The device receives the path after the colon. An unknown or unregistered device is fatal, as it is
/// in the original.
/// </description>
/// </item>
/// </list>
/// <para>
/// Aliases are bound at runtime — <c>LANGUAGE:</c> is rebound whenever the language changes and
/// <c>MISSION:</c> whenever a mission loads — so callers must resolve at use time, never cache.
/// </para>
/// </remarks>
public sealed class VirtualFileSystem
{
    /// <summary>The alias bound to the intro movie and screen directory.</summary>
    public const string IntroScreenAlias = "INTROSCREEN:";

    /// <summary>The alias bound to the in-game status screen directory.</summary>
    public const string StatusScreenAlias = "STATUSSCREEN:";

    /// <summary>The alias bound to the currently selected language directory.</summary>
    public const string LanguageAlias = "LANGUAGE:";

    /// <summary>The alias bound to the currently loaded mission directory.</summary>
    public const string MissionAlias = "MISSION:";

    /// <summary>The alias bound to the map computer directory.</summary>
    public const string ComputerAlias = "COMPUTER:";

    /// <summary>The alias bound to the menu system directory.</summary>
    public const string MenuSystemAlias = "MENUSYS:";

    private const string LogCategory = "Vfs";

    private readonly Dictionary<string, IFileDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AliasBinding> _aliases = [];
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="VirtualFileSystem"/> class.</summary>
    /// <param name="logger">The logger for mount and rebind diagnostics; defaults to a sink that discards them.</param>
    public VirtualFileSystem(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the registered device names.</summary>
    public IReadOnlyCollection<string> DeviceNames => _devices.Keys;

    /// <summary>Gets the bound alias names, in the order they are matched.</summary>
    public IReadOnlyList<string> AliasNames => _aliases.ConvertAll(alias => alias.Name);

    /// <summary>Registers a device, replacing any device of the same name.</summary>
    /// <param name="device">The device to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public void RegisterDevice(IFileDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices[device.Name] = device;
        _logger.Information(LogCategory, $"Registered device '{device.Name}:'.");
    }

    /// <summary>Binds an alias to a target, replacing any previous binding of the same name.</summary>
    /// <param name="alias">The alias including its trailing colon, for example <c>MISSION:</c>.</param>
    /// <param name="target">
    /// A semicolon-separated search list of device-prefixed targets, for example
    /// <c>LOCAL:missions/location0/level1/</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="alias"/> does not end with a colon, or <paramref name="target"/> is empty.</exception>
    /// <exception cref="EngineFatalException">A target names a device that is not registered.</exception>
    /// <remarks>
    /// The original validates the target's device at bind time and refuses the binding otherwise
    /// (<c>0x4B12F0</c>); binding an alias to a missing device is a configuration error that would
    /// otherwise only surface as a fatal error much later, during a load.
    /// </remarks>
    public void SetAlias(string alias, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        string name = ResourcePath.Normalize(alias);
        if (name[^1] != ResourcePath.NamespaceSeparator)
        {
            throw new ArgumentException(
                $"An alias name ends with '{ResourcePath.NamespaceSeparator}', for example 'MISSION:'; got '{alias}'.",
                nameof(alias));
        }

        string[] variants = ResourcePath.Normalize(target)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (variants.Length == 0)
        {
            throw new ArgumentException("An alias target names at least one path.", nameof(target));
        }

        foreach (string variant in variants)
        {
            string device = DeviceNameOf(variant);
            if (!_devices.ContainsKey(device))
            {
                throw new EngineFatalException($"Device not present: {device}");
            }
        }

        RemoveAlias(name);
        _aliases.Add(new AliasBinding(name, variants));
        _logger.Information(LogCategory, $"Bound alias '{name}' to '{string.Join(';', variants)}'.");
    }

    /// <summary>Removes an alias binding.</summary>
    /// <param name="alias">The alias including its trailing colon.</param>
    /// <returns><see langword="true"/> when a binding was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="alias"/> is <see langword="null"/>.</exception>
    public bool RemoveAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        string name = ResourcePath.Normalize(alias);
        return _aliases.RemoveAll(binding => binding.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>Expands a name into the device-prefixed candidates it may resolve to, in search order.</summary>
    /// <param name="name">The resource name, for example <c>MISSION:sounds/alarm.wav</c>.</param>
    /// <returns>One candidate per alias variant, or the normalized name itself when no alias matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> ResolveCandidates(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string path = ResourcePath.Normalize(name);

        foreach (AliasBinding alias in _aliases)
        {
            if (!path.StartsWith(alias.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string remainder = path[alias.Name.Length..];
            string[] candidates = new string[alias.Variants.Count];
            for (int i = 0; i < candidates.Length; i++)
            {
                candidates[i] = alias.Variants[i] + remainder;
            }

            return candidates;
        }

        return [path];
    }

    /// <summary>Resolves a name to the first candidate that exists.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="resolved">Receives the device-prefixed path that exists.</param>
    /// <returns><see langword="true"/> when one of the candidates exists.</returns>
    /// <exception cref="EngineFatalException">A candidate names a device that is not registered.</exception>
    public bool TryResolve(string name, [NotNullWhen(true)] out string? resolved)
    {
        foreach (string candidate in ResolveCandidates(name))
        {
            if (RequireDevice(candidate, out string devicePath).Exists(devicePath))
            {
                resolved = candidate;
                return true;
            }
        }

        resolved = null;
        return false;
    }

    /// <summary>Determines whether a name resolves to an existing file.</summary>
    /// <param name="name">The resource name.</param>
    /// <returns><see langword="true"/> when the file exists on some device.</returns>
    /// <exception cref="EngineFatalException">A candidate names a device that is not registered.</exception>
    public bool Exists(string name) => TryResolve(name, out _);

    /// <summary>Opens a name for reading, trying each alias variant in turn.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="stream">Receives the opened stream, which the caller disposes.</param>
    /// <returns><see langword="true"/> when the file was opened.</returns>
    /// <exception cref="EngineFatalException">A candidate names a device that is not registered.</exception>
    public bool TryOpenRead(string name, [NotNullWhen(true)] out Stream? stream)
    {
        foreach (string candidate in ResolveCandidates(name))
        {
            if (RequireDevice(candidate, out string devicePath).TryOpenRead(devicePath, out Stream? opened))
            {
                stream = opened!;
                return true;
            }
        }

        stream = null;
        return false;
    }

    /// <summary>Reads a file in full.</summary>
    /// <param name="name">The resource name.</param>
    /// <returns>The file contents.</returns>
    /// <exception cref="EngineFatalException">The file does not exist, or a candidate names an unregistered device.</exception>
    /// <remarks>A missing file is fatal here because the original treats a failed resource load as fatal.</remarks>
    public byte[] ReadAllBytes(string name)
    {
        if (!TryOpenRead(name, out Stream? stream))
        {
            throw new EngineFatalException($"Couldn't load file: {name}");
        }

        using (stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>Enumerates the files of a directory, across every alias variant.</summary>
    /// <param name="name">The directory name, for example <c>MISSION:sounds</c>.</param>
    /// <param name="searchPattern">A pattern such as <c>*.res</c>.</param>
    /// <returns>The matching device-prefixed paths.</returns>
    /// <exception cref="EngineFatalException">A candidate names a device that is not registered.</exception>
    public IReadOnlyList<string> EnumerateFiles(string name, string searchPattern)
    {
        List<string> results = [];
        foreach (string candidate in ResolveCandidates(name))
        {
            IFileDevice device = RequireDevice(candidate, out string devicePath);
            foreach (string file in device.EnumerateFiles(devicePath, searchPattern))
            {
                results.Add($"{device.Name}{ResourcePath.NamespaceSeparator}{file}");
            }
        }

        return results;
    }

    /// <summary>Enumerates the immediate sub-directories of a directory, across every alias variant.</summary>
    /// <param name="name">The directory name, for example <c>LOCAL:missions</c>.</param>
    /// <returns>The matching device-prefixed directory paths.</returns>
    /// <exception cref="EngineFatalException">A candidate names a device that is not registered.</exception>
    public IReadOnlyList<string> EnumerateDirectories(string name)
    {
        List<string> results = [];
        foreach (string candidate in ResolveCandidates(name))
        {
            IFileDevice device = RequireDevice(candidate, out string devicePath);
            foreach (string child in device.EnumerateDirectories(devicePath))
            {
                results.Add($"{device.Name}{ResourcePath.NamespaceSeparator}{child}");
            }
        }

        return results;
    }

    private static string DeviceNameOf(string path)
    {
        int separator = path.IndexOf(ResourcePath.NamespaceSeparator, StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new EngineFatalException($"Illegal device name in path: \"{path}\".");
        }

        return path[..separator];
    }

    private IFileDevice RequireDevice(string path, out string devicePath)
    {
        string deviceName = DeviceNameOf(path);
        if (!_devices.TryGetValue(deviceName, out IFileDevice? device))
        {
            throw new EngineFatalException($"Device not present: {deviceName}");
        }

        devicePath = path[(deviceName.Length + 1)..];
        return device;
    }

    private sealed record AliasBinding(string Name, IReadOnlyList<string> Variants);
}
