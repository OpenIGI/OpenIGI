using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Sky;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Sky;

/// <summary>
/// The atmosphere's lookup tables and the passes that build them: the sky's colour in every direction,
/// how much sunlight survives to a surface, and the sky's light reduced to nine numbers a colour.
/// </summary>
/// <remarks>
/// <para>
/// Two of the tables describe the air itself and nothing else, so they are built the first time this
/// runs and never again. The other two describe the sky as it looks from where the camera stands with
/// the sun where it currently is, and are rebuilt only when one of those actually moves.
/// </para>
/// <para>
/// The tables are small enough that rebuilding them costs almost nothing: the sky view is under twenty
/// thousand texels and the harmonics are eight. What they replace is a full atmospheric ray-march at
/// every pixel of the sky and at every surface asking how much sun reaches it.
/// </para>
/// <para>
/// There are no barriers here. Each pass reads what the one before wrote, and making that visible is
/// the backend's business — which is the whole reason this can be one piece of code over two very
/// different implementations.
/// </para>
/// </remarks>
public sealed class SkyAtmosphere : IDisposable
{
    /// <summary>Enough resolution for the transmittance to be smooth across the horizon.</summary>
    public const int TransmittanceWidth = 256;

    /// <summary>Its height, where it changes fastest.</summary>
    public const int TransmittanceHeight = 64;

    /// <summary>Multiple scattering varies slowly with both altitude and sun angle.</summary>
    public const int MultiScatterSize = 32;

    /// <summary>Wide rather than tall: a sky changes far more around the horizon than up it.</summary>
    public const int SkyViewWidth = 192;

    /// <summary>Its height, already packed towards the horizon by its parameterisation.</summary>
    public const int SkyViewHeight = 104;

    /// <summary>Seven vectors of coefficients plus the plain average.</summary>
    public const int IrradianceWidth = 8;

    /// <summary>How many floats the harmonics come back as.</summary>
    public const int IrradianceFloats = 28;

    private readonly IRhiDevice _device;
    private readonly IRhiTexture _transmittance;
    private readonly IRhiTexture _multiScatter;
    private readonly IRhiTexture _skyView;
    private readonly IRhiTexture _irradiance;
    private readonly IRhiSampler _sampler;
    private readonly ComputePass _transmittancePass;
    private readonly ComputePass _multiScatterPass;
    private readonly ComputePass _skyViewPass;
    private readonly ComputePass _irradiancePass;
    private readonly ComputeParameters _transmittanceBlock;
    private readonly ComputeParameters _multiScatterBlock;
    private readonly ComputeParameters _skyViewBlock;
    private readonly ComputeParameters _irradianceBlock;
    private readonly IRhiBuffer[] _readback;
    private readonly IRhiReadback?[] _pending;

    // Which march each slot's copy was taken after, so a reader can tell a slot holding the sky as it is now
    // from one still holding an hour ago. See SkyViewBuilds.
    private readonly int[] _slotBuild;

    private bool _staticTablesBuilt;
    private bool _dynamicBuilt;
    private SkyConditions _last;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SkyAtmosphere"/> class.</summary>
    /// <param name="device">The device the tables live on.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public SkyAtmosphere(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;

        _transmittance = Table(TransmittanceWidth, TransmittanceHeight, RhiTextureFormat.Rgba16Float, "sky.transmittance");
        _multiScatter = Table(MultiScatterSize, MultiScatterSize, RhiTextureFormat.Rgba16Float, "sky.multiscatter");
        _skyView = Table(SkyViewWidth, SkyViewHeight, RhiTextureFormat.Rgba16Float, "sky.view");

        // Wider than a colour needs, because these are coefficients rather than a picture: a half float
        // loses the small negative lobes the harmonics rely on.
        _irradiance = Table(IrradianceWidth, 1, RhiTextureFormat.Rgba32Float, "sky.irradiance");

        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "sky.linear"));

        _transmittancePass = new ComputePass(device, ShaderModuleId.SkyTransmittance, "sky.transmittance");
        _multiScatterPass = new ComputePass(device, ShaderModuleId.SkyMultiScatter, "sky.multiscatter");
        _skyViewPass = new ComputePass(device, ShaderModuleId.SkyView, "sky.view");
        _irradiancePass = new ComputePass(device, ShaderModuleId.SkyIrradiance, "sky.irradiance");

        _transmittanceBlock = new ComputeParameters(device, SkyParameters.Bytes, framesInFlight, "sky.transmittance");
        _multiScatterBlock = new ComputeParameters(device, SkyParameters.Bytes, framesInFlight, "sky.multiscatter");
        _skyViewBlock = new ComputeParameters(device, SkyParameters.Bytes, framesInFlight, "sky.view");
        _irradianceBlock = new ComputeParameters(device, SkyParameters.Bytes, framesInFlight, "sky.irradiance");

        _readback = new IRhiBuffer[framesInFlight];
        _pending = new IRhiReadback?[framesInFlight];
        _slotBuild = new int[framesInFlight];

        for (int i = 0; i < framesInFlight; i++)
        {
            _readback[i] = device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)RhiBufferTextureCopy.AlignRow(IrradianceWidth * 16),
                RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
                $"sky.readback{i}"));
        }

        Bind();
    }

    /// <summary>Gets the sky's colour by direction, which the shading reads the background from.</summary>
    public IRhiTextureView SkyViewTable => _skyView.View;

    /// <summary>Gets how much sunlight survives to a point, by height and sun angle.</summary>
    public IRhiTextureView TransmittanceTable => _transmittance.View;

    /// <summary>Gets the multiple scattering, which the sky march reads to light the air from the air.</summary>
    public IRhiTextureView MultiScatterTable => _multiScatter.View;

    /// <summary>Gets the sky's light as harmonics, which every surface reads its ambient from.</summary>
    public IRhiTextureView IrradianceTable => _irradiance.View;

    /// <summary>Gets the sampler the tables are read through: linear, and clamped at every edge.</summary>
    public IRhiSampler Sampler => _sampler;

    /// <summary>Gets whether the tables that describe the air alone have been built.</summary>
    public bool StaticTablesBuilt => _staticTablesBuilt;

    /// <summary>Gets how many times the sky march has run, which a test can watch for waste.</summary>
    public int SkyViewBuilds { get; private set; }

    /// <summary>Records this frame's update: the constant tables if they are not built, then the sky.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which frame's parameter buffers to write.</param>
    /// <param name="conditions">Where the sun is and how high the eye is.</param>
    /// <returns><see langword="true"/> when the sky march ran rather than holding its last answer.</returns>
    public bool Record(IRhiComputePassEncoder pass, int frameInFlight, in SkyConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> block = stackalloc float[SkyParameters.Floats];

        if (!_staticTablesBuilt)
        {
            // Built for nothing in particular: these two describe the air and not the sky, so they are
            // handed no sun and no viewpoint. Passing this frame's would make a constant table depend on
            // the frame that happened to be first.
            SkyConditions air = new(default, default, ViewHeightKm: 0f, SkyLightIntensity: 1f);

            SkyParameters.Write(block, TransmittanceWidth, TransmittanceHeight, air);
            _transmittancePass.Bind("p", _transmittanceBlock.Write<float>(frameInFlight, block));
            _transmittancePass.Dispatch(pass, TransmittanceWidth, TransmittanceHeight);

            SkyParameters.Write(block, MultiScatterSize, MultiScatterSize, air);
            _multiScatterPass.Bind("p", _multiScatterBlock.Write<float>(frameInFlight, block));
            _multiScatterPass.Dispatch(pass, MultiScatterSize, MultiScatterSize);

            _staticTablesBuilt = true;
        }

        if (_dynamicBuilt && _last.LooksLike(conditions))
        {
            return false;
        }

        SkyParameters.Write(block, SkyViewWidth, SkyViewHeight, conditions);

        _skyViewPass.Bind("p", _skyViewBlock.Write<float>(frameInFlight, block));
        _skyViewPass.Dispatch(pass, SkyViewWidth, SkyViewHeight);

        // One work group of sixty-four, one direction each: the whole sky reduced to eight vectors.
        _irradiancePass.Bind("p", _irradianceBlock.Write<float>(frameInFlight, block));
        _irradiancePass.Dispatch(pass, 1, 1);

        _last = conditions;
        _dynamicBuilt = true;
        SkyViewBuilds++;

        return true;
    }

    /// <summary>Copies this frame's harmonics down to the host, for the game layer to read.</summary>
    /// <param name="encoder">The encoder to record the copy into, outside any pass.</param>
    /// <param name="frameInFlight">Which frame's buffer to copy into.</param>
    /// <remarks>
    /// <para>
    /// Recorded every frame whether or not the march ran, so each frame-in-flight slot holds that
    /// (unchanged) answer rather than one slot holding it and the others holding nothing.
    /// </para>
    /// <para>
    /// Except into a slot the host is still mapping: WebGPU refuses a submission that copies into a
    /// buffer with a map outstanding, and a browser's map finishes when it finishes. That slot keeps the
    /// answer it has, which for a sky that changes once a mission is the same answer.
    /// </para>
    /// </remarks>
    public void RecordReadback(IRhiCommandEncoder encoder, int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int slot = frameInFlight % _readback.Length;

        if (_pending[slot] is { IsComplete: false })
        {
            return;
        }

        encoder.CopyTextureToBuffer(
            new RhiTextureRegion(_irradiance),
            new RhiBufferTextureCopy(_readback[slot], 0, RhiBufferTextureCopy.AlignRow(IrradianceWidth * 16), 1),
            new RhiExtent3D(IrradianceWidth, 1));

        _pending[slot]?.Dispose();
        _pending[slot] = null;
        _slotBuild[slot] = SkyViewBuilds;
    }

    /// <summary>Gets which march a slot's copied harmonics were taken after.</summary>
    /// <param name="frameInFlight">The slot.</param>
    /// <returns>The <see cref="SkyViewBuilds"/> count when its copy was recorded.</returns>
    public int BuildOf(int frameInFlight) => _slotBuild[frameInFlight % _slotBuild.Length];

    /// <summary>Starts mapping a slot whose copy has just been submitted, so it can arrive before it is asked for.</summary>
    /// <param name="frameInFlight">The slot the submission just sent copied into.</param>
    /// <remarks>
    /// Requested as the frame is submitted rather than when the game asks, mid-way through the next: a
    /// map asked for then has only the ask itself to complete in, which either waits for it or answers
    /// no. Started a frame ahead, it has a whole frame, and the ask only has to look.
    /// </remarks>
    public void RequestReadback(int frameInFlight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int slot = frameInFlight % _readback.Length;
        _pending[slot] ??= _device.ReadBuffer(_readback[slot]);
    }

    /// <summary>Reads the harmonics a frame slot copied down, once they have arrived.</summary>
    /// <param name="frameInFlight">
    /// A frame-in-flight slot whose copy has been SUBMITTED. Mapping one whose copy is still only
    /// recorded is refused by WebGPU at the submit.
    /// </param>
    /// <param name="destination">Room for <see cref="IrradianceFloats"/> floats.</param>
    /// <returns><see langword="true"/> when that slot's harmonics were read.</returns>
    /// <remarks>
    /// A future rather than a value, because a browser cannot block for one. Asked for with no time to
    /// wait: a platform whose readback is synchronous answers at once, and one whose is not is given a
    /// chance to make progress and then reports whether it has arrived. A slot that has not reports
    /// nothing rather than waiting, and the caller keeps last frame's answer — which for a sky that
    /// changes once a mission is the same answer.
    /// </remarks>
    public bool TryReadIrradiance(int frameInFlight, Span<float> destination) =>
        TryReadIrradiance(frameInFlight, destination, TimeSpan.Zero);

    /// <summary>Reads a slot's harmonics, waiting for them up to a time.</summary>
    /// <param name="frameInFlight">A slot whose copy has been submitted.</param>
    /// <param name="destination">Room for <see cref="IrradianceFloats"/> floats.</param>
    /// <param name="wait">How long to wait for the copy to arrive.</param>
    /// <returns><see langword="true"/> when the harmonics were read.</returns>
    public bool TryReadIrradiance(int frameInFlight, Span<float> destination, TimeSpan wait)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.Length < IrradianceFloats)
        {
            return false;
        }

        int slot = frameInFlight % _readback.Length;
        IRhiReadback readback = _pending[slot] ??= _device.ReadBuffer(_readback[slot]);

        if (!readback.Wait(wait) && !readback.IsComplete)
        {
            return false;
        }

        ReadOnlySpan<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            readback.GetData());

        floats[..IrradianceFloats].CopyTo(destination);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiReadback? readback in _pending)
        {
            readback?.Dispose();
        }

        foreach (IRhiBuffer buffer in _readback)
        {
            buffer.Dispose();
        }

        _irradianceBlock.Dispose();
        _skyViewBlock.Dispose();
        _multiScatterBlock.Dispose();
        _transmittanceBlock.Dispose();

        _irradiancePass.Dispose();
        _skyViewPass.Dispose();
        _multiScatterPass.Dispose();
        _transmittancePass.Dispose();

        _sampler.Dispose();
        _irradiance.Dispose();
        _skyView.Dispose();
        _multiScatter.Dispose();
        _transmittance.Dispose();
    }

    /// <summary>What every table a pass writes is: storage to write, sampled to read, copied to read back.</summary>
    private IRhiTexture Table(int width, int height, RhiTextureFormat format, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            format,
            RhiTextureUsage.Storage | RhiTextureUsage.Sampled | RhiTextureUsage.CopySource,
            Label: label));

    /// <summary>Points each pass at the tables it reads and the one it writes, once.</summary>
    /// <remarks>
    /// The tables never change place — they are a fixed size, unlike everything sized from the frame —
    /// so each pass binds once here and builds one group that lasts the renderer's life.
    /// </remarks>
    private void Bind()
    {
        _transmittancePass.Bind("uTransmittance", _transmittance.View);

        _multiScatterPass.Bind("uTransmittanceLut", _transmittance.View, _sampler);
        _multiScatterPass.Bind("uMultiScatter", _multiScatter.View);

        _skyViewPass.Bind("uTransmittanceLut", _transmittance.View, _sampler);
        _skyViewPass.Bind("uMultiScatterLut", _multiScatter.View, _sampler);
        _skyViewPass.Bind("uSkyView", _skyView.View);

        _irradiancePass.Bind("uSkyView", _skyView.View, _sampler);
        _irradiancePass.Bind("uIrradiance", _irradiance.View);
    }
}
