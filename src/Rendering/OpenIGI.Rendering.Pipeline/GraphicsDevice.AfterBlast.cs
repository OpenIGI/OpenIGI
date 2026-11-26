using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>A bomb's after-blast over the lit scene. See <see cref="BombAfterBlastPass"/>.</summary>
/// <remarks>
/// Drawn in the resolve where the scene is drawn offscreen, and over the back buffer itself on the direct path, which keeps
/// no picture of its own: there it works from a copy of the frame and lays what it made back in its place.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private BombAfterBlastPass? _afterBlast;

    // The direct path's frame as the after-blast reads it, and what lays the after-blast's picture back in its place.
    private IRhiTexture? _afterBlastFrame;
    private FullscreenPipeline? _screenPicture;
    private IRhiBindGroupLayout? _screenPictureLayout;
    private ShaderBindings? _screenPictureBindings;

    /// <inheritdoc/>
    /// <remarks>
    /// The frame is copied into an sRGB image, so the pass reads it in linear light, the form the resolve hands it; the direct
    /// path is not exposed, so the pass is told an exposure of one. Its picture is drawn back over the frame by a fullscreen
    /// pass. A frame the device cannot copy from is left as it is.
    /// </remarks>
    public void DrawAfterBlastOverFrame(in BombAfterBlastSettings afterBlast)
    {
        ThrowIfDisposed();

        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        if (!afterBlast.IsActive
            || !_inFrame
            || _layered
            || _hudActive
            || width <= 1
            || height <= 1
            || !FrameReadable
            || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            return;
        }

        // Opened and closed, so a clear still pending lands before the copy reads the frame and its depth.
        Pass();
        ClosePass();
        if (_depth is not { } depth)
        {
            return;
        }

        IRhiTexture frameColour = _frame.Target.Texture;
        RhiTextureFormat format = FrameCopyFormat(frameColour.Format);
        if (_afterBlastFrame is null || _afterBlastFrame.Width != width || _afterBlastFrame.Height != height || _afterBlastFrame.Format != format)
        {
            if (_afterBlastFrame is not null)
            {
                _retired.Retire(_afterBlastFrame);
            }

            _afterBlastFrame = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
                Label: "afterblast.frame"));
        }

        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(frameColour),
            new RhiTextureRegion(_afterBlastFrame),
            new RhiExtent3D(Math.Min(width, frameColour.Width), Math.Min(height, frameColour.Height)));

        _afterBlast ??= new BombAfterBlastPass(_device, FramesInFlight, _retired);
        IRhiTextureView picture;
        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Forward, "afterblast"),
            "afterblast")))
        {
            picture = _afterBlast.Record(
                pass,
                _afterBlastFrame.View,
                depth.View,
                new BombAfterBlastFrame(width, height, depth.Width, depth.Height, _unprojection, 1f, (int)(_frameCounter % 1024)),
                afterBlast,
                FrameSlot);
        }

        LayPictureOverFrame(picture, width, height);
    }

    // Draws a picture in linear light over the whole back buffer (Post/ScreenPicture.slang).
    private void LayPictureOverFrame(IRhiTextureView picture, int width, int height)
    {
        if (_screenPicture is null)
        {
            ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ScreenPicture);
            _screenPictureLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
                ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
                "screenpicture"));
            _screenPicture = new FullscreenPipeline(
                _device,
                ShaderModuleId.ScreenPicture,
                _screenPictureLayout,
                PresentFormat,
                fragmentConstants: new Dictionary<int, RhiConstant>
                {
                    [Mesh3DConstants.TargetEncodes] = RhiConstant.Of(PresentFormat.IsSrgb() ? 1 : 0),
                });
            _screenPictureBindings = new ShaderBindings(_device, reflection, [_screenPictureLayout], RhiShaderStages.Fragment, "screenpicture");
        }

        _screenPictureBindings!.Bind("uPicture", picture);
        using IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(_frame.Target, RhiLoadOp.Load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.Forward, "afterblast.lay"),
            Label: "afterblast.lay"));
        pass.SetViewport(0, 0, width, height, 0f, 1f);
        pass.SetScissor(0, 0, width, height);
        _screenPicture.Draw(pass, _screenPictureBindings.GroupFor(0), []);
        pass.EndPass();
    }

    /// <summary>Draws the after-blast over the lit scene, where a bomb's front is out this frame.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The lit scene.</param>
    /// <param name="post">What the frame asked for.</param>
    /// <param name="exposure">The exposure the resolve will apply this frame.</param>
    /// <returns>The picture, or null where there is no front.</returns>
    /// <remarks>Not for a debug view, which isolates one term of the picture.</remarks>
    private IRhiTextureView? RecordAfterBlast(SceneTargetSet scene, IRhiTextureView colour, in PostProcessSettings post, float exposure)
    {
        if (!post.AfterBlast.IsActive || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            return null;
        }

        _afterBlast ??= new BombAfterBlastPass(_device, FramesInFlight, _retired);
        (int width, int height) = PostSize(scene);
        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "afterblast"),
            "afterblast"));
        return _afterBlast.Record(
            pass,
            colour,
            scene.View(SceneTargetId.Depth),
            new BombAfterBlastFrame(width, height, scene.Width, scene.Height, _unprojection, exposure, (int)(_frameCounter % 1024)),
            post.AfterBlast,
            FrameSlot);
    }

    private void DisposeAfterBlast()
    {
        _afterBlast?.Dispose();
        _afterBlast = null;
        _afterBlastFrame?.Dispose();
        _afterBlastFrame = null;
        _screenPictureBindings?.Dispose();
        _screenPictureBindings = null;
        _screenPicture?.Dispose();
        _screenPicture = null;
        _screenPictureLayout?.Dispose();
        _screenPictureLayout = null;
    }
}
