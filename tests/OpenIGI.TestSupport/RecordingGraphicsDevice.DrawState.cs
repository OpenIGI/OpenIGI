using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    public Matrix4f Projection { get; private set; } = Matrix4f.Identity;

    public DepthState DepthState { get; private set; } = DepthState.Disabled;

    public CullMode CullMode { get; private set; } = CullMode.None;

    /// <summary>The material each draw was made under, most recent last.</summary>
    public List<SurfaceMaterial> Materials { get; } = [];

    /// <inheritdoc/>
    public void SetMaterialOverride(in SurfaceMaterial material) => Materials.Add(material);

    /// <summary>Gets the lightmap scale the draws that follow carry.</summary>
    public float LightmapScale { get; private set; } = 1f;

    /// <summary>Gets the model-space direction towards the fixed-function sun the draws that follow carry, or zero.</summary>
    public Vector3f VertexSun { get; private set; }

    /// <summary>The fixed-function sun's colours, each time they were set.</summary>
    public List<(Vector3f Ambient, Vector3f Colour)> SceneSuns { get; } = [];

    /// <inheritdoc/>
    public void SetLightmapScale(float scale) => LightmapScale = scale;

    /// <inheritdoc/>
    public void SetSceneSun(Vector3f ambient, Vector3f colour) => SceneSuns.Add((ambient, colour));

    /// <inheritdoc/>
    public void SetVertexSun(Vector3f towardsSun) => VertexSun = towardsSun;

    /// <summary>Gets the colour the draws that follow fog towards.</summary>
    public FogTarget FogTarget { get; private set; }

    /// <inheritdoc/>
    public void SetFogTarget(FogTarget target) => FogTarget = target;

    /// <inheritdoc/>
    public void SetAlphaHash(float seed)
    {
    }

    /// <summary>Gets the cut-out threshold the draws being recorded were made at.</summary>
    public byte AlphaReference { get; private set; } = IGraphicsDevice.DefaultAlphaReference;

    /// <summary>Every cut-out threshold set, in order, so a run of draws can be checked as a run.</summary>
    public List<byte> AlphaReferences { get; } = [];

    /// <inheritdoc/>
    public void SetAlphaReference(byte reference)
    {
        AlphaReference = reference;
        AlphaReferences.Add(reference);
    }

    /// <summary>Gets a value indicating whether the draws being recorded were marked first-person.</summary>
    public bool FirstPersonGeometry { get; private set; }

    /// <inheritdoc/>
    public void SetFirstPersonGeometry(bool firstPerson) => FirstPersonGeometry = firstPerson;

    /// <summary>Gets whether draws are currently marked as moving on their own.</summary>
    public bool ObjectMotion { get; private set; }

    /// <summary>The meshes drawn while <see cref="ObjectMotion"/> was set, in draw order.</summary>
    public List<MeshHandle> MovingDraws { get; } = [];

    /// <inheritdoc/>
    public void SetObjectMotion(bool moving, long identity = 0) => ObjectMotion = moving;

    /// <summary>How strongly the derived detail maps were last asked to apply.</summary>
    public float MaterialDetail { get; private set; }

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => MaterialDetail = strength;

    /// <inheritdoc/>
    public void SetFloorGloss(float gloss)
    {
    }

    /// <summary>The previous poses reported, in order; a null entry is a clear.</summary>
    public List<Matrix4f?> PreviousPoses { get; } = [];

    /// <summary>The pose blends reported, in order; a null entry is a clear.</summary>
    public List<(float Current, float Previous)?> PoseInterpolations { get; } = [];

    /// <inheritdoc/>
    public void SetPoseInterpolation(float currentBlend, float previousBlend) =>
        PoseInterpolations.Add((currentBlend, previousBlend));

    /// <inheritdoc/>
    public void ClearPoseInterpolation() => PoseInterpolations.Add(null);

    public void SetDepthState(DepthState state) => DepthState = state;

    public void SetCullMode(CullMode mode) => CullMode = mode;

    public void SetProjection(in Matrix4f projection) => Projection = projection;

    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha) =>
        DetailFade = (scale, offset, slope, fadeAlpha);

    public void ClearDetailFade() => DetailFade = null;

    public (float Scale, float Offset, float Slope, bool FadeAlpha)? DetailFade { get; private set; }

    public void SetFog(in SceneFog fog) => Fog = fog;

    /// <summary>Gets the fog the last <see cref="SetFog"/> asked for.</summary>
    public SceneFog Fog { get; private set; } = SceneFog.None;

    public void SetModelAmbient(Vector3f ambient) => ModelAmbient = ambient;

    /// <summary>Gets the flat model light the last <see cref="SetModelAmbient"/> asked for.</summary>
    public Vector3f ModelAmbient { get; private set; } = Vector3f.One;
}
