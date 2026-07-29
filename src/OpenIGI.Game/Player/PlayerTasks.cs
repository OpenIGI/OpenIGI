using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Player;

/// <summary>
/// The base every animated character derives from — the original's <c>Human</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the original at <c>0x45E450</c> with an instance size of 3320 and a pool of 128. It
/// declares no parameters of its own; its subclasses do, and every one of them starts with the same
/// four (<see cref="HumanSoldierTask"/> and <see cref="HumanPlayerTask"/> both open
/// <c>Position, Gamma, Model, Team</c>).
/// </para>
/// </remarks>
public abstract class HumanTask : ContainerTask
{
    /// <summary>Gets or sets the world position, in engine units.</summary>
    /// <remarks>The original keeps this as three <see cref="double"/> at <c>Human+32</c>.</remarks>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the heading about the vertical axis, in radians.</summary>
    public float Gamma { get; set; }

    /// <summary>Gets or sets the pack instance name the character's model comes from.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the team, which decides who shoots at whom.</summary>
    /// <remarks>
    /// The original registers <c>HUMAN_TEAM_*</c> script constants alongside the class
    /// (<c>0x45E450</c>'s tail loop over <c>off_53F348</c>), so a script may name these rather than
    /// numbering them.
    /// </remarks>
    public int Team { get; set; }
}

/// <summary>The player's character — the original's <c>HumanPlayer</c>.</summary>
/// <remarks>
/// <para>
/// Registered at <c>0x40FE30</c>, 3568 bytes, pool 8. Note what its parameter list does <b>not</b>
/// carry: a skeleton. <see cref="HumanSoldierTask"/> declares <c>Bone Heirachy</c> and the player does
/// not, which matches the constructor hard-coding skeleton 0 (<c>0x410370</c> calls the bone-object
/// initialiser with a literal). The player's rig is not a level-data decision.
/// </para>
/// </remarks>
public partial class HumanPlayerTask : HumanTask
{
    /// <summary>Gets or sets the position the level places the player at.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d SpawnPosition { get => Position; set => Position = value; }

    /// <summary>Gets or sets the heading the player starts facing.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float SpawnGamma { get => Gamma; set => Gamma = value; }

    /// <summary>Gets or sets the player's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string PlayerModel { get => Model; set => Model = value; }

    /// <summary>Gets or sets the player's team.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int PlayerTeam { get => Team; set => Team = value; }
}

/// <summary>An AI-driven character — the original's <c>HumanSoldier</c>.</summary>
/// <remarks>
/// Its two extra parameters are the ones the player's list lacks: <b><c>Bone Heirachy</c> is the
/// skeleton id</b> and <c>Stand Animation</c> the clip it starts in. Both are read straight off the
/// task and handed to the bone object by <c>0x454F10</c>, which is what settles that a model does not
/// name its own skeleton (<c>animation.md</c> §7.3).
/// </remarks>
public partial class HumanSoldierTask : HumanTask
{
    /// <summary>Gets or sets the position the level places the soldier at.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d SpawnPosition { get => Position; set => Position = value; }

    /// <summary>Gets or sets the heading the soldier starts facing.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float SpawnGamma { get => Gamma; set => Gamma = value; }

    /// <summary>Gets or sets the soldier's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string SoldierModel { get => Model; set => Model = value; }

    /// <summary>Gets or sets the soldier's team.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int SoldierTeam { get => Team; set => Team = value; }

    /// <summary>Gets or sets the skeleton id — the <c>anims/&lt;id&gt;.iff</c> set this character uses.</summary>
    /// <remarks>The script spells the parameter <c>Bone Heirachy</c>, misspelling and all.</remarks>
    [ScriptParam(ScriptParamType.Int32, "Bone Heirachy")]
    public int BoneHierarchy { get; set; }

    /// <summary>Gets or sets the animation the soldier stands in.</summary>
    [ScriptParam(ScriptParamType.Int32, "Stand Animation")]
    public int StandAnimation { get; set; }
}

/// <summary>
/// The player's input source — the original's <c>HumanPlayerInput</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered at <c>0x45DB70</c>, 484 bytes. Its four parameters line up one for one with the fields
/// the update reads: <c>Record</c> is <c>+128</c>, <c>Playback</c> is <c>+129</c>, <c>File</c> names
/// the demo, and the expression is the script-bound condition at <c>+404</c>.
/// </para>
/// </remarks>
public partial class HumanPlayerInputTask : GameTask
{
    /// <summary>Gets or sets whether sampled frames are recorded to <see cref="File"/>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Record")]
    public bool Record { get; set; }

    /// <summary>Gets or sets whether frames are replayed from <see cref="File"/> instead of sampled.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Playback")]
    public bool Playback { get; set; }

    /// <summary>Gets or sets the demo file recorded to or replayed from.</summary>
    [ScriptParam(ScriptParamType.String256, "File")]
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that gates the map computer.</summary>
    [ScriptParam(ScriptParamType.VarString, "Mapcomputer on expression")]
    public string MapComputerOnExpression { get; set; } = string.Empty;

    /// <summary>Gets the frames this task has posted to its parent.</summary>
    public PlayerInputState State { get; } = new();
}
