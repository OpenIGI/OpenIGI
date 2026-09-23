namespace OpenIGI.Rendering.Pipeline;

/// <summary>The level's lamps: what the shading, the fog and the first-person rig are lit by besides the sun.</summary>
/// <remarks>
/// Held for one capture, as on the Vulkan device: the shading reads them, the fog scatters them, and the
/// forward pass lights the rig from them after the shading has run, so they are cleared only as the capture
/// ends -- a frame that submits none still lights from none.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly DeferredLocalLight[] _localLights = new DeferredLocalLight[DeferredLocalLight.Maximum];
    private int _localLightCount;

    /// <summary>Gets this frame's lamps.</summary>
    private ReadOnlySpan<DeferredLocalLight> LocalLights => _localLights.AsSpan(0, _localLightCount);

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        ThrowIfDisposed();

        _localLightCount = Math.Min(lights.Length, DeferredLocalLight.Maximum);
        lights[.._localLightCount].CopyTo(_localLights);
    }

    /// <summary>What the frame's lamps do, on the Vulkan device's terms.</summary>
    /// <remarks>
    /// The screen march is on wherever lamp shadows are asked for at all: it needs no ray query, and it is
    /// what keeps a lamp from lighting through a wall. The traced visibility is not on this device yet.
    /// </remarks>
    private DeferredLampState LampState() => new(
        _frameCounter,
        ScreenMarch: RenderCVars.LocalLightShadows.Value != 0,
        Traced: false,
        MaxSourceRadius: RenderCVars.LocalLightSource.Value);
}
