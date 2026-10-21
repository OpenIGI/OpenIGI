using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The editor's level notes, I.G.I. 2's <c>LevelInfo</c> (levelinfo.c): a task type with no handlers at all, whose buttons are the
/// editor's. The game loads it and does nothing with it.
/// </summary>
public sealed partial class Igi2LevelInfoTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LevelInfo";

    /// <summary>Gets or sets the editor's update button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Update info")]
    public bool UpdateInfo { get; set; }

    /// <summary>Gets or sets the editor's save button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Save mission models")]
    public bool SaveMissionModels { get; set; }

    /// <summary>Gets or sets where the editor saves the mission's models.</summary>
    [ScriptParam(ScriptParamType.String256, "Model path")]
    public string ModelPath { get; set; } = string.Empty;
}

/// <summary>
/// A sector's lightmap settings, I.G.I. 2's <c>LightmapInfo</c> (lightmapinfo.c): what the editor bakes the lightmaps under it with,
/// and the ambient light inside the sector object it sits in.
/// </summary>
/// <remarks>
/// <c>LightmapInfo_UpdateInternalDataHandler</c>: under a sector object, its indoors ambient light becomes that object's inside ambient
/// colour. The rest of the handler bakes, and runs only in the editor with lightmaps off; the game draws the bakes the level ships
/// (<see cref="Igi2LightmapIndex"/>).
/// </remarks>
public sealed partial class Igi2LightmapInfoTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LightmapInfo";

    /// <summary>Gets or sets the editor's hemicube resolution.</summary>
    [ScriptParam(ScriptParamType.Int32, "Hemicube resolution")]
    public int HemicubeResolution { get; set; } = 150;

    /// <summary>Gets or sets the ambient light inside its sector object.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Indoors ambient light")]
    public Vector3f IndoorsAmbient { get; set; }

    /// <summary>Gets or sets the name its bakes are saved under.</summary>
    [ScriptParam(ScriptParamType.String16, "Filename")]
    public string Filename { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override void OnParsed()
    {
        if (Parent is Igi2BuildingTask building)
        {
            building.InsideAmbient = IndoorsAmbient;
        }
    }
}
