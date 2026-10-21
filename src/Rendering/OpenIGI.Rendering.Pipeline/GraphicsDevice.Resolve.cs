using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The resolve: the lit, steadied scene taken through the lens and the tone map onto the screen.</summary>
/// <remarks>
/// The bloom and the sun's shafts are built here from the picture the resolve is about to read, so that
/// everything that is lens behaviour sits beside the one pass that consumes it.
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        ThrowIfDisposed();

        if (_scene is not { } scene || _sceneResolve is not { } resolve)
        {
            return;
        }

        ClosePass();

        _grainFrame = (_grainFrame + 1) % ResolvePushBlock.GrainCycle;

        // The shutter first, because everything after it is lens behaviour and a lens sees the smeared
        // picture: a bright moving highlight blooms along its own smear rather than from where it was.
        IRhiTextureView colour = RecordMotionBlur(scene, post) ?? ResolvedScene(scene);
        IRhiTextureView? bloom = RecordBloom(scene, colour, post);
        float adapted = MeterExposure(scene, post);

        Span<float> push = stackalloc float[ResolvePushBlock.Floats];

        ResolvePushBlock.Write(push, post, new ResolveFrameState(
            Aspect: scene.Height / (float)Math.Max(1, scene.Width),
            GrainPhase: _grainFrame * ResolvePushBlock.GrainStep,
            PresentWidth: CurrentMode.Width,
            PresentHeight: CurrentMode.Height,
            MaskView: _debugView.IsMask(),
            BloomRan: bloom is not null,
            AdaptedExposure: adapted,
            GradeSize: GradeSize(post)));

        // What a painted sky is divided by as it is drawn, so that it leaves the tone map at the colour
        // it was painted. Last frame's: the sky is drawn before the frame says what it will be exposed by.
        _sceneExposure = MathF.Max(post.Exposure * adapted, 1e-3f);

        // Into the back buffer, or into the picture's own image when the interface is to be drawn apart from
        // it this frame (frame generation). See GraphicsDevice.Layer.
        IRhiTextureView resolveTarget = _frame.Target;
        if (BeginLayeredResolve() is { } layered)
        {
            resolveTarget = layered.Target;
            resolve = layered.Resolve;
        }

        resolve.BindGrade(GradeSize(post) > 0f ? _colourLut?.View : null);
        resolve.BindFrame(colour, bloom);

        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(resolveTarget, RhiLoadOp.Clear, RhiStoreOp.Store, _clearColour)],
            Timestamps: _profiler.Time(RenderStage.ToneMapGrade, "tonemap"),
            Label: "resolve")))
        {
            pass.SetViewport(0, 0, CurrentMode.Width, CurrentMode.Height, 0f, 1f);
            pass.SetScissor(0, 0, CurrentMode.Width, CurrentMode.Height);
            resolve.Draw(pass, MemoryMarshal.AsBytes(push));
            pass.EndPass();
        }

        // The resolve cleared the frame and then covered all of it, so a clear the caller asked for is
        // done. Left armed, it is honoured by whatever opens the frame's pass next — the letterbox, the
        // HUD — which clears away the whole picture a moment after it was drawn.
        _clearPending = false;

        scene.AdvanceFrame();

        // The lens chain, finished while the interface records.
        SealEncoder("frame.lens");
    }

    /// <summary>Builds the lens scatter from the picture the resolve is about to read, where one is asked for.</summary>
    /// <returns>The summed bloom, or null where none ran.</returns>
    /// <remarks>
    /// Before the tone map and after the temporal resolve, and not for a mask view: that is a code value
    /// rather than radiance, and it gets neither this nor the vignette.
    /// </remarks>
    private IRhiTextureView? RecordBloom(SceneTargetSet scene, IRhiTextureView colour, in PostProcessSettings post)
    {
        if (!post.WantsBloom || _debugView.IsMask() || _bloom is not { } bloom)
        {
            return null;
        }

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Bloom, "bloom"),
            "bloom")))
        {
            (int width, int height) = PostSize(scene);
            bloom.Record(
                pass,
                colour,
                width,
                height,
                post.BloomIntensity,
                post.BloomThreshold,
                FrameSlot);

            // The sun's shafts, laid onto the bloom where the sun is on screen or near it.
            float shafts = RenderCVars.SunShafts.Value;
            if (shafts > 0f && bloom.Result is { } summed && SunOnScreen(out (float X, float Y) sunUv, out float visible))
            {
                _sunShafts ??= new SunShaftPass(_device, FramesInFlight, _retired);
                _sunShafts.Record(
                    pass,
                    colour,
                    width,
                    height,
                    scene.View(SceneTargetId.Depth),
                    scene.Width,
                    scene.Height,
                    summed,
                    Math.Max(1, width >> 1),
                    Math.Max(1, height >> 1),
                    sunUv,
                    shafts * SunShaftScale,
                    visible,
                    Math.Min(shafts, 1f) * SunShaftOcclusion,
                    (_unprojection.Near, _unprojection.Far),
                    FrameSlot);
                return _sunShafts.Result;
            }
        }

        return bloom.Result;
    }

    /// <summary>What the sky round the sun adds to the bloom as shafts, at a strength of one.</summary>
    /// <remarks>
    /// Under a half: the mask is the sky's own radiance, and laid on at full strength the glow round the sun
    /// doubled the sky's brightness there; a quarter left the rays too faint to find.
    /// </remarks>
    private const float SunShaftScale = 0.4f;

    /// <summary>How deep the shadows the occluders cast into the haze round the sun are cut, at a strength of one.</summary>
    /// <remarks>
    /// The shafts are mostly these: the lit air either side of an occluder's shadow. Bright rays alone, added
    /// to an already bright sky, read as a smear rather than as light.
    /// </remarks>
    private const float SunShaftOcclusion = 0.6f;

    /// <summary>Where the sun stands on screen, and how much of its shafts that leaves.</summary>
    /// <param name="uv">The sun's screen position, which may lie off the screen.</param>
    /// <param name="visible">One with the sun well on screen, fading to nought as it leaves it or turns behind.</param>
    /// <returns>Whether there is anything to draw.</returns>
    private bool SunOnScreen(out (float X, float Y) uv, out float visible)
    {
        Vector3f sun = _lighting.SunDirection;
        uv = default;
        visible = 0f;
        if (sun.Z <= 0.05f)
        {
            return false;
        }

        uv = (
            0.5f + (sun.X * _unprojection.XScale / (2f * sun.Z)),
            0.5f - (sun.Y * _unprojection.YScale / (2f * sun.Z)));

        // Off the screen by as much as a third of it the glow still reaches in; past that, nothing.
        float outside = MathF.Max(
            MathF.Max(-uv.X, uv.X - 1f),
            MathF.Max(-uv.Y, uv.Y - 1f));
        float edge = Math.Clamp(1f - (outside / 0.35f), 0f, 1f);
        visible = edge * Math.Clamp((sun.Z - 0.05f) * 4f, 0f, 1f);
        return visible > 0.001f;
    }
}
