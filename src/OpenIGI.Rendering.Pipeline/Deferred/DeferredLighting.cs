using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The deferred shading: one triangle over the frame, turning the G-buffer into lit colour.
/// </summary>
/// <remarks>
/// <para>
/// The sampled form of the shading module rather than the subpass one. Vulkan can hand a lighting
/// subpass the attachments the pass before wrote and never move them off chip; WebGPU has no subpasses
/// at all, so the same shader reads the same texels as ordinary textures from a pass of its own. The
/// two produce identical colour — it is one source with one line different — and this is the form both
/// backends can run.
/// </para>
/// <para>
/// The shading reads far more than the renderer over the RHI yet produces: a probe field, the
/// atmosphere's tables, the cascades, the lamps. A slot a shader names has to be filled whether or not
/// this frame has anything for it, so everything the frame does not supply is bound to a one-texel
/// stand-in of the right shape, chosen from the module's own description of the slot. As each of those
/// passes is built over the RHI, it takes its slot by name and the stand-in stops being used for it.
/// </para>
/// </remarks>
public sealed class DeferredLighting : IDisposable
{
    /// <summary>Which compiled form this draws.</summary>
    /// <remarks>
    /// The software one: the traced form binds an acceleration structure, and the RHI offers one only
    /// through a capability a browser does not have. A device that can trace stands its own lamp shadows
    /// up separately rather than compiling a second shading.
    /// </remarks>
    public static ShaderVariant Variant => new("software.sampled");

    /// <summary>The G-buffer the shading reads, by the name the module declares and the target it is.</summary>
    private static readonly (string Name, SceneTargetId Target)[] GBuffer =
    [
        ("uBaseColour", SceneTargetId.BaseColour),
        ("uNormal", SceneTargetId.Normal),
        ("uMaterial", SceneTargetId.Material),
        ("uBaked", SceneTargetId.Baked),
        ("uDepth", SceneTargetId.Depth),
        ("uOcclusion", SceneTargetId.Occlusion),
    ];

    private readonly IRhiBindGroupLayout _layout;
    private readonly FullscreenPipeline _pipeline;
    private readonly ShaderBindings _bindings;
    private readonly StandInTextures _standIns;
    private readonly ComputeParameters _gi;
    private readonly ComputeParameters _lamps;
    private readonly IRhiSampler _sampler;
    private readonly IRhiSampler _comparison;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="DeferredLighting"/> class.</summary>
    /// <param name="device">The device the pass runs on.</param>
    /// <param name="framesInFlight">How many frames can be recorded before the first has finished.</param>
    /// <param name="litFormat">The format of the lit target it draws into.</param>
    public DeferredLighting(IRhiDevice device, int framesInFlight, RhiTextureFormat litFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        Reflection = ShaderCatalog.GetReflection(ShaderModuleId.Shading, Variant);

        _layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(Reflection, RhiShaderStages.Fragment),
            "shading"));

        _pipeline = new FullscreenPipeline(
            device,
            ShaderModuleId.Shading,
            _layout,
            litFormat,
            variant: Variant);

        _bindings = new ShaderBindings(device, Reflection, [_layout], RhiShaderStages.Fragment, "shading");
        _standIns = new StandInTextures(device, "shading.standin");

        _gi = new ComputeParameters(device, DeferredLightingBlocks.GiBytes, framesInFlight, "shading.gi");
        _lamps = new ComputeParameters(device, DeferredLightingBlocks.LampBytes, framesInFlight, "shading.lamps");

        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "shading.linear"));

        // The cascades are read by comparison rather than by value, which is a sampler of its own kind
        // and a slot of its own kind to bind it to.
        _comparison = device.CreateSampler(new RhiSamplerDescriptor(
            Compare: RhiCompare.LessEqual,
            Label: "shading.comparison"));
    }

    /// <summary>Gets what the module declares.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets how many bytes of push data the shading reads per draw.</summary>
    public int PushDataBytes => _pipeline.PushDataBytes;

    /// <summary>Gets what is bound, for a caller with a real sky or shadow map to put in a slot.</summary>
    public ShaderBindings Bindings => _bindings;

    /// <summary>Gets how many bind groups have been built, which a test can watch for churn.</summary>
    public int GroupsBuilt => _bindings.GroupsBuilt;

    /// <summary>Points the shading at this frame's G-buffer, parameters and stand-ins.</summary>
    /// <param name="targets">The frame's images.</param>
    /// <param name="frameInFlight">Which frame's parameter buffers to write.</param>
    /// <param name="gi">The GI parameter block.</param>
    /// <param name="lamps">The local-light block.</param>
    /// <remarks>
    /// Called every frame, and every frame it binds the same things until the frame is resized or the
    /// occlusion changes places, so the group behind it is built once and kept.
    /// </remarks>
    public void BindFrame(
        SceneTargetSet targets,
        int frameInFlight,
        ReadOnlySpan<float> gi,
        ReadOnlySpan<float> lamps)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach ((string name, SceneTargetId target) in GBuffer)
        {
            _bindings.Bind(name, targets.View(target), _sampler);
        }

        _bindings.Bind("gi", _gi.Write(frameInFlight, gi));
        _bindings.Bind("lamps", _lamps.Write(frameInFlight, lamps));

        // The comparison sampler first, so the sweep that fills what is left does not put a filtering
        // one in its slot.
        _bindings.Bind("uShadowMapSampler", _comparison);

        _bindings.BindRemainingTextures(_standIns.For);
        _bindings.BindRemainingSamplers(_sampler);
    }

    /// <summary>Records the draw into a pass already begun on the lit target.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="push">The per-draw block the shading reads.</param>
    /// <exception cref="InvalidOperationException">A slot the module declares was never bound.</exception>
    public void Draw(IRhiRenderPassEncoder pass, ReadOnlySpan<byte> push)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _pipeline.Draw(pass, _bindings.GroupFor(0), push);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _comparison.Dispose();
        _sampler.Dispose();
        _lamps.Dispose();
        _gi.Dispose();
        _standIns.Dispose();
        _bindings.Dispose();
        _pipeline.Dispose();
        _layout.Dispose();
    }
}
