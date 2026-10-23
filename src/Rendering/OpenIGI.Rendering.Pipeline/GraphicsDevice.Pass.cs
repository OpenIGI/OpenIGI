using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The one render pass the direct path has: everything into the back buffer.</summary>
/// <remarks>
/// <para>
/// The interface above draws without saying where a pass begins or ends — it clears, it draws, it
/// swaps — while the RHI states attachments when a pass begins and forbids anything else until it ends.
/// So the pass is opened lazily by the first thing that needs one and closed by whatever cannot happen
/// inside it: a clear, a copy, the end of the frame.
/// </para>
/// <para>
/// A clear is therefore not a command but a property of the next pass. That is the only shape the RHI
/// has, and it is also what a tiler wants; the cost is that a clear mid-frame ends the pass and starts
/// another.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private IRhiTexture? _depth;
    private RhiClearColor _clearColour = RhiClearColor.Transparent;
    private Viewport _viewport;
    private bool _clearPending;
    private bool _clearDepthPending;

    /// <summary>The pass everything draws into, opened if it is not already.</summary>
    /// <remarks>
    /// The scene's pass is closed first if one is open. A render pass begun inside another is not
    /// nesting but undefined, and the two cannot both be right anyway: what draws into the frame is not
    /// what draws into the G-buffer.
    /// </remarks>
    private IRhiRenderPassEncoder Pass()
    {
        CloseScenePass();
        CloseShadowPass();

        if (_pass is { } open)
        {
            return open;
        }

        IRhiCommandEncoder encoder = Encoder();
        _frameDrawn = true;

        // The interface layer starts each frame transparent: it holds only what is drawn into it, and the
        // end of the frame lays it over the picture by its coverage.
        // The heads-up display's own layer likewise, while it is open (BeginHudLayer).
        bool clearLayer = _hudActive ? _hudClearPending : DrawsIntoLayer && _layerClearPending;

        _pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [
                new RhiColorAttachment(
                    FrameDrawTarget,
                    clearLayer || _clearPending ? RhiLoadOp.Clear : RhiLoadOp.Load,
                    RhiStoreOp.Store,
                    clearLayer ? RhiClearColor.Transparent : _clearColour),
            ],
            new RhiDepthAttachment(
                Depth().View,

                // Reverse-Z, so the far plane is nought and a pass that clears starts with everything
                // already further away than anything it will draw.
                _clearDepthPending ? RhiLoadOp.Clear : RhiLoadOp.Load,
                RhiStoreOp.Store,
                DepthClearValue: 0f),

            // The stage the host has bracketed the draws in, and otherwise what the frame's pass mostly
            // is: the forward drawing of the direct path.
            _profiler.Time(_stageBracket ?? RenderStage.Forward),
            Label: "present"));

        _clearPending = false;
        _clearDepthPending = false;
        if (_hudActive)
        {
            _hudClearPending = false;
        }
        else
        {
            _layerClearPending = false;
        }

        ApplyViewport(_pass);
        return _pass;
    }

    /// <summary>Closes the render pass, if one is open.</summary>
    /// <remarks>
    /// Not the interface's EndPass, which means the end of a SCENE pass — a different thing entirely,
    /// and the reason this one is named for what it closes.
    /// </remarks>
    private void ClosePass()
    {
        if (_pass is not { } open)
        {
            return;
        }

        open.EndPass();
        open.Dispose();
        _pass = null;
    }

    /// <summary>The depth buffer the direct path draws against, sized to the mode.</summary>
    /// <remarks>
    /// Made on demand and thrown away on a mode change rather than allocated with the device, because
    /// a session that never draws anything three-dimensional — the menu before a mission loads — has no
    /// use for one.
    /// </remarks>
    private IRhiTexture Depth()
    {
        if (_depth is { } existing
            && existing.Width == CurrentMode.Width
            && existing.Height == CurrentMode.Height)
        {
            return existing;
        }

        if (_depth is { } outgrown)
        {
            _retired.Retire(outgrown);
        }

        _depth = _device.CreateTexture(new RhiTextureDescriptor(
            Math.Max(1, CurrentMode.Width),
            Math.Max(1, CurrentMode.Height),
            DepthFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
            Label: "present.depth"));

        return _depth;
    }

    private void ApplyViewport(IRhiRenderPassEncoder pass)
    {
        int width = _viewport.Width > 0 ? _viewport.Width : CurrentMode.Width;
        int height = _viewport.Height > 0 ? _viewport.Height : CurrentMode.Height;

        pass.SetViewport(
            _viewport.X,
            _viewport.Y,
            width,
            height,
            _viewport.MinDepth,
            _viewport.MaxDepth is 0f && _viewport.MinDepth is 0f ? 1f : _viewport.MaxDepth);

        pass.SetScissor(Math.Max(0, _viewport.X), Math.Max(0, _viewport.Y), width, height);
    }
}
