using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Legacy;

namespace OpenIGI.Engine.Resources;

/// <summary>
/// The central resource lookup: an in-memory registry of named resources, backed by the virtual file
/// system for anything not yet loaded.
/// </summary>
/// <remarks>
/// <para>
/// This models <c>Res_Load</c> (<c>0x4B5F00</c>) and <c>Res_Register</c> (<c>0x4B5F80</c>). Every load
/// checks the in-memory registry first and only then goes to disk, which is what makes mounting an
/// archive change the meaning of every name inside it — the mechanism the whole mission load sequence
/// is built on, and the seam mod overrides hook into.
/// </para>
/// <para>
/// Registering a buffer inspects it: an <c>ILFF</c>+<c>IRES</c> archive and a <c>"RESOURCE"</c> flat
/// archive are exploded into their named entries, and anything else is kept as one opaque blob. Entries
/// are slices of the archive buffer, so exploding costs no extra memory.
/// </para>
/// </remarks>
public sealed class ResourceRegistry
{
    private const string LogCategory = "Resources";

    private readonly Dictionary<string, ResourceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly VirtualFileSystem _fileSystem;
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ResourceRegistry"/> class.</summary>
    /// <param name="fileSystem">The virtual file system used when a name is not registered.</param>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public ResourceRegistry(VirtualFileSystem fileSystem, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of registered resources, exploded archive entries included.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets every registered resource.</summary>
    public IReadOnlyCollection<ResourceEntry> Entries => _entries.Values;

    /// <summary>Registers a buffer under a name, exploding it when it is an archive.</summary>
    /// <param name="name">The resource name; backslashes are normalized.</param>
    /// <param name="data">The buffer, which the registry keeps a reference to rather than copying.</param>
    /// <param name="replaceExisting">Whether duplicate names from this registration replace existing entries.</param>
    /// <returns>The entry created for <paramref name="name"/> itself.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The buffer looks like an archive but does not parse.</exception>
    public ResourceEntry Register(string name, ReadOnlyMemory<byte> data, bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        string key = ResourcePath.Normalize(name);

        if (IlffContainer.IsIlff(data.Span))
        {
            IlffContainer container = ReadContainer(key, data);
            if (container.FormType == IlffTags.Ires)
            {
                return RegisterArchive(key, data, container, replaceExisting);
            }

            // A model or a font is a container too, but it is a single resource: nothing inside it is
            // separately addressable by name.
            return Add(key, ResourceKind.RawFile, data, parent: null, replaceExisting);
        }

        if (FlatResourceArchive.IsFlatArchive(data.Span))
        {
            return RegisterFlatArchive(key, data, replaceExisting);
        }

        return Add(key, ResourceKind.RawFile, data, parent: null, replaceExisting);
    }

    /// <summary>Looks up a registered resource.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="entry">Receives the entry when one is registered.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetEntry(string name, [NotNullWhen(true)] out ResourceEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _entries.TryGetValue(ResourcePath.Normalize(name), out entry);
    }

    /// <summary>Loads a resource, from the registry when it is present and from disk otherwise.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="data">Receives the resource bytes.</param>
    /// <returns><see langword="true"/> when the resource was found.</returns>
    /// <remarks>A resource read from disk is registered, so the second load of a name never hits the disk.</remarks>
    public bool TryLoad(string name, out ReadOnlyMemory<byte> data)
    {
        // Keep explicit in-memory registrations authoritative. Disk-backed alias loads are registered
        // under their resolved device path below, so rebinding MISSION: cannot return the previous
        // mission's file from this lookup.
        if (TryGetEntry(name, out ResourceEntry? entry))
        {
            data = entry.Data;
            return true;
        }

        foreach (string candidate in _fileSystem.ResolveCandidates(name))
        {
            if (!candidate.Equals(name, StringComparison.OrdinalIgnoreCase)
                && TryGetEntry(candidate, out entry))
            {
                data = entry.Data;
                return true;
            }

            if (!_fileSystem.TryOpenRead(candidate, out Stream? stream))
            {
                continue;
            }

            byte[] bytes;
            using (stream)
            {
                bytes = VirtualFileSystem.ReadToEnd(stream);
            }

            _logger.Debug(LogCategory, $"Loaded '{candidate}' from disk ({bytes.Length} bytes).");
            data = Register(candidate, bytes).Data;
            return true;
        }

        data = default;
        return false;
    }

    /// <summary>Loads a resource, failing fatally when it does not exist.</summary>
    /// <param name="name">The resource name.</param>
    /// <returns>The resource bytes.</returns>
    /// <exception cref="EngineFatalException">The resource is neither registered nor on disk.</exception>
    /// <remarks>A missing resource is fatal in the original, and staying fatal keeps a bad install loud.</remarks>
    public ReadOnlyMemory<byte> Load(string name)
    {
        if (!TryLoad(name, out ReadOnlyMemory<byte> data))
        {
            throw new EngineFatalException($"Couldn't load resource: {name}");
        }

        return data;
    }

    /// <summary>Removes a resource, along with everything exploded from it.</summary>
    /// <param name="name">The resource name.</param>
    /// <returns><see langword="true"/> when the name was registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool Free(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string key = ResourcePath.Normalize(name);

        if (!_entries.Remove(key))
        {
            return false;
        }

        List<string> children = [];
        foreach (ResourceEntry entry in _entries.Values)
        {
            if (key.Equals(entry.Parent, StringComparison.OrdinalIgnoreCase))
            {
                children.Add(entry.Name);
            }
        }

        foreach (string child in children)
        {
            _entries.Remove(child);
        }

        _logger.Debug(LogCategory, $"Freed '{key}' and {children.Count} exploded entries.");
        return true;
    }

    /// <summary>Removes every registered resource.</summary>
    public void Clear() => _entries.Clear();

    private ResourceEntry RegisterArchive(
        string key,
        ReadOnlyMemory<byte> data,
        IlffContainer container,
        bool replaceExisting)
    {
        IresArchive archive;
        try
        {
            archive = IresArchive.FromContainer(container);
        }
        catch (IlffFormatException error)
        {
            throw new EngineFatalException($"Error in resource file: '{key}'", error);
        }

        ResourceEntry entry = Add(key, ResourceKind.Container, data, parent: null, replaceExisting);
        foreach (IresEntry item in archive.Entries)
        {
            Add(item.Name, ToResourceKind(item.Kind), item.Data, key, replaceExisting);
        }

        _logger.Debug(LogCategory, $"Registered archive '{key}' with {archive.Entries.Count} entries.");
        return entry;
    }

    private ResourceEntry RegisterFlatArchive(string key, ReadOnlyMemory<byte> data, bool replaceExisting)
    {
        FlatResourceArchive archive;
        try
        {
            archive = FlatResourceArchive.Read(data);
        }
        catch (FlatResourceFormatException error)
        {
            throw new EngineFatalException($"Error in resource file: '{key}'", error);
        }

        ResourceEntry entry = Add(key, ResourceKind.Container, data, parent: null, replaceExisting);
        foreach (FlatResourceEntry item in archive.Entries)
        {
            Add(item.Name, ResourceKind.Body, item.Data, key, replaceExisting);
        }

        _logger.Debug(LogCategory, $"Registered flat archive '{key}' with {archive.Entries.Count} entries.");
        return entry;
    }

    private static IlffContainer ReadContainer(string key, ReadOnlyMemory<byte> data)
    {
        try
        {
            return IlffContainer.Read(data);
        }
        catch (IlffFormatException error)
        {
            throw new EngineFatalException($"Error in resource file: '{key}'", error);
        }
    }

    private static ResourceKind ToResourceKind(IresEntryKind kind) => kind switch
    {
        IresEntryKind.Body => ResourceKind.Body,
        IresEntryKind.Path => ResourceKind.Path,
        _ => ResourceKind.ContextString,
    };

    private ResourceEntry Add(
        string name,
        ResourceKind kind,
        ReadOnlyMemory<byte> data,
        string? parent,
        bool replaceExisting = false)
    {
        var entry = new ResourceEntry(name, kind, data, parent);
        if (replaceExisting)
        {
            _entries[name] = entry;
            return entry;
        }

        if (!_entries.TryAdd(name, entry))
        {
            // The original's hash table refuses a duplicate rather than replacing it; a mount that
            // shadows an existing name is worth seeing, but it must not change which bytes win.
            _logger.Warning(LogCategory, $"Resource '{name}' is already registered; keeping the first registration.");
            return _entries[name];
        }

        return entry;
    }
}
