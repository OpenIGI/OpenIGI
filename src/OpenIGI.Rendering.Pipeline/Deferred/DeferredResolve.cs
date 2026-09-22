using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The resolve: the lit scene tone-mapped, graded and composited onto the frame that is presented.
/// </summary>
/// <remarks>
/// <para>
/// The other end of the deferred pair. It takes whichever picture the frame ended up with — the raw lit
/// colour, the temporally resolved one, or the motion-blurred one — and turns the half-float scene into
/// the eight-bit frame a display takes, with the bloom composited in and the authored grade applied.
/// </para>
/// <para>
/// All three of its inputs are always bound, including for a frame that has no bloom and no grade: a
/// shader that names a resource makes it statically used, and reading a descriptor nothing has written
/// is undefined however carefully the branch avoids it. A frame without one binds a stand-in and the
/// push block tells the shader to leave the colour alone.
/// </para>
/// </remarks>
public sealed class DeferredResolve : IDisposable
{
    /// <summary>The resolve's switch saying its target applies the sRGB transfer as it stores.</summary>
    private const int TargetEncodesConstant = 65;

    private readonly IRhiBindGroupLayout _layout;
    private readonly FullscreenPipeline _pipeline;
    private readonly ShaderBindings _bindings;
    private readonly StandInTextures _standIns;
    private readonly IRhiSampler _sampler;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="DeferredResolve"/> class.</summary>
    /// <param name="device">The device the pass runs on.</param>
    /// <param name="targetFormat">The format of the frame it draws into.</param>
    public DeferredResolve(IRhiDevice device, RhiTextureFormat targetFormat)
    {
        ArgumentNullException.ThrowIfNull(device);

        Reflection = ShaderCatalog.GetReflection(ShaderModuleId.Resolve);

        _layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(Reflection, RhiShaderStages.Fragment),
            "resolve"));

        // The resolve arrives at the display encoding. A target that encodes as it stores is told so, or
        // the picture is encoded twice and comes out washed out.
        _pipeline = new FullscreenPipeline(
            device,
            ShaderModuleId.Resolve,
            _layout,
            targetFormat,
            fragmentConstants: new Dictionary<int, RhiConstant>
            {
                [TargetEncodesConstant] = RhiConstant.Of(targetFormat.IsSrgb() ? 1 : 0),
            });
        _bindings = new ShaderBindings(device, Reflection, [_layout], RhiShaderStages.Fragment, "resolve");
        _standIns = new StandInTextures(device, "resolve.standin");

        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "resolve.linear"));
    }

    /// <summary>Gets what the module declares.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets how many bytes of push data the resolve reads per draw.</summary>
    public int PushDataBytes => _pipeline.PushDataBytes;

    /// <summary>Gets what is bound, for a caller with a real bloom or grade to put in a slot.</summary>
    public ShaderBindings Bindings => _bindings;

    /// <summary>Points the resolve at the picture this frame ended up with.</summary>
    /// <param name="scene">The lit colour to resolve, which is whichever picture the frame produced.</param>
    /// <remarks>
    /// The bloom and the grade take stand-ins until something produces them, which is what a frame with
    /// neither switched on would bind anyway.
    /// </remarks>
    public void BindFrame(IRhiTextureView scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.Bind("uScene", scene, _sampler);
        _bindings.BindRemainingTextures(_standIns.For);
        _bindings.BindRemainingSamplers(_sampler);
    }

    /// <summary>Records the draw into a pass already begun on the frame.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="push">The per-draw block the resolve reads.</param>
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

        _sampler.Dispose();
        _standIns.Dispose();
        _bindings.Dispose();
        _pipeline.Dispose();
        _layout.Dispose();
    }
}
