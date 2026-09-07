using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// Remembers what the driver made of a pipeline description, so the same one is never built twice.
/// </summary>
/// <remarks>
/// <para>
/// This is the other half of the work <see cref="Shaders.ShaderBinaryCache"/> already saves. That one
/// stops the same GLSL going through <c>shaderc</c> twice; this one stops the same SPIR-V going
/// through the driver's own compiler twice. The driver's half is the larger of the two on a modern
/// card, and none of it is visible as loading — it lands as a hitch on the first frame that happens
/// to need a permutation, which for this renderer means the first time a mesh is drawn with a
/// particular depth, cull and blend combination.
/// </para>
/// <para>
/// The header is checked before the blob is handed back. Vulkan says a cache built by a different
/// driver, device or vendor must not be trusted, and gives every blob a 32-byte header carrying
/// exactly that so the host can tell: sixteen bytes of sizes and identifiers, then the driver's own
/// cache UUID. A player who updates their driver, or moves the folder to another machine, gets a
/// miss rather than undefined behaviour.
/// </para>
/// <para>
/// Nothing here can fail in a way that matters. A missing, short, corrupt or foreign file is a miss;
/// an unwritable directory is a cache that never persists; and either way the backend builds its
/// pipelines exactly as it did before this existed.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanPipelineCache : IDisposable
{
    private const string LogCategory = "Vulkan";
    private const string FileName = "pipelines.bin";

    // The layout Vulkan guarantees at the head of every cache blob: four uint32 fields and then the
    // sixteen-byte UUID. Everything after it is the driver's own business.
    private const int HeaderLength = 32;
    private const int HeaderVersionOne = 1;
    private const int UuidLength = 16;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly string? _path;
    private readonly IIgiLogger _logger;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanPipelineCache"/> class.</summary>
    /// <param name="vk">The Vulkan API.</param>
    /// <param name="device">The logical device the cache belongs to.</param>
    /// <param name="properties">The physical device's properties, which the blob's header must match.</param>
    /// <param name="logger">Where to report what was loaded.</param>
    /// <param name="directory">Where to keep the blob, or <see langword="null"/> for the user directory.</param>
    public VulkanPipelineCache(
        Vk vk,
        Device device,
        in PhysicalDeviceProperties properties,
        IIgiLogger logger,
        string? directory = null)
    {
        _vk = vk;
        _device = device;
        _logger = logger;

        byte[]? initial = null;
        try
        {
            _path = Path.Combine(
                directory ?? Path.Combine(SettingsPaths.GetUserDirectory(), "cache"),
                FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            if (File.Exists(_path))
            {
                byte[] blob = File.ReadAllBytes(_path);
                initial = Matches(blob, in properties) ? blob : null;
                if (initial is null)
                {
                    _logger.Debug(
                        LogCategory,
                        "Discarding the pipeline cache: it was written by a different driver or device.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be read is a cache that misses.
            _path = null;
        }

        fixed (byte* data = initial)
        {
            PipelineCacheCreateInfo createInfo = new()
            {
                SType = StructureType.PipelineCacheCreateInfo,
                InitialDataSize = (nuint)(initial?.Length ?? 0),
                PInitialData = data,
            };

            PipelineCache cache;
            VkCheck.Ok(_vk.CreatePipelineCache(_device, &createInfo, null, &cache), "vkCreatePipelineCache");
            Handle = cache;
        }

        if (initial is not null)
        {
            _logger.Debug(LogCategory, $"Pipeline cache loaded, {initial.Length / 1024} KiB.");
        }
    }

    /// <summary>Gets the handle every pipeline creation passes.</summary>
    public PipelineCache Handle { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Save();
        _vk.DestroyPipelineCache(_device, Handle, null);
    }

    /// <summary>Writes what the driver has learned back to disk.</summary>
    /// <remarks>
    /// Through a temporary and a rename, so a run that is killed part-way through the write leaves the
    /// previous cache intact rather than a half-file that the header check would then have to reject.
    /// </remarks>
    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            nuint size = 0;
            if (_vk.GetPipelineCacheData(_device, Handle, ref size, null) != Result.Success || size == 0)
            {
                return;
            }

            byte[] blob = new byte[(int)size];
            fixed (byte* data = blob)
            {
                if (_vk.GetPipelineCacheData(_device, Handle, ref size, data) != Result.Success)
                {
                    return;
                }
            }

            string temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, blob);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(LogCategory, "The pipeline cache could not be written; it will be rebuilt.");
        }
    }

    /// <summary>Determines whether a blob was written by this driver on this device.</summary>
    /// <param name="blob">The bytes read back from disk.</param>
    /// <param name="properties">The physical device the cache is about to be fed to.</param>
    /// <returns><see langword="true"/> when the blob is safe to seed the cache with.</returns>
    /// <remarks>
    /// Internal so it can be tested directly. It is the only part of this class that can fail
    /// silently: a comparison that is wrong in the rejecting direction turns the whole cache into a
    /// permanent miss, which costs performance and reports nothing.
    /// </remarks>
    internal static bool Matches(ReadOnlySpan<byte> blob, in PhysicalDeviceProperties properties)
    {
        if (blob.Length < HeaderLength)
        {
            return false;
        }

        uint headerLength = BitConverter.ToUInt32(blob[..4]);
        uint headerVersion = BitConverter.ToUInt32(blob[4..8]);
        uint vendorId = BitConverter.ToUInt32(blob[8..12]);
        uint deviceId = BitConverter.ToUInt32(blob[12..16]);

        if (headerLength != HeaderLength
            || headerVersion != HeaderVersionOne
            || vendorId != properties.VendorID
            || deviceId != properties.DeviceID)
        {
            return false;
        }

        // The driver's own identity for its compiled output. It moves with a driver update, which is
        // exactly when a cache written by the old one stops being safe to feed to the new one.
        fixed (byte* uuid = properties.PipelineCacheUuid)
        {
            return blob.Slice(16, UuidLength).SequenceEqual(new ReadOnlySpan<byte>(uuid, UuidLength));
        }
    }
}
