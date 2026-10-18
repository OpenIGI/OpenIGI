using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A body of water — I.G.I. 2's <c>Water</c>: one horizontal sheet, its surface material, and the
/// environment cube maps it reflects.
/// </summary>
/// <remarks>
/// <para>
/// Thirty parameters, and they divide sharply. The first dozen describe the water itself: where the
/// sheet sits, how big it is, how it is tessellated, and the optical properties
/// <c>WaterRender</c> shades it with — <see cref="AbsorptionLength"/> and
/// <see cref="MaxReflectionAngle"/> are a depth-absorption term and a Fresnel cutoff, not decoration.
/// The waves themselves are not here: they are the <see cref="WaterLayerTask"/> children, summed.
/// </para>
/// <para>
/// Everything from <see cref="Cubemap"/> onward is the environment map and how it was baked. The
/// camera position, orientation and field of view, and the five booleans naming which kinds of task to
/// draw, are the editor's capture settings — <c>Envmap_CubeMapRender</c> (<c>igi2ed 0x581930</c>) runs
/// six renders with them. They are recorded in the level so a map can be rebuilt, and are not consulted
/// while a mission is played; what is consulted is the baked result, named through
/// <see cref="ResourceList"/>.
/// </para>
/// </remarks>
public sealed partial class WaterTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Water";

    /// <summary>The alias the baked cube-map faces are resolved under.</summary>
    /// <remarks>
    /// <c>Water_LoadEnvmapData</c> (<c>igi2ed 0x646810</c>) tries this first, then a bare filename, then
    /// a <c>LOCAL:missions/location%d/level%d/envmaps/</c> path rebuilt from the mission number. On a
    /// retail install the first is enough: the faces arrive through <c>envmaps/water_&lt;id&gt;.res</c>.
    /// </remarks>
    public const string EnvmapDirectory = "MISSION:envmaps";

    /// <summary>How many faces a cube map has.</summary>
    public const int CubeMapFaceCount = 6;

    /// <summary>The most environment maps one water task can name.</summary>
    /// <remarks>
    /// The original parses <see cref="ResourceList"/> with a fixed <c>"%d %d %d %d %d"</c>, so five is a
    /// hard ceiling rather than a convention, and a sixth entry is silently dropped.
    /// </remarks>
    public const int MaximumEnvmaps = 5;

    /// <summary>Gets or sets where the sheet sits — <c>Water_t::tPos</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far the sheet reaches — <c>vRadius</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Size")]
    public float Size { get; set; } = 10f;

    /// <summary>Gets or sets how finely the sheet is tessellated — <c>vDetail</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Detail")]
    public float Detail { get; set; } = 0.2f;

    /// <summary>Gets or sets how transparent the surface is — <c>vAlpha</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Alpha")]
    public float Alpha { get; set; } = 1f;

    /// <summary>Gets or sets how far the surface texture is tiled — <c>vUVScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "UV Scale")]
    public float UvScale { get; set; } = 1f;

    /// <summary>Gets or sets how far the reflection is tiled — <c>vEnvScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Env Scale")]
    public float EnvScale { get; set; } = 0.5f;

    /// <summary>Gets or sets how far out the sheet fades — <c>vFadeOut</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fadeout")]
    public float FadeOut { get; set; } = 0.005f;

    /// <summary>Gets or sets the surface texture — <c>atTexture[0]</c>.</summary>
    [ScriptParam(ScriptParamType.String256, "Texture")]
    public string Texture { get; set; } = string.Empty;

    /// <summary>Gets or sets the reflection texture — <c>atTexture[1]</c>.</summary>
    /// <remarks>The still image used where no cube map applies; see <see cref="PlainEnvmapFile"/>.</remarks>
    [ScriptParam(ScriptParamType.String256, "Envmap")]
    public string Envmap { get; set; } = string.Empty;

    /// <summary>Gets or sets the water's own colour — <c>tDiffuseColor</c>.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Diffuse Color")]
    public Vector3f DiffuseColour { get; set; } = new(0.1f, 0.1f, 0f);

    /// <summary>Gets or sets the colour of its highlights — <c>tSpecularColor</c>.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Specular Color")]
    public Vector3f SpecularColour { get; set; } = new(0.1f, 0.1f, 0f);

    /// <summary>Gets or sets how far light travels into the water before it is absorbed, in metres.</summary>
    /// <remarks><c>vAbsorptionLength</c> — what makes shallow water read as shallow.</remarks>
    [ScriptParam(ScriptParamType.Real32, "Absorption length (depth) (m)")]
    public float AbsorptionLength { get; set; } = 3.5f;

    /// <summary>Gets or sets the cosine beyond which the surface stops reflecting — <c>vMaxReflectionAngle</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Reflection Angle (cos)")]
    public float MaxReflectionAngle { get; set; }

    /// <summary>Gets or sets whether the reflection is a cube map rather than a single image.</summary>
    /// <remarks>
    /// The branch in <c>Envmap_LoadFromMemory</c> (<c>igi2ed 0x581670</c>): set, it builds a cube texture
    /// and loads all six faces; clear, it builds one 2D texture from face zero alone.
    /// </remarks>
    [ScriptParam(ScriptParamType.Bool8, "Cubemap")]
    public bool Cubemap { get; set; }

    /// <summary>Gets or sets the still reflection image used when <see cref="Cubemap"/> is clear.</summary>
    [ScriptParam(ScriptParamType.String16, "Plain env. map file")]
    public string PlainEnvmapFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the edge length the environment map was baked at, in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Resolution of the environmental map")]
    public int EnvmapResolution { get; set; }

    /// <summary>Gets or sets where the bake camera stood.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Camera position")]
    public Vector3d CameraPosition { get; set; }

    /// <summary>Gets or sets which way the bake camera faced.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Camera orientation")]
    public Vector3f CameraOrientation { get; set; }

    /// <summary>Gets or sets the bake camera's field of view.</summary>
    [ScriptParam(ScriptParamType.Real32, "FOV")]
    public float FieldOfView { get; set; }

    /// <summary>Gets or sets whether the bake drew the sky.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Sky")]
    public bool BakeSky { get; set; }

    /// <summary>Gets or sets whether the bake drew the terrain.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Terrain")]
    public bool BakeTerrain { get; set; }

    /// <summary>Gets or sets whether the bake drew rigid objects.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Rigid objects")]
    public bool BakeRigidObjects { get; set; }

    /// <summary>Gets or sets whether the bake drew forests.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Forests")]
    public bool BakeForests { get; set; }

    /// <summary>Gets or sets whether the bake drew spline objects.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Spline objects")]
    public bool BakeSplineObjects { get; set; }

    /// <summary>Gets or sets the editor's rebake button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Rebuild env. map")]
    public bool RebuildEnvmap { get; set; }

    /// <summary>Gets or sets which environment map the editor's buttons act on — <c>iEnvmapToSave</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Environmental map number")]
    public int EnvmapNumber { get; set; }

    /// <summary>Gets or sets the editor's save button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Save env. map")]
    public bool SaveEnvmap { get; set; }

    /// <summary>Gets or sets the environment maps this water loads, as a space-separated list of indices.</summary>
    /// <remarks>
    /// Parsed with <c>sscanf(zResourceList, "%d %d %d %d %d")</c>, which is what caps it at
    /// <see cref="MaximumEnvmaps"/>. Each index names six face files; see
    /// <see cref="EnvmapIndices"/> and <see cref="CubeMapFaceResource"/>.
    /// </remarks>
    [ScriptParam(ScriptParamType.String16, "Resource list")]
    public string ResourceList { get; set; } = string.Empty;

    /// <summary>Gets or sets the editor's bundle-the-faces button.</summary>
    /// <remarks>What writes <c>envmaps/water_&lt;id&gt;.res</c>, the archive the faces ship in.</remarks>
    [ScriptParam(ScriptParamType.PushButton, "Make resource file")]
    public bool MakeResourceFile { get; set; }

    /// <summary>Gets or sets the expression choosing which loaded map is reflected.</summary>
    /// <remarks>
    /// A level expression rather than a constant, so a level can switch reflections as it runs — which
    /// is why a water task loads several maps rather than one.
    /// </remarks>
    [ScriptParam(ScriptParamType.VarString, "Environmental map index")]
    public string EnvmapIndexExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the body the level runs for it, or <see langword="null"/> when no level runs it.</summary>
    public Igi2WaterBody? Body { get; set; }

    /// <summary>
    /// Gets the slot of the environment maps <see cref="ResourceList"/> loads that the water reflects, as its
    /// <see cref="EnvmapIndexExpression"/> last said (<c>nEnvmapIndex</c>).
    /// </summary>
    public int EnvmapSlot { get; private set; }

    private OpenIGI.Scripting.Expressions.LevelExpression? _envmapIndex;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Water_RunHandler</c>: its ripples spread, its splashes rise and fall; its environment map index is evaluated, the
    /// value truncated; and its tick counts.
    /// </remarks>
    public override void Run(Flow.Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Body?.Run();
        _envmapIndex ??= Igi2LevelExpressions.Create(EnvmapIndexExpression, Name, level.Natives);
        if (!_envmapIndex.IsEmpty)
        {
            EnvmapSlot = (int)(long)level.Evaluate(_envmapIndex);
        }
    }

    /// <summary>Gets the environment map indices <see cref="ResourceList"/> names.</summary>
    /// <remarks>
    /// Stops at the first entry that is not a number, and never returns more than
    /// <see cref="MaximumEnvmaps"/>, because <c>sscanf</c> does both: it returns how many conversions
    /// succeeded, and the caller loops over exactly that many.
    /// </remarks>
    public IReadOnlyList<int> EnvmapIndices
    {
        get
        {
            List<int> indices = [];
            foreach (Range range in ResourceList.AsSpan().Split(' '))
            {
                ReadOnlySpan<char> token = ResourceList.AsSpan()[range].Trim();
                if (token.IsEmpty)
                {
                    continue;
                }

                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    break;
                }

                indices.Add(index);
                if (indices.Count == MaximumEnvmaps)
                {
                    break;
                }
            }

            return indices;
        }
    }

    /// <summary>Builds the resource name of one face of one of this water's environment maps.</summary>
    /// <param name="envmapIndex">One of <see cref="EnvmapIndices"/>.</param>
    /// <param name="face">The face, 0 to 5.</param>
    /// <returns>The alias-qualified resource name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The face is not one of the six.</exception>
    /// <exception cref="InvalidOperationException">The task holds no level id.</exception>
    /// <remarks>
    /// <c>cubemap_&lt;waterTaskId&gt;_&lt;index:000&gt;_&lt;face&gt;.bmp</c>. The task id is
    /// <see cref="GameTask.Id"/>, the same level-table slot that names a forest's placement file, so a
    /// water's baked maps are also named by where the level put the task rather than by a parameter.
    /// The face number is the <c>D3DCUBEMAP_FACES</c> index it is loaded into, in order: the original
    /// passes the array straight through, face <c>i</c> from file <c>i</c>.
    /// </remarks>
    public string CubeMapFaceResource(int envmapIndex, int face)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, CubeMapFaceCount);

        if (Id == NoId)
        {
            throw new InvalidOperationException(
                "A Water task with no level id has no environment maps; the id is what names them.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{EnvmapDirectory}/cubemap_{Id}_{envmapIndex:D3}_{face}.bmp");
    }
}
