using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.SurfaceCache;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The surface-cache card capture: drawing every model flat into an atlas of its materials.</summary>
/// <remarks>
/// <para>
/// This runs once, when a level loads, and it does not belong to a frame. The caller opens the capture,
/// walks its list of cards asking for one page at a time, draws a model into each, and closes it. So it
/// records into an encoder of its own, submitted and waited on at the close — the frame loop is not
/// running yet and there is nothing to interleave with.
/// </para>
/// <para>
/// While the capture is open the ordinary mesh-drawing path records into that pass without knowing
/// anything has changed: the pass it asks for is the capture's, and the pipeline it is given is the
/// card one. What differs is the push block, which carries a model's material rather than a scene's
/// lighting.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private CardAtlas? _cardAtlas;
    private CardPipeline? _cardPipeline;
    private IRhiCommandEncoder? _cardEncoder;
    private IRhiRenderPassEncoder? _cardPass;
    /// <summary>What the surfaces being drawn are made of, until the next override.</summary>
    private SurfaceMaterial _material = SurfaceMaterial.Default;

    /// <summary>Gets whether a card capture is open, which routes every mesh draw into it.</summary>
    private bool CapturingCards => _cardPass is not null;

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height)
    {
        ThrowIfDisposed();

        if (CapturingCards)
        {
            throw new GraphicsDeviceException("A card capture is already open.");
        }

        // Refused only inside a scene capture, as the Vulkan renderer refuses it: the capture draws through
        // the device's per-draw state, which a scene capture owns. An open FRAME is no obstacle -- the
        // cards record into an encoder of their own and are submitted apart, and everything the load
        // uploaded is queued ahead of that submission. It has to be allowed: the menu loads a mission
        // while it goes on drawing, so on that route a frame is always open, and refusing here left every
        // mission entered from the menu without a surface cache -- its global illumination held at the
        // baked daylight, and a night chosen in the menu came up lit like noon.
        if (Capturing || width <= 0 || height <= 0)
        {
            return false;
        }

        EnsureCardAtlas(width, height);

        _cardEncoder = _device.CreateCommandEncoder("cards");
        _cardPass = _cardAtlas!.Begin(_cardEncoder);
        return true;
    }

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        ThrowIfDisposed();
        CardAtlas.SetPage(CardPass(), x, y, width, height);
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        ThrowIfDisposed();

        IRhiRenderPassEncoder pass = CardPass();
        pass.EndPass();
        pass.Dispose();
        _cardPass = null;

        // Waited on rather than left in flight: the caller reads the atlas back straight after, and
        // every frame from here on samples it.
        _device.Submit([_cardEncoder!.Finish("cards")]);
        _cardEncoder.Dispose();
        _cardEncoder = null;
        _device.WaitIdle();
    }

    /// <inheritdoc/>
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination)
    {
        ThrowIfDisposed();

        if (_cardAtlas is not { } atlas)
        {
            throw new GraphicsDeviceException("There is no card atlas to read.");
        }

        if (atlas.Target(attachment) is not { } page)
        {
            throw new GraphicsDeviceException($"There is no card atlas attachment {attachment}.");
        }

        int texels = atlas.Width * atlas.Height;

        if (destination.Length < texels)
        {
            throw new GraphicsDeviceException(
                $"The card atlas is {atlas.Width}x{atlas.Height}; the destination is shorter.");
        }

        // Every page is four bytes a texel, so the caller's span carries them unconverted: this is a
        // diagnostic dump, and what it wants is the bits rather than a colour.
        const int BytesPerTexel = 4;
        int row = RhiBufferTextureCopy.AlignRow(atlas.Width * BytesPerTexel);

        using IRhiBuffer readback = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * atlas.Height),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "cards.readback"));

        using (IRhiCommandEncoder encoder = _device.CreateCommandEncoder("cards.read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(page),
                new RhiBufferTextureCopy(readback, 0, row, atlas.Height),
                new RhiExtent3D(atlas.Width, atlas.Height));

            _device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = _device.ReadBuffer(readback);

        if (!data.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new GraphicsDeviceException("The card atlas never came back from the device.");
        }

        ReadOnlySpan<byte> padded = data.GetData();
        Span<byte> bytes = MemoryMarshal.AsBytes(destination);

        for (int y = 0; y < atlas.Height; y++)
        {
            padded
                .Slice(y * row, atlas.Width * BytesPerTexel)
                .CopyTo(bytes.Slice(y * atlas.Width * BytesPerTexel));
        }
    }

    /// <summary>Records one mesh into the page the capture is aimed at.</summary>
    /// <param name="call">What to draw.</param>
    /// <param name="modelViewProjection">Its transform into the card's own window.</param>
    /// <returns><see langword="true"/> when it was drawn.</returns>
    private bool DrawCard(in MeshDrawCall call, in Matrix4f modelViewProjection)
    {
        Span<float> push = stackalloc float[CardPushBlock.Floats];
        CardPushBlock.Write(push, modelViewProjection, _material);

        // No frame group: a card pipeline reads the material and nothing else, and binding a group its
        // layout does not declare is refused.
        return _meshes.Draw(
            CardPass(),
            _cardPipeline!.Pipeline,
            call,
            MemoryMarshal.AsBytes(push),
            _frameCounter);
    }

    private IRhiRenderPassEncoder CardPass() =>
        _cardPass ?? throw new GraphicsDeviceException("No card capture is open.");

    private void EnsureCardAtlas(int width, int height)
    {
        if (_cardAtlas is { } existing && existing.Width == width && existing.Height == height)
        {
            return;
        }

        DisposeCardAtlas();

        _cardAtlas = new CardAtlas(_device, width, height);
        _cardPipeline = new CardPipeline(_device, _materialLayout, CardAtlas.Formats, CardAtlas.DepthFormat);
    }

    private void DisposeCardAtlas()
    {
        _cardPipeline?.Dispose();
        _cardPipeline = null;
        _cardAtlas?.Dispose();
        _cardAtlas = null;
    }
}
