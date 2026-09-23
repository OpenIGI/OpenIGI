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
    /// <summary>The compiled form for a device that traces rays.</summary>
    /// <remarks>
    /// It reads the traced lamp visibility and binds the world, so each lamp the reservoir's one ray did
    /// not cover gets a ray of its own -- the Vulkan renderer's shading, in its sampled G-buffer form.
    /// </remarks>
    public static ShaderVariant TracedVariant => new("rayquery.sampled");

    /// <summary>The compiled form everywhere else, which marches the screen for a lamp's shadow.</summary>
    public static ShaderVariant SoftwareVariant => new("software.sampled");

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
    /// <param name="traced">Whether the device traces rays, which chooses <see cref="TracedVariant"/>.</param>
    public DeferredLighting(IRhiDevice device, int framesInFlight, RhiTextureFormat litFormat, bool traced = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        Variant = traced ? TracedVariant : SoftwareVariant;
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

    /// <summary>Gets which compiled form this draws.</summary>
    public ShaderVariant Variant { get; }

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

    /// <summary>Points the shading at the atmosphere's tables instead of at stand-ins.</summary>
    /// <param name="skyView">The sky's colour by direction, which the background is read from.</param>
    /// <param name="transmittance">How much sunlight survives to a point.</param>
    /// <param name="irradiance">The sky's light as harmonics, which every surface reads its ambient from.</param>
    /// <param name="sampler">The sampler the three are read through.</param>
    /// <remarks>
    /// Once, when the atmosphere is stood up; the tables are a fixed size and never change place. What
    /// this replaces is three of the stand-ins, and the sweep that fills the rest leaves a slot alone
    /// once something has been bound to it — which is how each pass takes its own slot as it is built.
    /// </remarks>
    public void BindSky(
        IRhiTextureView skyView,
        IRhiTextureView transmittance,
        IRhiTextureView irradiance,
        IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(skyView);
        ArgumentNullException.ThrowIfNull(transmittance);
        ArgumentNullException.ThrowIfNull(irradiance);
        ArgumentNullException.ThrowIfNull(sampler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.Bind("uSkyView", skyView, sampler);
        _bindings.Bind("uTransmittanceLut", transmittance, sampler);
        _bindings.Bind("uSkyIrradiance", irradiance, sampler);
    }

    /// <summary>Points the shading at what the GI stack produced this frame, or at stand-ins where it did not.</summary>
    /// <param name="screen">The screen gather's resolved light, or null for none.</param>
    /// <param name="red">The radiance cache's red harmonics, or null for no cache.</param>
    /// <param name="green">Its green.</param>
    /// <param name="blue">Its blue.</param>
    /// <param name="distance">Its visibility.</param>
    /// <remarks>
    /// Every frame, because what the game hands over can come and go: a slot left bound to last frame's
    /// volume after the game cleared it would light the frame from a cache nothing is keeping current.
    /// The block says whether each is worth reading; this makes sure what it is told not to read is a
    /// stand-in rather than something stale.
    /// </remarks>
    public void BindIndirect(
        IRhiTextureView? screen,
        IRhiTextureView? red,
        IRhiTextureView? green,
        IRhiTextureView? blue,
        IRhiTextureView? distance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        BindOrStandIn("uScreenGi", screen);
        BindOrStandIn("uProbeRed", red);
        BindOrStandIn("uProbeGreen", green);
        BindOrStandIn("uProbeBlue", blue);
        BindOrStandIn("uProbeDistance", distance);
    }

    /// <summary>Points the shading at this frame's fog volume, or at nothing.</summary>
    /// <param name="volume">The integrated volume, or null for a frame with no fog.</param>
    /// <remarks>
    /// With none, a stand-in fills the slot and the lighting block's slice distribution says there is no
    /// fog to read, so the shading never samples it.
    /// </remarks>
    public void BindFog(IRhiTextureView? volume) => BindOrStandIn("uFogVolume", volume);

    /// <summary>Points the shading at the level's roofs, which keep water off the ground beneath them.</summary>
    /// <param name="roofs">The roof map, or null where the level has none, whose block then says so.</param>
    public void BindShelterMap(IRhiTextureView? roofs) => BindOrStandIn("uShelterMap", roofs);

    /// <summary>Points the traced form at this frame's lamp visibility and the world, where it has them.</summary>
    /// <param name="visibility">The filtered lamp visibility, or null for a frame that did not trace it.</param>
    /// <param name="world">The structure the per-lamp rays trace against.</param>
    /// <remarks>
    /// Nothing on the software form, which declares neither. On the traced form a frame without a trace
    /// reads a stand-in, and the lamp block says the lamps are not traced, so it is never sampled.
    /// </remarks>
    public void BindLampVisibility(IRhiTextureView? visibility, IRhiAccelerationStructure? world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_bindings.Declares("uLampVisibility"))
        {
            return;
        }

        BindOrStandIn("uLampVisibility", visibility);

        if (world is not null)
        {
            _bindings.Bind("uWorldTlas", world);
        }
    }

    /// <summary>Points the shading at this frame's traced sun shadow, or at nothing.</summary>
    /// <param name="mask">How much sun reaches each pixel, or null for a frame whose sun uses the cascades.</param>
    /// <remarks>
    /// With none, a stand-in fills the slot and the lighting block says the sun is not traced, so the
    /// shading reads the cascades instead.
    /// </remarks>
    public void BindSunShadow(IRhiTextureView? mask) => BindOrStandIn("uSunShadow", mask);

    private void BindOrStandIn(string name, IRhiTextureView? view)
    {
        ShaderBinding binding = Reflection.Bindings.First(slot => string.Equals(slot.Name, name, StringComparison.Ordinal));
        _bindings.Bind(name, view ?? _standIns.For(binding), _sampler);
    }

    /// <summary>Points the shading at the sun's cascades instead of at a stand-in.</summary>
    /// <param name="cascades">The shadow map, every cascade a layer of one depth array.</param>
    /// <remarks>
    /// Once, when the map is made; it is remade only when its resolution changes. The comparison
    /// sampler it is read through is bound with the frame, because it is the shading's own.
    /// </remarks>
    public void BindShadowMap(IRhiTextureView cascades)
    {
        ArgumentNullException.ThrowIfNull(cascades);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.Bind("uShadowMap", cascades);
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
