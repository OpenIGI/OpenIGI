using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The volumetric fog: a froxel volume lit once a frame and integrated into what each pixel sees through it.
/// </summary>
/// <remarks>
/// <para>
/// Two dispatches sharing one parameter block (<see cref="VolumetricFogParameters"/>): FogScatter lights each
/// froxel and blends it with the previous frame's, FogIntegrate accumulates the column front to back, and the
/// shading samples the result at the pixel's own screen position and depth.
/// </para>
/// <para>
/// <b>Three volumes.</b> The scattering volume is written this frame and read as history the next, so it
/// ping-pongs; the integrated volume is what the shading samples. Half floats throughout -- radiance and a
/// transmittance, neither of which wants more.
/// </para>
/// </remarks>
public sealed class VolumetricFog : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _scatter;
    private readonly ComputePass _integrate;
    private readonly ComputeParameters _parameters;
    private readonly IRhiSampler _sampler;
    private readonly StandInTextures _standIns;

    private readonly IRhiTexture?[] _scattering = new IRhiTexture?[2];
    private IRhiTexture? _integrated;
    private (int X, int Y, int Z) _grid;
    private int _current;
    private bool _hasHistory;
    private long _frameIndex;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VolumetricFog"/> class.</summary>
    /// <param name="device">The device the volumes live on.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where volumes a resize replaces go until the frames reading them finish.</param>
    public VolumetricFog(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _scatter = new ComputePass(device, ShaderModuleId.FogScatter, "fog.scatter");
        _integrate = new ComputePass(device, ShaderModuleId.FogIntegrate, "fog.integrate");
        _parameters = new ComputeParameters(device, VolumetricFogParameters.Floats * sizeof(float), framesInFlight, "fog");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "fog"));
        _standIns = new StandInTextures(device, "fog.standin");
    }

    /// <summary>Gets the integrated volume the shading samples, or null before one is built.</summary>
    public IRhiTextureView? Integrated => _integrated?.View;

    /// <summary>Gets the depth distribution the shading needs to find a pixel's slice.</summary>
    /// <remarks>
    /// <see cref="FogSliceDistribution.None"/> until a frame has been recorded, which is how the shading is
    /// told there is no fog to read.
    /// </remarks>
    public FogSliceDistribution Slices { get; private set; } = FogSliceDistribution.None;

    /// <summary>Forgets the accumulated history, for a camera that has moved somewhere unrelated.</summary>
    public void Invalidate() => _hasHistory = false;

    /// <summary>Sizes the volumes for a render resolution and a quality, rebuilding them if either moved.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <param name="slices">How many depth slices the quality dial asks for.</param>
    /// <remarks>
    /// Volumes a change replaces are retired rather than freed: the frames still in flight name them.
    /// </remarks>
    public void Ensure(int width, int height, int slices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        (int X, int Y, int Z) grid = VolumetricFogParameters.Grid(width, height, slices);
        if (_integrated is not null && grid == _grid)
        {
            return;
        }

        Release();
        _grid = grid;

        for (int i = 0; i < _scattering.Length; i++)
        {
            _scattering[i] = CreateVolume($"fog.scattering.{i}");
        }

        _integrated = CreateVolume("fog.integrated");
    }

    /// <summary>Lights the grid and integrates it, for one frame.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which set of per-frame resources this frame may write.</param>
    /// <param name="fog">The medium and its lights.</param>
    /// <param name="camera">Where the camera is and was.</param>
    /// <param name="shadowMap">The cascades' array view, or null with a cascade count of nought.</param>
    /// <param name="skyIrradiance">The sky's harmonics.</param>
    /// <param name="cascadeMatrices">Each cascade's camera-relative world to light clip.</param>
    /// <param name="cascadeSplits">The view depth each cascade stops at.</param>
    /// <param name="cascadeCount">How many cascades hold anything this frame.</param>
    /// <param name="shelterMap">The roofs seen from above, or null when the level has none.</param>
    /// <param name="shelter">How to read that map, or <see cref="ShelterMapTransform.None"/>.</param>
    /// <remarks>
    /// Both dispatches go in one compute pass: WebGPU orders a dispatch's writes before the next one's
    /// reads within a pass, and the Vulkan backend places the barrier between them.
    /// </remarks>
    public void Record(
        IRhiComputePassEncoder pass,
        int frameInFlight,
        in VolumetricFogFrame fog,
        in VolumetricFogCamera camera,
        IRhiTextureView? shadowMap,
        IRhiTextureView skyIrradiance,
        ReadOnlySpan<Matrix4f> cascadeMatrices,
        ReadOnlySpan<float> cascadeSplits,
        int cascadeCount,
        IRhiTextureView? shelterMap,
        in ShelterMapTransform shelter)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_integrated is not { } integrated || _scattering[0] is null || _scattering[1] is null)
        {
            return;
        }

        // A camera that jumped did not travel; nothing it accumulated describes where it is now.
        if (camera.CameraDelta.LengthSquared
            > VolumetricFogParameters.TeleportMetres * VolumetricFogParameters.TeleportMetres)
        {
            _hasHistory = false;
        }

        IRhiTexture target = _scattering[_current]!;
        IRhiTexture history = _scattering[_current ^ 1]!;

        Span<float> block = stackalloc float[VolumetricFogParameters.Floats];
        Slices = VolumetricFogParameters.Write(
            block,
            fog,
            camera,
            _grid,
            cascadeMatrices,
            cascadeSplits,
            cascadeCount,
            shelter,
            _frameIndex,
            _hasHistory);

        IRhiBuffer parameters = _parameters.Write<float>(frameInFlight, block);

        _scatter.Bind("uShadowMap", shadowMap ?? StandIn("uShadowMap"));
        _scatter.Bind("uSkyIrradiance", skyIrradiance);
        _scatter.Bind("uHistory", history.View, _sampler);
        _scatter.Bind("uScattering", target.View);
        _scatter.Bind("uShelterMap", shelterMap ?? StandIn("uShelterMap"));
        _scatter.Bind("p", parameters);
        _scatter.Dispatch(pass, _grid.X, _grid.Y, _grid.Z);

        _integrate.Bind("uScattering", target.View);
        _integrate.Bind("uIntegrated", integrated.View);
        _integrate.Bind("p", parameters);
        _integrate.Dispatch(pass, _grid.X, _grid.Y);

        _current ^= 1;
        _hasHistory = true;
        _frameIndex++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiTexture? volume in _scattering)
        {
            volume?.Dispose();
        }

        _integrated?.Dispose();
        _standIns.Dispose();
        _sampler.Dispose();
        _parameters.Dispose();
        _integrate.Dispose();
        _scatter.Dispose();
    }

    // A one-texel texture of the shape the scatter declares for a slot, for what this frame has none of.
    private IRhiTextureView StandIn(string name) => _standIns.For(
        _scatter.Program.Reflection.Bindings.First(binding => string.Equals(binding.Name, name, StringComparison.Ordinal)));

    private IRhiTexture CreateVolume(string label) => _device.CreateTexture(new RhiTextureDescriptor(
        _grid.X,
        _grid.Y,
        RhiTextureFormat.Rgba16Float,
        RhiTextureUsage.Storage | RhiTextureUsage.Sampled,
        DepthOrArrayLayers: _grid.Z,
        Dimension: RhiTextureViewDimension.Texture3D,
        Label: label));

    private void Release()
    {
        for (int i = 0; i < _scattering.Length; i++)
        {
            if (_scattering[i] is { } volume)
            {
                _retired.Retire(volume);
                _scattering[i] = null;
            }
        }

        if (_integrated is { } integrated)
        {
            _retired.Retire(integrated);
            _integrated = null;
        }

        _hasHistory = false;
        Slices = FogSliceDistribution.None;
    }
}
