using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The sun's shadow cascades: the world drawn from the light, once per slice of the view.</summary>
/// <remarks>
/// <para>
/// The Vulkan original's, pass for pass. The game divides the view into cascades and hands each one's
/// projection and light rotation to <see cref="SetShadows"/>, then draws the casters into each between
/// <see cref="BeginShadowCascade"/> and <see cref="EndShadowCascade"/>. Each cascade is one layer of a
/// depth array the shading compares against.
/// </para>
/// <para>
/// This depth runs FORWARDS, unlike the scene's: cleared to one and tested less-or-equal, because it is
/// orthographic and the precision reverse-Z buys a perspective projection would buy nothing here.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The shadow map's format.</summary>
    private const RhiTextureFormat ShadowFormat = RhiTextureFormat.Depth32Float;

    private readonly float[] _cascadeSplits = new float[ShadowSettings.MaxCascades];
    private readonly Matrix4f[] _cascadeMatrices = new Matrix4f[ShadowSettings.MaxCascades];
    private readonly Matrix3f[] _cascadeLightRotations = new Matrix3f[ShadowSettings.MaxCascades];

    private ShadowSettings _shadowSettings = ShadowSettings.Off;
    private int _cascadeCount;
    private ShadowPipelineCache? _shadowPipelines;
    private IRhiTexture? _shadowMap;
    private IRhiTextureView? _shadowArray;
    private IRhiTextureView[] _shadowLayers = [];
    private IRhiRenderPassEncoder? _shadowPass;
    private int _shadowCascade;
    private bool _shadowsDrawn;

    /// <inheritdoc/>
    /// <remarks>
    /// Keeps the depth unprojection and view rotation the shading needs, and composes each cascade's
    /// camera-relative world-to-clip matrix — the orthographic projection after the world-to-light
    /// rotation, with no transpose, for the reason the Vulkan device's <c>ComposeCascade</c> gives. Sizes
    /// the shadow map to the cascade count and resolution.
    /// </remarks>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();

        _unprojection = unprojection;
        _viewToWorld = viewToWorld;
        _shadowSettings = settings;
        _cascadeCount = Math.Min(cascades.Length, ShadowSettings.MaxCascades);

        for (int i = 0; i < _cascadeCount; i++)
        {
            _cascadeSplits[i] = cascades[i].SplitDistance;
            _cascadeMatrices[i] = cascades[i].Projection * Matrix4f.CreateRigid(cascades[i].LightRotation, default);
            _cascadeLightRotations[i] = cascades[i].LightRotation;
        }

        if (_cascadeCount > 0)
        {
            EnsureShadowMap(settings.Resolution, _cascadeCount);
        }
    }

    /// <inheritdoc/>
    public bool BeginShadowCascade(int cascade)
    {
        ThrowIfDisposed();

        if (cascade < 0
            || cascade >= _cascadeCount
            || _shadowMap is not { } map
            || cascade >= _shadowLayers.Length
            || _shadowPipelines is null)
        {
            return false;
        }

        ClosePass();
        CloseScenePass();
        CloseShadowPass();

        OpenShadowCascade(cascade, map, resume: false);
        _shadowsDrawn = true;
        return true;
    }

    /// <summary>Opens one cascade's pass.</summary>
    /// <param name="cascade">Which cascade.</param>
    /// <param name="map">The shadow map it is a layer of.</param>
    /// <param name="resume">Whether this picks the cascade up again after a flush, keeping what it drew.</param>
    private void OpenShadowCascade(int cascade, IRhiTexture map, bool resume)
    {
        _shadowPass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [],
            new RhiDepthAttachment(
                _shadowLayers[cascade],
                resume ? RhiLoadOp.Load : RhiLoadOp.Clear,
                RhiStoreOp.Store,
                DepthClearValue: 1f),
            _profiler.Time(RenderStage.Shadows),
            Label: $"cascade{cascade}"));

        _shadowPass.SetViewport(0, 0, map.Width, map.Height, 0f, 1f);
        _shadowPass.SetScissor(0, 0, map.Width, map.Height);
        _shadowCascade = cascade;
    }

    /// <inheritdoc/>
    public void EndShadowCascade()
    {
        ThrowIfDisposed();
        CloseShadowPass();

        // A cascade is thousands of draws, finished while the next one records.
        SealEncoder("frame.cascade");
    }

    /// <summary>Gets whether a cascade is being drawn, which routes every draw into it.</summary>
    private bool DrawingShadow => _shadowPass is not null;

    /// <summary>Records one mesh into the cascade being drawn.</summary>
    /// <param name="call">What to draw.</param>
    /// <param name="ranges">Which parts of it, or empty for the call's own range.</param>
    /// <param name="modelView">Its transform into the light's view.</param>
    /// <param name="blend">How it is blended.</param>
    /// <remarks>
    /// Only what is solid casts. A blended draw is a decal over something that has already cast — the
    /// terrain's detail layers sit on a base that is opaque itself — and its alpha is a smooth blend, not
    /// a cut-out: through the cut-out test it casts the shadow of a chequerboard.
    /// </remarks>
    private void DrawShadow(in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, BlendMode blend)
    {
        if (blend != BlendMode.Opaque || _shadowPass is not { } pass || _shadowPipelines is not { } pipelines)
        {
            return;
        }

        Span<float> push = stackalloc float[ShadowPipelineCache.PushDataBytes / sizeof(float)];
        WriteShadowPush(push, modelView);

        IRhiRenderPipeline pipeline = pipelines.ForAlphaReference(_alphaReference);

        if (ranges.IsEmpty)
        {
            _meshes.Draw(pass, pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter);
        }
        else
        {
            _meshes.DrawRanges(pass, pipeline, call, ranges, MemoryMarshal.AsBytes(push), _frameCounter);
        }
    }

    /// <summary>The block a shadow draw carries: its transform, the terrain's slide, and its pose.</summary>
    /// <remarks>
    /// Terrain slides between levels of detail, and it has to cast from where it ended up, or the ground
    /// shadows itself along a shape it no longer has. The slide is measured from the eye — it has to be
    /// the same slide the frame drew — and this pass looks along the light from where the eye stands, so
    /// the light's rotation is what undoes its own view.
    /// </remarks>
    private void WriteShadowPush(Span<float> push, in Matrix4f modelView)
    {
        push.Clear();
        (_projection * modelView).CopyColumnMajorTo(push[..16]);

        if (_detailFade.Active)
        {
            Matrix3f lightToWorld = _cascadeLightRotations[_shadowCascade].Transposed();
            Vector3f offset = GBufferPushBlock.TerrainCameraOffset(modelView, lightToWorld);

            push[16] = _detailFade.Scale;
            push[17] = _detailFade.Offset;
            push[18] = _detailFade.Slope;
            push[19] = _detailFade.Mode;
            push[20] = offset.X;
            push[21] = offset.Y;
            push[22] = offset.Z;
        }

        // A body casts the shadow of the pose it is drawn in, or its shadow walks at the tick rate while
        // it does not.
        push[23] = _poseBlend;
    }

    /// <summary>Makes the shadow map, the size and depth the settings ask for.</summary>
    private void EnsureShadowMap(int resolution, int cascades)
    {
        resolution = Math.Max(1, resolution);

        if (_shadowMap is { } existing
            && existing.Width == resolution
            && existing.DepthOrArrayLayers >= cascades)
        {
            return;
        }

        ReleaseShadowMap(retire: true);

        // Always the most it can be asked for, so a preset that adds a cascade does not reallocate.
        _shadowMap = _device.CreateTexture(new RhiTextureDescriptor(
            resolution,
            resolution,
            ShadowFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
            DepthOrArrayLayers: ShadowSettings.MaxCascades,
            Label: "shadow-map"));

        _shadowArray = _shadowMap.CreateView(new RhiTextureViewDescriptor(
            ArrayLayerCount: ShadowSettings.MaxCascades,
            Dimension: RhiTextureViewDimension.Texture2DArray,
            Label: "shadow-map.array"));

        _shadowLayers = new IRhiTextureView[ShadowSettings.MaxCascades];

        for (int layer = 0; layer < _shadowLayers.Length; layer++)
        {
            _shadowLayers[layer] = _shadowMap.CreateView(new RhiTextureViewDescriptor(
                BaseArrayLayer: layer,
                ArrayLayerCount: 1,
                Label: $"shadow-map.{layer}"));
        }

        _shadowPipelines ??= new ShadowPipelineCache(_device, _materialLayout, ShadowFormat);
        _shading?.BindShadowMap(_shadowArray);
    }

    /// <summary>What the shading is told about the cascades this frame.</summary>
    /// <remarks>
    /// Whether they were drawn is kept apart from how many there are: the splits and the matrices
    /// describe how the view was divided either way, and the shading reads the map only where it holds
    /// something.
    /// </remarks>
    private DeferredLightingState WithShadows(in DeferredLightingState state) => state with
    {
        CascadeCount = _cascadeCount,
        ShadowsReady = _cascadeCount > 0 && _shadowsDrawn,
        DepthBias = _shadowSettings.DepthBias,
        NormalBias = _shadowSettings.NormalBias,
        ShadowTexelSize = _shadowMap is { } map ? 1f / map.Width : 0f,
    };

    /// <summary>The splits the shading divides the view by.</summary>
    private ReadOnlySpan<float> CascadeSplits => _cascadeSplits.AsSpan(0, _cascadeCount);

    /// <summary>The matrices it projects into each cascade with.</summary>
    private ReadOnlySpan<Matrix4f> CascadeMatrices => _cascadeMatrices.AsSpan(0, _cascadeCount);

    private void CloseShadowPass()
    {
        if (_shadowPass is not { } pass)
        {
            return;
        }

        pass.EndPass();
        pass.Dispose();
        _shadowPass = null;
    }

    /// <summary>Lets the map go: retired against the frame when a resize replaces it, freed on dispose.</summary>
    private void ReleaseShadowMap(bool retire)
    {
        foreach (IDisposable? owned in (IDisposable?[])[.. _shadowLayers, _shadowArray, _shadowMap])
        {
            if (owned is null)
            {
                continue;
            }

            if (retire)
            {
                _retired.Retire(owned);
            }
            else
            {
                owned.Dispose();
            }
        }

        _shadowLayers = [];
        _shadowArray = null;
        _shadowMap = null;
    }

    private void DisposeShadows()
    {
        _shadowPass?.Dispose();
        _shadowPass = null;
        ReleaseShadowMap(retire: false);
        _shadowPipelines?.Dispose();
        _shadowPipelines = null;
    }
}
