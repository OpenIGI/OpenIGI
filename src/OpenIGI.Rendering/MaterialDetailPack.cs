using System.Collections.Concurrent;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// A directory of derived surface-detail maps, attached to textures as they are loaded.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. ships a diffuse texture per surface and nothing else. The maps a physically-based shading
/// model needs — the normal, the roughness, whether it is metal — are derived from those textures by
/// <see cref="MaterialMapGenerator"/> and written out as PNGs named after them. This finds them and
/// registers each one against the texture it belongs to.
/// </para>
/// <para>
/// <b>Absent is a supported state, not an error.</b> An installation that has never run the tool has no
/// pack, and it renders exactly as it did before any of this existed. That is what lets the pack be an
/// optional asset rather than a shipping requirement, and it is why every failure below is a warning
/// and a null handle rather than an exception.
/// </para>
/// <para>
/// <b>The pack does not have to exist up front.</b> Given a cache directory and
/// <see cref="DeriveMissing"/>, a texture with no map derives its own on first sight and the result is
/// written out, so the second run reads a PNG where the first computed one. That is what lets the maps
/// be a per-user cache built from the player's own installation rather than an asset this repository
/// carries — the game's art with a filter over it is not ours to redistribute.
/// </para>
/// <para>
/// One instance per device. It owns the textures it uploads, so disposing it releases them; the
/// diffuse textures it attaches them to belong to whoever loaded those.
/// </para>
/// </remarks>
public sealed class MaterialDetailPack : IDisposable
{
    private const string LogCategory = "Material";

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;

    // Detail PNGs read and decoded ahead of the render thread by Prewarm. A name maps to its pixels, or
    // to null when the pack has no map for it -- which is an answer worth remembering, because a level
    // full of textures with no maps would otherwise be a level's worth of File.Exists on the frame.
    private readonly ConcurrentDictionary<string, ReadMap?> _read = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _root;
    private readonly string? _cacheRoot;
    private readonly Dictionary<string, TextureHandle> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    // Names being derived right now, each holding the diffuse textures that asked for one while it was
    // in flight. Every entry is removed by the pump, whether the derivation produced a map or failed.
    private readonly Dictionary<string, List<TextureHandle>> _awaiting = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<DerivedMap> _derived = new();
    private readonly List<Task> _running = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MaterialDetailPack"/> class.</summary>
    /// <param name="device">The device the maps are uploaded to.</param>
    /// <param name="directory">Where the maps are, or empty or missing for no pack at all.</param>
    /// <param name="logger">Where to report what was found and what would not load.</param>
    /// <param name="cacheDirectory">
    /// Where maps derived on demand are written, or empty for none. Read from as well as written to, and
    /// created on first write, so a fresh installation with no pack at all still fills it in.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public MaterialDetailPack(
        IGraphicsDevice device,
        string? directory,
        IIgiLogger? logger = null,
        string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger ?? NullLogger.Instance;

        if (!string.IsNullOrWhiteSpace(cacheDirectory))
        {
            _cacheRoot = Path.GetFullPath(cacheDirectory);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string resolved = Path.GetFullPath(directory);

        if (!Directory.Exists(resolved))
        {
            _logger.Warning(LogCategory, $"No detail maps at '{resolved}'; surfaces keep their flat normals.");
            return;
        }

        _root = resolved;
        _logger.Information(LogCategory, $"Detail maps from '{resolved}'.");
    }

    /// <summary>Gets a value indicating whether an authored pack was found to read from.</summary>
    /// <remarks>
    /// Specifically the directory <c>r_material_pack</c> names. A pack is no longer the only source —
    /// with a cache directory and <see cref="DeriveMissing"/> the maps arrive without one — so this
    /// answers where they come from rather than whether there will be any.
    /// </remarks>
    public bool IsAvailable => _root is not null;

    /// <summary>
    /// Gets or sets a value indicating whether a texture with no map derives one and caches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and pointless without a cache directory. The caller owns this because the answer
    /// is a rendering policy rather than a property of the pack: <c>r_material_detail</c> is zero under
    /// the vanilla and enhanced presets, and deriving a level's worth of maps that the shader is about to
    /// multiply by nothing is pure waste.
    /// </para>
    /// <para>
    /// Read on every miss rather than captured once, so raising the setting mid-session starts deriving
    /// for whatever loads next.
    /// </para>
    /// </remarks>
    public bool DeriveMissing { get; set; }

    /// <summary>Gets how many maps have been uploaded.</summary>
    public int Count => _maps.Count(entry => entry.Value.IsValid);

    /// <summary>Reads and decodes the maps for a set of textures on the thread pool.</summary>
    /// <param name="names">The texture names, as the pack records them.</param>
    /// <remarks>
    /// <para>
    /// A detail map is a PNG on disk, and finding and inflating one is file I/O and CPU with no graphics
    /// in it at all -- yet it happened inside <c>Attach</c>, on the thread that was uploading the
    /// texture it belongs to. Measured over one level's surface-cache capture: 219 ms of the render
    /// thread, most of it inside the inflater.
    /// </para>
    /// <para>
    /// So the reading is done here instead, wherever the caller has a worker to spare, and
    /// <c>Attach</c> finds the pixels already in hand and does nothing but upload. Optional in
    /// exactly the way the pack itself is: a name that was never prewarmed, or one prewarmed after the
    /// frame asked for it, is read inline as it always was.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
    public void Prewarm(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (_disposed || (_root is null && _cacheRoot is null))
        {
            return;
        }

        string[] wanted = [.. names];
        if (wanted.Length == 0)
        {
            return;
        }

        // Capped, and the cap matters: unbounded this took every core, and the thread it took them from
        // was the one drawing the frame. See TextureDecodeCache.MaxDecodeThreads, which is the same
        // bargain -- the reads finish well inside the swap either way.
        ParallelOptions options = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) };

        _ = Task.Run(() => Parallel.ForEach(wanted, options, name =>
        {
            if (!_disposed && !string.IsNullOrEmpty(name) && !_read.ContainsKey(name))
            {
                _read[name] = ReadMap.From(FindMapPath(name));
            }
        }));
    }

    /// <summary>Attaches this texture's detail map, if the pack has one.</summary>
    /// <param name="name">The texture's name, as the pack records it and as the file is named.</param>
    /// <param name="diffuse">The loaded diffuse texture to attach it to.</param>
    /// <remarks>
    /// Safe to call for every texture as it loads. A texture with no map in the pack is remembered as
    /// having none, so a level full of them costs one failed lookup each rather than one per frame.
    /// </remarks>
    public void Attach(string name, TextureHandle diffuse) => Attach(name, diffuse, default, 0, 0);

    /// <summary>Attaches this texture's detail map, deriving and caching one if there is none.</summary>
    /// <param name="name">The texture's name, as the pack records it and as the file is named.</param>
    /// <param name="diffuse">The loaded diffuse texture to attach it to.</param>
    /// <param name="pixels">That texture's top-level pixels, for deriving a map from on a miss.</param>
    /// <param name="width">The texture's width in pixels.</param>
    /// <param name="height">The texture's height in pixels.</param>
    /// <remarks>
    /// <para>
    /// The lookup runs pack, then cache, then derivation. A miss with <see cref="DeriveMissing"/> set
    /// hands the pixels to a worker and returns immediately: the derivation measured at tens of
    /// milliseconds for a single texture, and a level loads a thousand of them, so doing this inline
    /// would put most of a minute on the first load of every level. The texture keeps its flat normals
    /// until <see cref="PumpDerived"/> installs the result, which is the same state an installation with
    /// no pack renders in permanently — a supported one, and invisible at the strengths these maps are
    /// applied at.
    /// </para>
    /// <para>
    /// Several textures may ask for the same name before any answer arrives, which is ordinary across two
    /// renderers loading the same wall; they queue against the one derivation rather than starting their
    /// own.
    /// </para>
    /// </remarks>
    public void Attach(string name, TextureHandle diffuse, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        if (_disposed || !diffuse.IsValid || string.IsNullOrEmpty(name))
        {
            return;
        }

        if (_maps.TryGetValue(name, out TextureHandle map))
        {
            if (map.IsValid)
            {
                _device.SetTextureDetail(diffuse, map);
            }

            return;
        }

        // Already being derived. Wait behind it rather than deriving the same texture twice.
        if (_awaiting.TryGetValue(name, out List<TextureHandle>? queued))
        {
            queued.Add(diffuse);
            return;
        }

        if (_root is not null || _cacheRoot is not null)
        {
            map = Load(name);

            if (map.IsValid)
            {
                _maps[name] = map;
                _device.SetTextureDetail(diffuse, map);
                return;
            }
        }

        if (!DeriveMissing
            || _cacheRoot is null
            || width <= 0
            || height <= 0
            || pixels.Length < width * height
            || !IsCacheableName(name))
        {
            // Remembered as having none, so a level full of them costs one failed lookup each.
            _maps[name] = TextureHandle.None;
            return;
        }

        _awaiting[name] = [diffuse];

        // Copied because the caller's span is a view onto a decode buffer it is about to reuse.
        Bgra32[] source = pixels[..(width * height)].ToArray();
        _running.Add(Task.Run(() => DeriveToCache(name, source, width, height)));
    }

    /// <summary>Derives a detail map from pixels in hand and attaches it.</summary>
    /// <param name="diffuse">The loaded diffuse texture to attach it to.</param>
    /// <param name="pixels">That texture's pixels, row-major.</param>
    /// <param name="width">The texture's width in pixels.</param>
    /// <param name="height">The texture's height in pixels.</param>
    /// <remarks>
    /// <para>
    /// For the textures the pack cannot be asked about, because they have no name to ask with. The
    /// terrain's ground tiles are the case that matters: they arrive as an indexed atlas rather than as
    /// named resources, and they are also the single largest surface on screen, so leaving them flat
    /// while every wall gained detail would be the most visible half of the job left undone.
    /// </para>
    /// <para>
    /// Runs the derivation in process rather than reading a file. That is affordable here precisely
    /// because it is the exception — a level has a handful of ground tiles and a thousand other
    /// textures, and the thousand come from the pack.
    /// </para>
    /// <para>
    /// <see cref="DeriveMissing"/> is still the policy gate. Ground tiles have no authored name to look
    /// up, but deriving maps for them while material detail is disabled is wasted CPU and can turn a
    /// mission load into a multi-minute stall on some systems.
    /// </para>
    /// </remarks>
    public void AttachDerived(TextureHandle diffuse, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        if (!DeriveMissing
            || !diffuse.IsValid
            || width <= 0
            || height <= 0
            || pixels.Length < width * height)
        {
            return;
        }

        AttachPacked(diffuse, Derive(pixels, width, height), width, height);
    }

    /// <summary>Derives a packed detail map from a texture's pixels, without a device.</summary>
    /// <param name="pixels">The diffuse pixels of the level the map is derived from.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The packed detail pixels, ready for <see cref="AttachPacked"/>.</returns>
    /// <remarks>
    /// The expensive half of <see cref="AttachDerived"/> and the half that touches no graphics state, split
    /// out so it can run on a worker. Deriving a ground tile's maps measured at tens of milliseconds each,
    /// which on a level's worth of tiles is most of a second — far too much to spend on the thread that is
    /// holding a menu up, and none of it work that has to be there.
    /// </remarks>
    public static Bgra32[] Derive(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, width, height);
        Bgra32[] packed = new Bgra32[width * height];
        MaterialMapGenerator.PackDetail(maps, packed);
        return packed;
    }

    /// <summary>Attaches an already-derived detail map to a texture, uploading it as it is.</summary>
    /// <param name="diffuse">The texture the map belongs to.</param>
    /// <param name="packed">The packed detail pixels, from <see cref="Derive"/>.</param>
    /// <param name="width">The map width in pixels.</param>
    /// <param name="height">The map height in pixels.</param>
    public void AttachPacked(TextureHandle diffuse, ReadOnlySpan<Bgra32> packed, int width, int height)
    {
        if (!diffuse.IsValid || width <= 0 || height <= 0 || packed.Length < width * height)
        {
            return;
        }

        try
        {
            (Bgra32[] chain, int levels) = LinearMipChain.Build(packed, width, height);
            TextureHandle map = _device.CreateTexture(
                new TextureDescription(width, height, levels, TextureFilter.Linear, TextureAddressMode.Repeat),
                chain);

            // Keyed by the diffuse it was made for, so Dispose releases it. The name is synthetic
            // because there is none -- that is the whole reason this overload exists.
            _maps[$"#{diffuse.Id}"] = map;
            _device.SetTextureDetail(diffuse, map);
        }
        catch (Exception error) when (error is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Could not derive a detail map for {diffuse}: {error.Message}");
        }
    }

    /// <summary>Uploads whatever the workers have finished deriving, and attaches it.</summary>
    /// <param name="maxUploads">The most maps to upload on this call.</param>
    /// <returns>How many were uploaded.</returns>
    /// <remarks>
    /// Call once a frame. Bounded because an upload is a texture creation and a mip chain, and a level
    /// that just derived several hundred maps would otherwise hand the driver all of them on one frame —
    /// trading the load stall this design exists to avoid for a single enormous hitch instead.
    /// </remarks>
    public int PumpDerived(int maxUploads = 8)
    {
        if (_disposed)
        {
            return 0;
        }

        int uploaded = 0;

        while (uploaded < maxUploads && _derived.TryDequeue(out DerivedMap? item) && item is not null)
        {
            _awaiting.Remove(item.Name, out List<TextureHandle>? waiting);

            if (item.Error is not null)
            {
                _logger.Warning(LogCategory, $"Could not derive a detail map for '{item.Name}': {item.Error}");
                _maps[item.Name] = TextureHandle.None;
                continue;
            }

            TextureHandle map = TextureHandle.None;

            try
            {
                (Bgra32[] chain, int levels) = LinearMipChain.Build(item.Pixels, item.Width, item.Height);
                map = _device.CreateTexture(
                    new TextureDescription(
                        item.Width, item.Height, levels, TextureFilter.Linear, TextureAddressMode.Repeat),
                    chain);
            }
            catch (Exception error) when (error is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Derived detail map for '{item.Name}' did not upload: {error.Message}");
            }

            _maps[item.Name] = map;
            uploaded++;

            if (!map.IsValid || waiting is null)
            {
                continue;
            }

            foreach (TextureHandle diffuse in waiting)
            {
                try
                {
                    _device.SetTextureDetail(diffuse, map);
                }
                catch (GraphicsDeviceException)
                {
                    // The texture that asked was released while the derivation ran -- a level unloaded
                    // under it. The map itself is still good and stays in the pack for the next level.
                }
            }
        }

        _running.RemoveAll(task => task.IsCompleted);
        return uploaded;
    }

    /// <summary>Waits for every outstanding derivation, then uploads them all.</summary>
    /// <param name="timeout">How long to wait before giving up on the workers.</param>
    /// <returns>How many maps were uploaded.</returns>
    /// <remarks>
    /// For callers that want the pack settled rather than filling in over the next few frames — a
    /// pre-bake pass, or a test. Ordinary rendering should use <see cref="PumpDerived"/>.
    /// </remarks>
    public int WaitForDerivations(TimeSpan timeout)
    {
        if (_disposed)
        {
            return 0;
        }

        Task[] outstanding = [.. _running];

        if (outstanding.Length > 0)
        {
            Task.WaitAll(outstanding, timeout);
        }

        return PumpDerived(int.MaxValue);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Waited on rather than abandoned: a worker holds a half-written cache file open, and letting it
        // run past the pack that owns it is how a truncated PNG survives to poison the next run.
        Task[] outstanding = [.. _running];

        if (outstanding.Length > 0)
        {
            try
            {
                Task.WaitAll(outstanding, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Reported by the pump that will now never run. Nothing here can act on it.
            }
        }

        _running.Clear();
        _awaiting.Clear();
        _derived.Clear();

        foreach (TextureHandle map in _maps.Values)
        {
            if (map.IsValid)
            {
                _device.DestroyTexture(map);
            }
        }

        _maps.Clear();
    }

    /// <summary>Rejects a texture name that cannot be a file name in the cache directory.</summary>
    /// <remarks>
    /// A name arrives from a resource archive, and a map is written to a path built out of it. Anything
    /// carrying a separator or a wildcard is refused rather than sanitised: the texture still renders,
    /// and guessing at what a malformed name meant is how a write escapes the directory it was meant for.
    /// </remarks>
    private static bool IsCacheableName(string name) =>
        name.Length <= 128
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Equals(".", StringComparison.Ordinal)
        && !name.Equals("..", StringComparison.Ordinal);

    private void DeriveToCache(string name, Bgra32[] pixels, int width, int height)
    {
        try
        {
            Bgra32[] packed = Derive(pixels, width, height);
            WriteCache(name, packed, width, height);
            _derived.Enqueue(new DerivedMap(name, packed, width, height, null));
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            // Enqueued rather than logged, because this is a worker thread and the logger belongs to the
            // one that owns the device. The pump reports it and records the miss.
            _derived.Enqueue(new DerivedMap(name, [], width, height, error.Message));
        }
    }

    private void WriteCache(string name, Bgra32[] packed, int width, int height)
    {
        Directory.CreateDirectory(_cacheRoot!);

        string path = Path.Combine(_cacheRoot!, name + ".png");

        // Written beside the target and moved into place, so a run that dies mid-write leaves a stray
        // temporary rather than a truncated PNG that every later run would read and fail on. The
        // temporary carries the thread id because two packs may share one cache directory.
        string temporary = $"{path}.{Environment.CurrentManagedThreadId}.tmp";

        try
        {
            PngWriter.Write(temporary, packed, width, height);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // Nothing useful to do about a stray temporary; the next run overwrites it.
                }
            }
        }
    }

    private TextureHandle Load(string name)
    {
        // Decoded by Prewarm if a worker got to it, and read here if not. Taken rather than looked at:
        // the pixels are about to become a texture and the only copy that matters after that is the GPU's.
        if (!_read.TryRemove(name, out ReadMap? read))
        {
            read = ReadMap.From(FindMapPath(name));
        }

        if (read is null)
        {
            if (_missing.Add(name))
            {
                _logger.Debug(LogCategory, $"No detail map for '{name}'.");
            }

            return TextureHandle.None;
        }

        if (read.Error is not null)
        {
            _logger.Warning(LogCategory, $"Detail map for '{name}' did not load: {read.Error}");
            return TextureHandle.None;
        }

        try
        {
            // Repeat, because the map tiles exactly as the texture it belongs to does -- and mipmapped,
            // because a normal map minified without one aliases far worse than a colour does. A wrong
            // normal is a wrong lighting direction, and the eye reads that as sparkle rather than as
            // detail. The chain also restores what a single level silently costs: the sampler picks its
            // mip mode and its anisotropy from the level count, so with one level it was sampling
            // nearest-mip and no anisotropy as well.
            //
            // LinearMipChain rather than the picture one: these channels are numbers, and putting a
            // normal's x through a display decode and re-encode biases every average it takes.
            (Bgra32[] chain, int levels) = LinearMipChain.Build(read.Pixels, read.Width, read.Height);
            return _device.CreateTexture(
                new TextureDescription(read.Width, read.Height, levels, TextureFilter.Linear, TextureAddressMode.Repeat),
                chain);
        }
        catch (GraphicsDeviceException error)
        {
            _logger.Warning(LogCategory, $"Detail map for '{name}' did not load: {error.Message}");
            return TextureHandle.None;
        }
    }

    /// <summary>Finds which file holds a texture's map, preferring the authored pack over the cache.</summary>
    /// <param name="name">The texture's name.</param>
    /// <returns>The path, or <see langword="null"/> when neither has one.</returns>
    private string? FindMapPath(string name)
    {
        if (_root is null && _cacheRoot is null)
        {
            return null;
        }

        string path = Path.Combine(_root ?? _cacheRoot!, name + ".png");
        if (File.Exists(path))
        {
            return path;
        }

        if (_root is not null && _cacheRoot is not null)
        {
            // The pack proper does not have it. A map derived on an earlier run might.
            path = Path.Combine(_cacheRoot, name + ".png");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>A detail PNG decoded off the device thread, or the reason it could not be.</summary>
    /// <param name="Pixels">The decoded pixels.</param>
    /// <param name="Width">The map width.</param>
    /// <param name="Height">The map height.</param>
    /// <param name="Error">Why the decode failed, or <see langword="null"/> when it did not.</param>
    private sealed record ReadMap(Bgra32[] Pixels, int Width, int Height, string? Error)
    {
        /// <summary>Reads and decodes a map, on whichever thread asks. Touches no graphics state.</summary>
        /// <param name="path">The file, or <see langword="null"/> when the pack has none.</param>
        /// <returns>The decoded map, or <see langword="null"/> when there is no file to read.</returns>
        public static ReadMap? From(string? path)
        {
            if (path is null)
            {
                return null;
            }

            try
            {
                (Bgra32[] pixels, int width, int height) = PngReader.Read(path);
                return new ReadMap(pixels, width, height, null);
            }
            catch (Exception error)
                when (error is InvalidDataException or IOException or ArgumentException)
            {
                return new ReadMap([], 0, 0, error.Message);
            }
        }
    }

    /// <summary>A map a worker finished, waiting for the device thread to upload it.</summary>
    /// <param name="Name">The texture the map belongs to.</param>
    /// <param name="Pixels">The packed detail pixels, or empty when the derivation failed.</param>
    /// <param name="Width">The map width in pixels.</param>
    /// <param name="Height">The map height in pixels.</param>
    /// <param name="Error">Why the derivation failed, or <see langword="null"/> when it did not.</param>
    private sealed record DerivedMap(string Name, Bgra32[] Pixels, int Width, int Height, string? Error);
}
