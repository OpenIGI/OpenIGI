using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One stand of trees — I.G.I. 2's <c>Forest</c>: one model, an area, and a placement file listing every
/// instance of it.
/// </summary>
/// <remarks>
/// <para>
/// A forest is a single model drawn many times. <see cref="Model"/> names it, the placement comes from
/// <c>MISSION:forest_&lt;id&gt;.dat</c>, and <see cref="TreeCount"/> says how many of that file's records
/// to use. The engine builds a BSP tree over them at load time — <c>Forest_CreateBSPTree</c>
/// (<c>igi2.exe 0x59F350</c>) — which is what line-of-sight and tracing queries run against, so a forest
/// is collidable geometry and not decoration.
/// </para>
/// <para>
/// <b>Nine of the eighteen parameters are editor tools.</b> The three <c>PushButton</c>s do not hold a
/// value at all: they are the buttons that randomise the area, rebuild the placement and rebake the
/// light. <c>Brush size</c> and <c>Brush draw/delete</c> are the painting tool, and the four
/// <c>Random … range</c> values are what that tool draws with — they shape the file when it is written
/// and are not consulted again once it exists.
/// </para>
/// </remarks>
public sealed partial class ForestTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Forest";

    /// <summary>Gets or sets where the stand is centred.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the model every tree in the stand is an instance of.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets how far the stand reaches from <see cref="Position"/>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Area size")]
    public float AreaSize { get; set; }

    /// <summary>Gets or sets the editor's randomise-area button.</summary>
    /// <remarks>
    /// A <c>PushButton</c> carries no state — level data still declares one, so the class still holds one,
    /// and dropping it would shift every parameter after it.
    /// </remarks>
    [ScriptParam(ScriptParamType.PushButton, "Randomize area")]
    public bool RandomizeArea { get; set; }

    /// <summary>Gets or sets the editor's rebuild-placement button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Update")]
    public bool Update { get; set; }

    /// <summary>Gets or sets the editor's rebake-light button.</summary>
    /// <remarks>
    /// What <c>Forest_LightForest</c> (<c>0x59F800</c>) runs, filling each record's light from a hemicube
    /// gather. The result is in the placement file; nothing relights a forest during play.
    /// </remarks>
    [ScriptParam(ScriptParamType.PushButton, "Calculate light")]
    public bool CalculateLight { get; set; }

    /// <summary>Gets or sets how densely the editor scatters trees, per square metre.</summary>
    [ScriptParam(ScriptParamType.Real32, "Density of trees [1/m^2]")]
    public float Density { get; set; }

    /// <summary>Gets or sets how far a tree may be turned when the placement is generated.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random rotation range")]
    public float RandomRotationRange { get; set; }

    /// <summary>Gets or sets the scatter of the x scale.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random X-scale range")]
    public float RandomScaleRangeX { get; set; }

    /// <summary>Gets or sets the scatter of the y scale.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random Y-scale range ")]
    public float RandomScaleRangeY { get; set; }

    /// <summary>Gets or sets the scatter of the z scale.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random Z-scale range")]
    public float RandomScaleRangeZ { get; set; }

    /// <summary>Gets or sets whether one scatter value drives all three axes.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Isotropic scaling")]
    public bool IsotropicScaling { get; set; }

    /// <summary>Gets or sets how many of the placement file's records the stand uses.</summary>
    /// <remarks>
    /// The count lives here and not in the file. <c>Forest_LoadData</c> lowers it when the file holds
    /// fewer records, so this is an upper bound rather than a promise.
    /// </remarks>
    [ScriptParam(ScriptParamType.Int32, "Number of trees")]
    public int TreeCount { get; set; }

    /// <summary>Gets or sets the radius of the editor's painting brush, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Brush size (m)")]
    public float BrushSize { get; set; }

    /// <summary>Gets or sets whether that brush adds trees or removes them.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Brush draw/delete")]
    public bool BrushDeletes { get; set; }

    /// <summary>Gets or sets how far away the stand stops being drawn, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "View cutoff (m) (limited by object lod settings)")]
    public float ViewCutoff { get; set; }

    /// <summary>Gets or sets whether trees are dropped onto the terrain when the placement is generated.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Normalize objects to ground")]
    public bool NormalizeToGround { get; set; }

    /// <summary>Gets or sets how many of the model's levels of detail the wind animates.</summary>
    /// <remarks>The wind itself is a <see cref="WindTask"/>, one per level rather than one per stand.</remarks>
    [ScriptParam(ScriptParamType.Int32, "Number of LODs affected by the wind")]
    public int WindAffectedLods { get; set; }

    /// <summary>Gets the resource name of this stand's placement file.</summary>
    /// <remarks>
    /// <c>MISSION:forest_&lt;id&gt;.dat</c>, where the id is <see cref="GameTask.Id"/> — the slot this task
    /// holds in the level's 4096-entry id table. That is a different arrangement from terrain, where the
    /// tile id is a declared parameter, and it means a forest's data file is named by where the level
    /// happened to put the task.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The task holds no level id.</exception>
    public string PlacementResource
    {
        get
        {
            if (Id == NoId)
            {
                throw new InvalidOperationException(
                    "A Forest task with no level id has no placement file; the id is what names it.");
            }

            return string.Create(CultureInfo.InvariantCulture, $"MISSION:forest_{Id}.dat");
        }
    }
}
