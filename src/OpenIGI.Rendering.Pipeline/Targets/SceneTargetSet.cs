using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Targets;

/// <summary>What one scene target is, whatever size the frame happens to be.</summary>
/// <param name="Id">Which target.</param>
/// <param name="Format">What one texel holds.</param>
/// <param name="Scale">How its size follows the frame's.</param>
/// <param name="Usage">What it may be used for.</param>
public readonly record struct SceneTargetSpec(
    SceneTargetId Id,
    RhiTextureFormat Format,
    SceneTargetScale Scale,
    RhiTextureUsage Usage);

/// <summary>
/// The images a frame is built out of, allocated together and thrown away together.
/// </summary>
/// <remarks>
/// <para>
/// A table rather than a field each. What a target is — its format, its size rule and what it may be
/// used for — is a fact about the renderer, so it is written down once and the allocation, the resize
/// and the release all read it. The Vulkan original spent a field, a property, an allocation and four
/// scattered lines on every one of forty images, which is how two of them ended up at subtly different
/// sizes.
/// </para>
/// <para>
/// There are no render passes or framebuffers here, which is most of what the original was. The RHI
/// states attachments when a pass begins, so a target is a texture and nothing else, and adding one
/// costs a row.
/// </para>
/// </remarks>
public sealed class SceneTargetSet : IDisposable
{
    /// <summary>What every target in a frame is.</summary>
    /// <remarks>
    /// The formats are the ones the renderer has always used: the albedo in sRGB because the textures
    /// are authored in it, the normal packed into ten bits a channel, the baked light in a float format
    /// because it is irradiance rather than a colour, and the lit scene in half floats because it is
    /// not yet tone-mapped.
    /// </remarks>
    public static IReadOnlyList<SceneTargetSpec> Layout { get; } =
    [
        new(SceneTargetId.BaseColour, RhiTextureFormat.Rgba8UnormSrgb, SceneTargetScale.Full, Attachment),
        new(SceneTargetId.Normal, RhiTextureFormat.Rgb10A2Unorm, SceneTargetScale.Full, Attachment),
        new(SceneTargetId.Material, RhiTextureFormat.Rgba8Unorm, SceneTargetScale.Full, Attachment),
        new(SceneTargetId.Baked, RhiTextureFormat.Rg11B10Ufloat, SceneTargetScale.Full, Attachment),
        new(SceneTargetId.Depth, RhiTextureFormat.Depth32Float, SceneTargetScale.Full, DepthAttachment),
        new(SceneTargetId.Lit, RhiTextureFormat.Rgba16Float, SceneTargetScale.Full, Attachment | RhiTextureUsage.Storage),
        new(SceneTargetId.Occlusion, RhiTextureFormat.R8Unorm, SceneTargetScale.Half, Compute),

        // A colour attachment as well as storage: the object-velocity pass rasterises each animated
        // mesh's own motion over the computed field.
        new(SceneTargetId.Velocity, RhiTextureFormat.Rg16Float, SceneTargetScale.Full, Compute | RhiTextureUsage.RenderTarget),
        new(SceneTargetId.DilatedVelocity, RhiTextureFormat.Rg16Float, SceneTargetScale.Full, Compute),
        new(SceneTargetId.ClosestDepth, RhiTextureFormat.R32Float, SceneTargetScale.Full, Compute),
        new(SceneTargetId.ClosestOccluder, RhiTextureFormat.R32Uint, SceneTargetScale.Full, Compute),
        new(SceneTargetId.ResolveOutput, RhiTextureFormat.Rgba16Float, SceneTargetScale.Full, Compute),
        new(SceneTargetId.ResolveHistoryColour, RhiTextureFormat.Rgba16Float, SceneTargetScale.Double, Compute),
        new(SceneTargetId.ResolveHistoryMetadata, RhiTextureFormat.R8Unorm, SceneTargetScale.Double, Compute),
    ];

    private const RhiTextureUsage Attachment =
        RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled | RhiTextureUsage.CopySource;

    private const RhiTextureUsage DepthAttachment =
        RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled;

    private const RhiTextureUsage Compute =
        RhiTextureUsage.Storage | RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination;

    private readonly IRhiDevice _device;
    private readonly Dictionary<SceneTargetId, IRhiTexture> _targets = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SceneTargetSet"/> class.</summary>
    /// <param name="device">The device the targets live on.</param>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">Its height.</param>
    public SceneTargetSet(IRhiDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        Allocate(width, height);
    }

    /// <summary>Gets the frame's width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets its height.</summary>
    public int Height { get; private set; }

    /// <summary>Gets one target.</summary>
    /// <param name="id">Which one.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture this[SceneTargetId id] => _targets[id];

    /// <summary>Gets the whole view of one target, which is what a pass attaches or binds.</summary>
    /// <param name="id">Which one.</param>
    /// <returns>The view.</returns>
    public IRhiTextureView View(SceneTargetId id) => _targets[id].View;

    /// <summary>The size one target has, for a frame of a given size.</summary>
    /// <param name="scale">How the target's size follows the frame's.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">Its height.</param>
    /// <returns>The target's size, never smaller than one texel.</returns>
    /// <remarks>
    /// Half rounds <b>up</b>, so an odd frame's half-size target still covers every pixel of it. Rounding
    /// down leaves the last row and column reading a texel that was never written, which at half
    /// resolution is a two-pixel band down two edges of the picture.
    /// </remarks>
    public static (int Width, int Height) SizeOf(SceneTargetScale scale, int width, int height) => scale switch
    {
        SceneTargetScale.Half => (Math.Max(1, (width + 1) / 2), Math.Max(1, (height + 1) / 2)),
        SceneTargetScale.Double => (Math.Max(1, width * 2), Math.Max(1, height * 2)),
        _ => (Math.Max(1, width), Math.Max(1, height)),
    };

    /// <summary>Rebuilds every target for a new frame size.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height.</param>
    /// <remarks>
    /// Everything goes and is remade rather than only what changed size, because every target's size
    /// follows the frame's: there is no such thing here as one that survives a resize.
    /// </remarks>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width == Width && height == Height)
        {
            return;
        }

        Release();
        Allocate(width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
    }

    private void Allocate(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;

        foreach (SceneTargetSpec spec in Layout)
        {
            (int targetWidth, int targetHeight) = SizeOf(spec.Scale, width, height);

            _targets[spec.Id] = _device.CreateTexture(new RhiTextureDescriptor(
                targetWidth,
                targetHeight,
                spec.Format,
                spec.Usage,
                Label: spec.Id.ToString()));
        }
    }

    private void Release()
    {
        foreach (IRhiTexture target in _targets.Values)
        {
            target.Dispose();
        }

        _targets.Clear();
    }
}
