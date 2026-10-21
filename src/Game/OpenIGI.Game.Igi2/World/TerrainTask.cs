using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A level's terrain root — I.G.I. 2's <c>Terrain</c>, the task every <see cref="TerrainMapTask"/> and
/// <see cref="TerrainMaterialTask"/> hangs under.
/// </summary>
/// <remarks>
/// <para>
/// The fourteen parameters are the ones the retail levels declare, in that order; 22 of the 24 shipped
/// levels declare this type, so registering the class turns every one of them into a check on these
/// attributes. Each maps to a named field of <c>Terrain_t</c> in <c>igi2.exe</c>, which is what settles
/// what they mean:
/// </para>
/// <list type="table">
/// <item><term><c>Position</c></term><description><c>tPos</c>, the terrain's world origin.</description></item>
/// <item><term><c>World width</c>, <c>World height</c></term><description><c>vWorldWidth</c>, <c>vWorldHeight</c>.</description></item>
/// <item><term><c>Detail</c>, <c>Adaption</c></term><description><c>vDetail</c>, <c>vAdapt</c> — the level-of-detail controls.</description></item>
/// <item><term>everything else</term><description>editor state: the height brush, the shadow sampler, and the random generator.</description></item>
/// </list>
/// <para>
/// <b>Two thirds of the block is editor-only.</b> <c>vBrushSize</c>, <c>vBrushPower</c>, the four
/// <c>vGenerator*</c> fields and <c>nNumShadowSamples</c>/<c>vShadowSpread</c> drive the terrain editor's
/// brush and its lightmap bake; nothing reads them while a mission is being played. They are carried
/// anyway because the declaration carries them and the arity has to match — dropping one would silently
/// shift every parameter after it.
/// </para>
/// </remarks>
public sealed partial class TerrainTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Terrain";

    /// <summary>Gets or sets the terrain's world origin — <c>Terrain_t::tPos</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far the terrain spans along x — <c>vWorldWidth</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "World width")]
    public float WorldWidth { get; set; }

    /// <summary>Gets or sets how far the terrain spans along y — <c>vWorldHeight</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "World height")]
    public float WorldHeight { get; set; }

    /// <summary>Gets or sets the tessellation the renderer aims for — <c>vDetail</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Detail")]
    public float Detail { get; set; }

    /// <summary>Gets or sets how sharply detail falls off with distance — <c>vAdapt</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Adaption")]
    public float Adaption { get; set; }

    /// <summary>Gets or sets the editor height brush's radius — <c>vBrushSize</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Brush size")]
    public float BrushSize { get; set; }

    /// <summary>Gets or sets the editor height brush's strength — <c>vBrushPower</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Brush power")]
    public float BrushPower { get; set; }

    /// <summary>Gets or sets how many rays the lightmap bake casts per texel — <c>nNumShadowSamples</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Shadow samples")]
    public int ShadowSamples { get; set; }

    /// <summary>Gets or sets how widely those rays are spread — <c>vShadowSpread</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Shadow spread")]
    public float ShadowSpread { get; set; }

    /// <summary>Gets or sets the random generator's smallest brush — <c>vGeneratorMinSize</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Minimum brush size")]
    public float MinimumBrushSize { get; set; }

    /// <summary>Gets or sets the random generator's largest brush — <c>vGeneratorMaxSize</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Maximum brush size")]
    public float MaximumBrushSize { get; set; }

    /// <summary>Gets or sets the random generator's weakest brush — <c>vGeneratorMinPow</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Minimum brush power")]
    public float MinimumBrushPower { get; set; }

    /// <summary>Gets or sets the random generator's strongest brush — <c>vGeneratorMaxPow</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Maximum brush power")]
    public float MaximumBrushPower { get; set; }

    /// <summary>Gets or sets how many brushes the random generator applies — <c>nGeneratorNumBrushes</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Number of brushes")]
    public int BrushCount { get; set; }
}
