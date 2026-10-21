using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The scene targets that were asked for, in order.</summary>
    public List<SceneTargetDescription> SceneCaptures { get; } = [];

    /// <summary>The passes that were opened, in order.</summary>
    public List<ScenePass> Passes { get; } = [];

    /// <summary>The settings each resolve was asked for, in order.</summary>
    public List<PostProcessSettings> Resolves { get; } = [];

    /// <summary>Whether a scene capture is open right now.</summary>
    public bool IsCapturing { get; private set; }

    /// <inheritdoc/>
    public ScenePass CurrentPass { get; private set; } = ScenePass.None;

    /// <summary>Set to make <see cref="BeginSceneCapture"/> refuse, as a backend without targets would.</summary>
    public bool RefuseSceneCapture { get; set; }

    /// <inheritdoc/>
    public bool BeginSceneCapture(in SceneTargetDescription description)
    {
        if (IsCapturing)
        {
            throw new GraphicsDeviceException("A scene capture is already in progress.");
        }

        if (RefuseSceneCapture)
        {
            return false;
        }

        SceneCaptures.Add(description);
        IsCapturing = true;
        return true;
    }

    /// <inheritdoc/>
    public TextureHandle SceneVelocity => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle SceneDepth => new(90_005);

    /// <inheritdoc/>
    public TextureHandle SceneNormal => new(90_006);

    /// <inheritdoc/>
    public TextureHandle SceneColour => new(90_007);

    /// <inheritdoc/>
    /// <remarks>One unless a test says otherwise: this double resolves nothing, so a colour written
    /// through it means what it says.</remarks>
    public float SceneExposure { get; set; } = 1f;

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        if (!IsCapturing)
        {
            throw new GraphicsDeviceException($"Cannot begin the {pass} pass outside a scene capture.");
        }

        // The front layer closes the geometry pass itself, as both real devices do.
        bool closesGeometry = pass == ScenePass.GlassLayer && CurrentPass == ScenePass.Geometry;

        if (CurrentPass != ScenePass.None && !closesGeometry)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        CurrentPass = pass;
        Passes.Add(pass);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        if (CurrentPass == ScenePass.None)
        {
            throw new GraphicsDeviceException("No pass is open.");
        }

        CurrentPass = ScenePass.None;
    }

    /// <inheritdoc/>
    public void EndSceneCapture()
    {
        if (!IsCapturing)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        IsCapturing = false;
    }

    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        if (IsCapturing)
        {
            throw new GraphicsDeviceException("The scene capture has not been ended.");
        }

        if (SceneCaptures.Count == 0)
        {
            throw new GraphicsDeviceException("Nothing has been captured this frame.");
        }

        Resolves.Add(post);
    }
}
