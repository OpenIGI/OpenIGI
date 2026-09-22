using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Meshes;
using OpenIGI.Rendering.Vulkan.SurfaceCache;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>The surface-cache card capture: drawing every model flat into an atlas of its materials.</summary>
/// <remarks>
/// <para>
/// This runs once, when a level loads, and it does not belong to a frame. The caller opens the capture,
/// walks its list of cards asking for one page at a time, draws a model into each, and closes it. So the
/// recording happens into a command buffer of its own, submitted and waited on at the close — the frame
/// loop is not running yet and there is nothing to interleave with.
/// </para>
/// <para>
/// While the capture is open, the device's current command buffer is pointed at that one, so the ordinary
/// mesh-drawing path records into it without knowing anything has changed. The stage flag is what tells
/// that path to use the card pipeline rather than the G-buffer's.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    private CardAtlas? _cardAtlas;
    private CardPipeline? _cardPipeline;
    private CommandBuffer _cardCommandBuffer;
    private CommandBuffer _suspendedCommandBuffer;
    private bool _cardCaptureActive;
    private TextureHandle _cardAlbedo = TextureHandle.None;
    private TextureHandle _cardNormal = TextureHandle.None;
    private TextureHandle _cardMaterial = TextureHandle.None;
    private TextureHandle _cardDepth = TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => _cardAlbedo;

    /// <inheritdoc/>
    public TextureHandle CardNormal => _cardNormal;

    /// <inheritdoc/>
    public TextureHandle CardMaterial => _cardMaterial;

    /// <inheritdoc/>
    public TextureHandle CardDepth => _cardDepth;

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height)
    {
        ThrowIfDisposed();

        if (_cardCaptureActive)
        {
            throw new GraphicsDeviceException("A card capture is already open.");
        }

        // A capture inside a frame would have to share the frame's command buffer and its render pass,
        // which is neither needed nor safe. Levels load between frames.
        if (_capturing)
        {
            WarnOnce("CardCaptureInFrame", "A card capture was asked for while a frame was being recorded; it was refused.");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        try
        {
            EnsureCardAtlas(width, height);
        }
        catch (GraphicsDeviceException error)
        {
            WarnOnce("CardAtlasFailed", $"The card atlas could not be made: {error.Message}");
            return false;
        }

        _cardCommandBuffer = _commands.BeginOneOff();
        _suspendedCommandBuffer = _frameCommandBuffer;
        _frameCommandBuffer = _cardCommandBuffer;

        _cardAtlas!.Begin(_cardCommandBuffer);
        _cardCaptureActive = true;
        return true;
    }

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        ThrowIfDisposed();
        if (!_cardCaptureActive || _cardAtlas is null)
        {
            throw new GraphicsDeviceException("No card capture is open.");
        }

        _cardAtlas.SetPage(_cardCommandBuffer, x, y, width, height);
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        ThrowIfDisposed();
        if (!_cardCaptureActive || _cardAtlas is null)
        {
            throw new GraphicsDeviceException("No card capture is open.");
        }

        _cardAtlas.End(_cardCommandBuffer);
        _cardCaptureActive = false;
        _frameCommandBuffer = _suspendedCommandBuffer;

        // Waited on rather than left in flight: the caller reads the atlas back straight after, and every
        // frame from here on samples it.
        _commands.SubmitOneOff(_cardCommandBuffer);
        _cardCommandBuffer = default;
    }

    /// <inheritdoc/>
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination)
    {
        ThrowIfDisposed();
        if (_cardAtlas is null)
        {
            throw new GraphicsDeviceException("There is no card atlas to read.");
        }

        VulkanStorageImage? image = _cardAtlas.Target(attachment);
        if (image is null)
        {
            throw new GraphicsDeviceException($"There is no card atlas attachment {attachment}.");
        }

        int texels = image.Width * image.Height;
        if (destination.Length < texels)
        {
            throw new GraphicsDeviceException(
                $"The card atlas is {image.Width}x{image.Height}; the destination is shorter.");
        }

        // Every target is four bytes a texel, so the caller's span carries them unconverted — the same
        // raw-bytes-through-a-colour-span the storage-texture readback uses, and for the same reason: this
        // is a diagnostic dump, and what it wants is the bits.
        byte[] raw = new byte[texels * 4];
        CopyImageToHost(image, raw);
        raw.AsSpan(0, Math.Min(raw.Length, destination.Length * 4)).CopyTo(MemoryMarshal.AsBytes(destination));
    }

    /// <summary>Binds a mesh for a draw into the card atlas.</summary>
    /// <param name="mesh">The mesh being captured.</param>
    /// <param name="modelView">Its transform into the card's own camera.</param>
    /// <param name="diffuse">Its colour texture.</param>
    /// <param name="lightmap">Its lightmap, bound only to satisfy the shared texture set.</param>
    /// <remarks>
    /// The lightmap is deliberately not read by the card shader. A page holds unlit albedo, and the whole
    /// point of the surface cache is that the lighting over it is recomputed every frame; baking a
    /// lightmap into the capture would fix one lighting state into a texture that outlives it.
    /// </remarks>
    private unsafe void BindCard(
        VulkanMesh mesh,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap)
    {
        if (_cardPipeline is null)
        {
            return;
        }

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _cardCommandBuffer;

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _cardPipeline.Pipeline);
        InvalidateBoundState();
        BindMeshBuffers(mesh);
        BindMeshTextures(_cardPipeline.PipelineLayout, diffuse, lightmap);

        Matrix4f mvp = _projection * modelView;
        float* data = stackalloc float[32];
        mvp.CopyColumnMajorTo(new Span<float>(data, 16));

        // The normal rows go unused — a card stores the model's own normal, which needs no transform —
        // but the push block is shared with the G-buffer's, so they are written rather than left stale.
        for (int i = 16; i < 28; i++)
        {
            data[i] = 0f;
        }

        data[28] = _material.Roughness;
        data[29] = _material.Metallic;
        data[30] = _material.Emissive;
        data[31] = 0f;

        vk.CmdPushConstants(
            cmd,
            _cardPipeline.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            128,
            data);
    }

    private void EnsureCardAtlas(int width, int height)
    {
        if (_cardAtlas is { } existing && existing.Width == width && existing.Height == height)
        {
            return;
        }

        DisposeCardAtlas();

        VulkanStorageImage[] targets = new VulkanStorageImage[4];
        for (int i = 0; i < 4; i++)
        {
            targets[i] = CardAtlas.CreateTarget(_instance.Vk, _device, _allocator, _commands, width, height, i);
        }

        _cardAtlas = new CardAtlas(
            _instance.Vk, _device, _allocator, _commands, width, height,
            targets[0], targets[1], targets[2], targets[3]);

        _cardPipeline = new CardPipeline(
            _instance.Vk, _device, _shaderCompiler, _cardAtlas.RenderPass, _mesh3D.CombinedTextureSetLayout);

        // Registered with the compute side, so the lighting passes can bind them by handle. That also
        // makes the device their owner: it frees everything it has registered.
        _cardAlbedo = RegisterStorageImage(targets[0]);
        _cardNormal = RegisterStorageImage(targets[1]);
        _cardMaterial = RegisterStorageImage(targets[2]);
        _cardDepth = RegisterStorageImage(targets[3]);
    }

    private void DisposeCardAtlas()
    {
        _cardPipeline?.Dispose();
        _cardPipeline = null;
        _cardAtlas?.Dispose();
        _cardAtlas = null;

        // The images themselves belong to the compute registry and are freed with it; only the handles
        // are dropped here, so nothing goes on naming an atlas that has been replaced.
        _cardAlbedo = TextureHandle.None;
        _cardNormal = TextureHandle.None;
        _cardMaterial = TextureHandle.None;
        _cardDepth = TextureHandle.None;
    }
}
