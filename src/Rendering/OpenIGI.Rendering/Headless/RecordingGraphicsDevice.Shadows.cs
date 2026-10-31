using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The shadow settings each frame was given, in order.</summary>
    public List<ShadowSettings> Shadows { get; } = [];

    /// <summary>The cascade indices that were rendered, in order.</summary>
    public List<int> ShadowCascadesRendered { get; } = [];

    /// <summary>Set to make BeginShadowCascade refuse, as a backend with no shadow map would.</summary>
    public bool RefuseShadows { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// False unless a test says otherwise: this double rasterises nothing, so the cascades are the
    /// only sun shadow it could be said to have.
    /// </remarks>
    public bool SunShadowTraced { get; set; }

    /// <inheritdoc/>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld) => Record(Shadows, settings);

    /// <inheritdoc/>
    public bool BeginShadowCascade(int cascade)
    {
        if (RefuseShadows)
        {
            return false;
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        CurrentPass = ScenePass.Shadow;
        Record(ShadowCascadesRendered, cascade);
        return true;
    }

    /// <inheritdoc/>
    public void EndShadowCascade() => CurrentPass = ScenePass.None;
}
