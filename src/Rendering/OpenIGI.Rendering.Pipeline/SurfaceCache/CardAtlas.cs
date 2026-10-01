using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.SurfaceCache;

/// <summary>
/// The four images the world's surfaces are captured into, and the pass that draws into them.
/// </summary>
/// <remarks>
/// <para>
/// Lighting a room from bounced light means knowing how bright every surface in it is, including the
/// ones off screen and behind the camera. Re-shading those per ray is far too expensive, so every model
/// is photographed once, flat, from up to six directions — those photographs are cards, and they live
/// here. A ray that hits a wall does not shade the wall; it looks up the wall's page.
/// </para>
/// <para>
/// Four targets, because a photograph of a material needs four things: what colour it is, which way it
/// faces, how it responds to light, and how far behind the card's window it sits. The last is what lets
/// a reader tell whether the surface it hit is the one the card recorded, or something else that
/// happens to project to the same place.
/// </para>
/// <para>
/// Capture happens at load rather than in a frame, which is why the whole atlas is cleared once when the
/// pass opens and each model then takes a page of it with a viewport and a scissor.
/// </para>
/// </remarks>
public sealed class CardAtlas : IDisposable
{
    /// <summary>How many colour targets a capture writes.</summary>
    public const int TargetCount = 4;

    /// <summary>What each of the four pages holds.</summary>
    /// <remarks>
    /// Ten bits for the normal, because the bounced light a normal steers is smooth and low-contrast and
    /// eight bits there bands visibly across a flat wall. A full float for the card depth, because that
    /// value is the whole basis of a reader's "is this the surface I hit" test and it is not worth being
    /// clever about. Every target is four bytes a texel, which is what a readback assumes.
    /// </remarks>
    public static IReadOnlyList<RhiTextureFormat> Formats { get; } =
    [
        RhiTextureFormat.Rgba8Unorm,
        RhiTextureFormat.Rgb10A2Unorm,
        RhiTextureFormat.Rgba8Unorm,
        RhiTextureFormat.R32Float,
    ];

    /// <summary>What the capture's own depth buffer holds.</summary>
    public const RhiTextureFormat DepthFormat = RhiTextureFormat.Depth32Float;

    private readonly IRhiTexture[] _targets;
    private readonly IRhiTexture _depthBuffer;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="CardAtlas"/> class.</summary>
    /// <param name="device">The device the atlas lives on.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">Its height.</param>
    public CardAtlas(IRhiDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;

        _targets = new IRhiTexture[TargetCount];
        string[] names = ["albedo", "normal", "material", "depth"];

        for (int i = 0; i < TargetCount; i++)
        {
            // Drawn into by a raster pass, then sampled by compute every frame after, and read back to
            // the host by the tool that dumps the atlas.
            _targets[i] = device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                Formats[i],
                RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled | RhiTextureUsage.CopySource,
                Label: $"cards.{names[i]}"));
        }

        // Depth testing only; nothing reads this afterwards, since the linear depth a card wants was
        // written to its own target by the fragment stage.
        _depthBuffer = device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            DepthFormat,
            RhiTextureUsage.RenderTarget,
            Label: "cards.depthbuffer"));
    }

    /// <summary>Gets the atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the surface colour, unlit, with coverage in the fourth channel.</summary>
    public IRhiTexture Albedo => _targets[0];

    /// <summary>Gets the surface normal in the model's own space.</summary>
    public IRhiTexture Normal => _targets[1];

    /// <summary>Gets roughness, metalness and emissiveness.</summary>
    public IRhiTexture Material => _targets[2];

    /// <summary>Gets how far behind the card's window the surface sits, as a fraction of its depth.</summary>
    public IRhiTexture Depth => _targets[3];

    /// <summary>Gets one of the four targets by the index the engine names it with.</summary>
    /// <param name="attachment">Zero albedo, one normal, two material, three depth.</param>
    /// <returns>The texture, or <see langword="null"/> when the index names no target.</returns>
    public IRhiTexture? Target(int attachment) =>
        attachment >= 0 && attachment < TargetCount ? _targets[attachment] : null;

    /// <summary>Opens the capture, clearing every target including the gutters between pages.</summary>
    /// <param name="encoder">The encoder to record into.</param>
    /// <returns>The pass, which the caller ends.</returns>
    /// <remarks>
    /// Cleared to zero, so coverage is zero everywhere no triangle reaches. The depth buffer clears to
    /// one rather than zero: a card's depth runs the ordinary way, near at zero and far at one, so the
    /// value standing for "nothing here yet" is the far plane — which is the opposite of the reverse-Z
    /// the rest of this renderer draws with, and is why the capture states its own.
    /// </remarks>
    public IRhiRenderPassEncoder Begin(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ObjectDisposedException.ThrowIf(_disposed, this);

        RhiColorAttachment[] colours = new RhiColorAttachment[TargetCount];

        for (int i = 0; i < TargetCount; i++)
        {
            colours[i] = new RhiColorAttachment(
                _targets[i].View,
                RhiLoadOp.Clear,
                RhiStoreOp.Store,
                RhiClearColor.Transparent);
        }

        return encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            colours,
            new RhiDepthAttachment(
                _depthBuffer.View,
                RhiLoadOp.Clear,
                RhiStoreOp.Discard,
                DepthClearValue: 1f),
            Label: "cards"));
    }

    /// <summary>Aims the following draws at one page of the atlas.</summary>
    /// <param name="pass">The capture pass.</param>
    /// <param name="x">The page's left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="width">The page width.</param>
    /// <param name="height">Its height.</param>
    /// <remarks>
    /// Both the viewport and the scissor, not just the viewport. The viewport decides where the card's
    /// clip space lands; the scissor is what stops a model drawn wider than its own card from writing
    /// over the page next to it.
    /// </remarks>
    public static void SetPage(IRhiRenderPassEncoder pass, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pass);

        pass.SetViewport(x, y, width, height, 0f, 1f);
        pass.SetScissor(x, y, width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _depthBuffer.Dispose();

        foreach (IRhiTexture target in _targets)
        {
            target.Dispose();
        }
    }
}
