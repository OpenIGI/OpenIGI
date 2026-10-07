using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player the level places, with the parameters I.G.I. 2 adds to the first game's four.</summary>
/// <remarks>
/// <para>
/// Mission 1's <c>objects.qvm</c> declares <c>HumanPlayer</c> with ten parameters: the first game's <c>Position</c>,
/// <c>Gamma</c>, <c>Model</c> and <c>Team</c>, then the script that fills the player's inventory, the first-person model,
/// the visibility triple and whether weapons carry over from the previous mission. It stays a
/// <see cref="HumanPlayerTask"/>, which is what finds the level's spawn, and binds all ten.
/// </para>
/// <para>
/// The <c>Weapon</c> script is evaluated once the level is loaded (<see cref="Weapons.Igi2HumanWeapons"/>).
/// </para>
/// </remarks>
public sealed partial class Igi2HumanPlayerTask : HumanPlayerTask
{
    /// <summary>Gets or sets the position the level places the player at.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d LevelPosition { get => SpawnPosition; set => SpawnPosition = value; }

    /// <summary>Gets or sets the heading the player starts facing.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float LevelGamma { get => SpawnGamma; set => SpawnGamma = value; }

    /// <summary>Gets or sets the player's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string LevelModel { get => PlayerModel; set => PlayerModel = value; }

    /// <summary>Gets or sets the player's team.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int LevelTeam { get => PlayerTeam; set => PlayerTeam = value; }

    /// <summary>Gets or sets the script that fills the player's inventory.</summary>
    [ScriptParam(ScriptParamType.VarString, "Weapon")]
    public string WeaponScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the model the first-person arms are drawn with.</summary>
    [ScriptParam(ScriptParamType.String16, "1st Person Model")]
    public string FirstPersonModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Visibility Gamma</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Visibility Gamma")]
    public float VisibilityGamma { get; set; }

    /// <summary>Gets or sets the <c>Visibility Minimum</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Visibility Minimum")]
    public float VisibilityMinimum { get; set; }

    /// <summary>Gets or sets the <c>Visibility Maximum</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Visibility Maximum")]
    public float VisibilityMaximum { get; set; }

    /// <summary>Gets or sets a value indicating whether the player keeps the previous mission's weapons.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Carry over weapons from previous mission")]
    public bool CarryOverWeapons { get; set; }
}
