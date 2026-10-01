using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// The container every <see cref="HumanAiConfigItemTask"/> hangs off — the original's
/// <c>HumanAIConfig</c>.
/// </summary>
/// <remarks>
/// Registered by <c>sub_406450</c> at 384 bytes with <b>no script parameters</b>. All it holds is the
/// 29 × 3 table of item pointers at <c>+36</c>, which <c>sub_406DF0</c> clears with a
/// <c>memset(config + 36, 0, 0x15C)</c> — 348 bytes, exactly 87 pointers, which is what fixes the
/// table's shape without reading a single index.
/// </remarks>
public partial class HumanAiConfigTask : GameTask
{
    /// <summary>Gets or sets the editor's save button — container <c>+32</c>.</summary>
    /// <remarks>
    /// The one parameter <c>sub_406450</c> places on the container, and a <c>PushButton</c>: an editor
    /// affordance that writes the table back out, carried as a boolean because that is what
    /// <c>PushButton</c> derives from. The shipped script declares it and never passes a value.
    /// </remarks>
    [ScriptParam(ScriptParamType.PushButton, "Save all settings")]
    public bool SaveAllSettings { get; set; }
}

/// <summary>
/// One <c>(AI type, difficulty)</c> row of <c>common/ai/settings.qsc</c> — the original's
/// <c>HumanAIConfigItem</c>, 680 bytes and thirty-four script parameters.
/// </summary>
/// <remarks>
/// <para>
/// The order below is <b>wire order</b>: the order <c>sub_406450</c> registers the fields in, which is
/// the order the script's own <c>Task_DeclareParameters</c> declares them in. The two orders were
/// checked against each other line for line.
/// </para>
/// <para>
/// The defaults are the ones the item constructor <c>sub_406B70</c> writes, so an item the script never
/// declares is still a usable row. Retail declares all 87, so none of them is reached by the shipped
/// data — they are here because the original has them.
/// </para>
/// </remarks>
public partial class HumanAiConfigItemTask : GameTask
{
    /// <summary>Gets or sets the <c>AITYPE_*</c> name this row configures — item <c>+296</c>.</summary>
    [ScriptParam(ScriptParamType.String256, "AI Type")]
    public string AiTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>GD_n</c> difficulty name this row configures — item <c>+40</c>.</summary>
    [ScriptParam(ScriptParamType.String256, "Difficulty")]
    public string DifficultyName { get; set; } = string.Empty;

    /// <summary>Gets or sets the owner's maximum health — item <c>+672</c>, range 0…10.</summary>
    /// <remarks>
    /// Named "Damage scale" by the script, but <c>sub_406F70</c> copies it straight to
    /// <c>Human+600</c>, which is the threshold the damage handler compares accumulated damage against.
    /// </remarks>
    [ScriptParam(ScriptParamType.Real32, "Damage scale")]
    public float DamageScale { get; set; } = 1.0f;

    /// <summary>Gets or sets how much damage it takes to make the owner flinch — item <c>+676</c>.</summary>
    /// <remarks>Copied to <c>Human+3332</c>, and only for a <c>HumanSoldier</c>.</remarks>
    [ScriptParam(ScriptParamType.Real32, "Recoil threshold")]
    public float RecoilThreshold { get; set; } = 0.33f;

    /// <summary>Gets or sets the delay before the first move of a fight, in seconds — item <c>+556</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat initial movement delay (in seconds)")]
    public float InitialMovementDelaySeconds { get; set; } = 0.33f;

    /// <summary>Gets or sets the delay between moves in a fight, in seconds — item <c>+560</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat movement delay (in seconds)")]
    public float MovementDelaySeconds { get; set; } = 1.0f;

    /// <summary>Gets or sets how long the owner keeps looking for a lost target — item <c>+616</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat targeting timeout (in seconds)")]
    public float TargetingTimeoutSeconds { get; set; } = 10.0f;

    /// <summary>Gets or sets the tracking distance in metres — item <c>+624</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat tracking distance (in meters)")]
    public float TrackingDistanceMetres { get; set; } = 15.0f;

    /// <summary>Gets or sets how close a target must be before the owner will lie down — item <c>+620</c>.</summary>
    /// <remarks>The script's own spelling of the parameter is <c>liedown mim distance</c>.</remarks>
    [ScriptParam(ScriptParamType.Real32, "Combat liedown mim distance (in meters)")]
    public float LieDownMinimumDistanceMetres { get; set; } = 15.0f;

    /// <summary>Gets or sets the chance of taking evasive action at all — item <c>+564</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat evasive action probability")]
    public float EvasiveActionProbability { get; set; } = 0.3f;

    /// <summary>Gets or sets the standing roll-versus-lie-down split — item <c>+568</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat evasive stand roll / liedown probability")]
    public float EvasiveStandRollOrLieDownProbability { get; set; } = 0.5f;

    /// <summary>Gets or sets the kneeling roll-versus-lie-down split — item <c>+572</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat evasive kneel roll / liedown probability")]
    public float EvasiveKneelRollOrLieDownProbability { get; set; } = 0.5f;

    /// <summary>Gets or sets which way a standing roll goes — item <c>+576</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat evasive stand roll side probability")]
    public float EvasiveStandRollSideProbability { get; set; } = 0.5f;

    /// <summary>Gets or sets which way a kneeling roll goes — item <c>+580</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat evasive kneel roll side probability")]
    public float EvasiveKneelRollSideProbability { get; set; } = 0.5f;

    /// <summary>Gets or sets the chance a round hits at point-blank range — item <c>+584</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing hit probability at minimum range")]
    public float HitProbabilityAtMinimumRange { get; set; } = 1.0f;

    /// <summary>Gets or sets the chance a round hits at the end of the view cone — item <c>+588</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing hit probability at maximum range")]
    public float HitProbabilityAtMaximumRange { get; set; } = 0.25f;

    /// <summary>Gets or sets the cap on a burst's length — item <c>+552</c>, the one <c>Int32</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Combat Firing Max Number Of Rounds per sequence")]
    public int MaximumRoundsPerSequence { get; set; } = 30;

    /// <summary>Gets or sets how fast the owner's aim converges on its target — item <c>+628</c>.</summary>
    /// <remarks>
    /// The per-tick rate at which the tracked aim point chases the target's real position in
    /// <c>0x48D8E0</c>: 1 lands on it in one tick, 0 never moves.
    /// </remarks>
    [ScriptParam(ScriptParamType.Real32, "Combat firing Aim precision")]
    public float AimPrecision { get; set; } = 0.2f;

    /// <summary>Gets or sets the smallest fraction of a clip a burst spends — item <c>+592</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing minimum clip percentage")]
    public float MinimumClipFraction { get; set; } = 0.1f;

    /// <summary>Gets or sets the random extra fraction of a clip a burst spends — item <c>+596</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing random clip percentage")]
    public float RandomClipFraction { get; set; } = 0.2f;

    /// <summary>Gets or sets the smallest gap between bursts, in seconds — item <c>+600</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing minimum delay (in seconds)")]
    public float MinimumFiringDelaySeconds { get; set; } = 1.5f;

    /// <summary>Gets or sets the random extra gap between bursts, in seconds — item <c>+604</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing random delay (in seconds)")]
    public float RandomFiringDelaySeconds { get; set; } = 1.0f;

    /// <summary>Gets or sets the smallest gap between single shots, in seconds — item <c>+608</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing minimum shot delay (in seconds)")]
    public float MinimumShotDelaySeconds { get; set; } = 0.1f;

    /// <summary>Gets or sets the random extra gap between single shots, in seconds — item <c>+612</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat firing random shot delay (in seconds)")]
    public float RandomShotDelaySeconds { get; set; }

    /// <summary>Gets or sets the chance of throwing a grenade — item <c>+632</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade throw probability")]
    public float GrenadeThrowProbability { get; set; } = 0.33f;

    /// <summary>Gets or sets the chance of throwing one instead of reloading — item <c>+636</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade throw when reload probability")]
    public float GrenadeThrowWhenReloadingProbability { get; set; } = 0.1f;

    /// <summary>Gets or sets the closest a grenade will be thrown, in metres — item <c>+640</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade minimum distance (in meters)")]
    public float GrenadeMinimumDistanceMetres { get; set; } = 10.0f;

    /// <summary>Gets or sets the furthest a grenade will be thrown, in metres — item <c>+644</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade maximum distance (in meters)")]
    public float GrenadeMaximumDistanceMetres { get; set; } = 60.0f;

    /// <summary>Gets or sets the smallest grenade flight time, in seconds — item <c>+648</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade minimum fly time (in seconds)")]
    public float GrenadeMinimumFlyTimeSeconds { get; set; } = 0.5f;

    /// <summary>Gets or sets the random extra grenade flight time, in seconds — item <c>+652</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade random fly time (in seconds)")]
    public float GrenadeRandomFlyTimeSeconds { get; set; } = 0.5f;

    /// <summary>Gets or sets the smallest grenade fuse, in seconds — item <c>+656</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade minimum explode time (in seconds)")]
    public float GrenadeMinimumExplodeTimeSeconds { get; set; } = 0.3f;

    /// <summary>Gets or sets the random extra grenade fuse, in seconds — item <c>+660</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Combat grenade random explode time (in seconds)")]
    public float GrenadeRandomExplodeTimeSeconds { get; set; } = 0.7f;

    /// <summary>Gets or sets the chance a melee attack connects — item <c>+664</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Close Combat hit probability")]
    public float CloseCombatHitProbability { get; set; } = 0.4f;

    /// <summary>Gets or sets how much damage a melee attack deals — item <c>+668</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Close Combat damage")]
    public float CloseCombatDamage { get; set; } = 0.5f;

    /// <summary>Gets the resolved AI type, or <see langword="null"/> when the name is not one of the 29.</summary>
    public AiType? ResolvedType =>
        AiScriptApi.Constants.TryGetValue(AiTypeName, out int value) && Enum.IsDefined((AiType)value)
            ? (AiType)value
            : null;

    /// <summary>Gets the resolved difficulty, or <see langword="null"/> when the name is not one of the three.</summary>
    public AiDifficulty? ResolvedDifficulty => HumanAiConfigRegistry.ParseDifficulty(DifficultyName);
}
