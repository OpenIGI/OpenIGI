using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The player's tuning, I.G.I. 2's <c>HumanPlayerConfigItem</c>: one task in
/// <c>HUMANPLAYER/humanplayer.qvm</c> carrying the jump and air-control speeds, the peek, the damage
/// multipliers and the two scripts that define the weapon cycle and the ammo limits.
/// </summary>
/// <remarks>
/// <para>
/// Declared by <c>HumanPlayerConfig_Open</c> (<c>igi2 0x438570</c>), whose parameter list is the order below.
/// The task itself does nothing each tick; its <c>UpdateInternalData</c> handler (<c>0x438540</c>) runs the
/// two scripts and hands the numbers to <c>HumanPlayer_UpdateParameters</c> (<c>0x4BD1F0</c>), which is
/// <see cref="Igi2HumanPlayerGeneral.From"/> here.
/// </para>
/// <para>
/// Unlike I.G.I. 1, whose <c>humanplayer.qsc</c> calls natives, the sequel's tuning is a task; the speeds
/// are written in km/h and the lengths in metres and converted to world units on the way in.
/// </para>
/// </remarks>
public sealed partial class HumanPlayerConfigItemTask : GameTask
{
    /// <summary>The script name the player script instantiates this type under.</summary>
    public const string TypeName = "HumanPlayerConfigItem";

    /// <summary>Gets or sets the scale on the animation-driven translation, <c>vDeltaTranslationScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Movement speed scale")]
    public float MovementSpeedScale { get; set; }

    /// <summary>Gets or sets the forward speed a jump adds, km/h.</summary>
    [ScriptParam(ScriptParamType.Real32, "Forward jump speed (km/h)")]
    public float ForwardJumpSpeed { get; set; }

    /// <summary>Gets or sets the upward speed a jump starts with, km/h.</summary>
    [ScriptParam(ScriptParamType.Real32, "Upward jump speed (km/h)")]
    public float UpwardJumpSpeed { get; set; }

    /// <summary>Gets or sets the speed each held direction adds per tick in the air, km/h.</summary>
    [ScriptParam(ScriptParamType.Real32, "In air control speed (km/h)")]
    public float InAirControlSpeed { get; set; }

    /// <summary>Gets or sets the base speed of a thrown item, km/h.</summary>
    /// <remarks>Declared fifth but stored last (<c>+0xD4</c>); <c>HumanPlayerConfigItem_CreateHandler</c> (<c>0x438450</c>) defaults it to 15.</remarks>
    [ScriptParam(ScriptParamType.Real32, "Throwing base velocity (km/h)")]
    public float ThrowingBaseVelocity { get; set; } = 15f;

    /// <summary>Gets or sets how far a peek leans sideways, metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Peek length right/left (m)")]
    public float PeekLengthSideways { get; set; }

    /// <summary>Gets or sets how far a peek rises from a crouch, metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Peek length up from crouch (m)")]
    public float PeekLengthUp { get; set; }

    /// <summary>Gets or sets how long a peek takes to reach its length, seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time to reach peek length (s)")]
    public float PeekTime { get; set; }

    /// <summary>Gets or sets the scale on the player's maximum damage.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max damage scale")]
    public float MaxDamageScale { get; set; }

    /// <summary>Gets or sets the scale on an electric fence's damage.</summary>
    [ScriptParam(ScriptParamType.Real32, "Electric fence damage scale")]
    public float ElectricFenceDamageScale { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right foot.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right foot damage multiplier SINGLEPLAYER")]
    public float RightFootDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right lower leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right lower leg damage multiplier SINGLEPLAYER")]
    public float RightLowerLegDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right upper leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right upper leg damage multiplier SINGLEPLAYER")]
    public float RightUpperLegDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right hand.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right hand damage multiplier SINGLEPLAYER")]
    public float RightHandDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right lower arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right lower arm damage multiplier SINGLEPLAYER")]
    public float RightLowerArmDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the right upper arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right upper arm damage multiplier SINGLEPLAYER")]
    public float RightUpperArmDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left foot.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left foot damage multiplier SINGLEPLAYER")]
    public float LeftFootDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left lower leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left lower leg damage multiplier SINGLEPLAYER")]
    public float LeftLowerLegDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left upper leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left upper leg damage multiplier SINGLEPLAYER")]
    public float LeftUpperLegDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left hand.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left hand damage multiplier SINGLEPLAYER")]
    public float LeftHandDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left lower arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left lower arm damage multiplier SINGLEPLAYER")]
    public float LeftLowerArmDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the left upper arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left upper arm damage multiplier SINGLEPLAYER")]
    public float LeftUpperArmDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the upper body.</summary>
    [ScriptParam(ScriptParamType.Real32, "Upperbody damage multiplier SINGLEPLAYER")]
    public float UpperBodyDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the chest.</summary>
    [ScriptParam(ScriptParamType.Real32, "Chest damage multiplier SINGLEPLAYER")]
    public float ChestDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the back.</summary>
    [ScriptParam(ScriptParamType.Real32, "Back damage multiplier SINGLEPLAYER")]
    public float BackDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the groin.</summary>
    [ScriptParam(ScriptParamType.Real32, "Groin damage multiplier SINGLEPLAYER")]
    public float GroinDamageMultiplier { get; set; }

    /// <summary>Gets or sets the single-player damage multiplier on the head.</summary>
    [ScriptParam(ScriptParamType.Real32, "Head damage multiplier SINGLEPLAYER")]
    public float HeadDamageMultiplier { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right foot.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right foot damage multiplier MULTIPLAYER")]
    public float RightFootDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right lower leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right lower leg damage multiplier MULTIPLAYER")]
    public float RightLowerLegDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right upper leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right upper leg damage multiplier MULTIPLAYER")]
    public float RightUpperLegDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right hand.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right hand damage multiplier MULTIPLAYER")]
    public float RightHandDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right lower arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right lower arm damage multiplier MULTIPLAYER")]
    public float RightLowerArmDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the right upper arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Right upper arm damage multiplier MULTIPLAYER")]
    public float RightUpperArmDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left foot.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left foot damage multiplier MULTIPLAYER")]
    public float LeftFootDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left lower leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left lower leg damage multiplier MULTIPLAYER")]
    public float LeftLowerLegDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left upper leg.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left upper leg damage multiplier MULTIPLAYER")]
    public float LeftUpperLegDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left hand.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left hand damage multiplier MULTIPLAYER")]
    public float LeftHandDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left lower arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left lower arm damage multiplier MULTIPLAYER")]
    public float LeftLowerArmDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the left upper arm.</summary>
    [ScriptParam(ScriptParamType.Real32, "Left upper arm damage multiplier MULTIPLAYER")]
    public float LeftUpperArmDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the upper body.</summary>
    [ScriptParam(ScriptParamType.Real32, "Upperbody damage multiplier MULTIPLAYER")]
    public float UpperBodyDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the chest.</summary>
    [ScriptParam(ScriptParamType.Real32, "Chest damage multiplier MULTIPLAYER")]
    public float ChestDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the back.</summary>
    [ScriptParam(ScriptParamType.Real32, "Back damage multiplier MULTIPLAYER")]
    public float BackDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the groin.</summary>
    [ScriptParam(ScriptParamType.Real32, "Groin damage multiplier MULTIPLAYER")]
    public float GroinDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the multiplayer damage multiplier on the head.</summary>
    [ScriptParam(ScriptParamType.Real32, "Head damage multiplier MULTIPLAYER")]
    public float HeadDamageMultiplierMultiplayer { get; set; }

    /// <summary>Gets or sets the script that defines the order weapons cycle in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Define weapon cycle script")]
    public string WeaponCycleScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that defines how much of each ammunition the player can carry.</summary>
    [ScriptParam(ScriptParamType.VarString, "Define ammo limit script")]
    public string AmmoLimitScript { get; set; } = string.Empty;

    /// <summary>Gets the single-player damage multipliers, by <see cref="Igi2DamagePart"/>.</summary>
    /// <returns>A new array of seventeen multipliers.</returns>
    public float[] DamageMultipliers() =>
    [
        RightFootDamageMultiplier,
        RightLowerLegDamageMultiplier,
        RightUpperLegDamageMultiplier,
        RightHandDamageMultiplier,
        RightLowerArmDamageMultiplier,
        RightUpperArmDamageMultiplier,
        LeftFootDamageMultiplier,
        LeftLowerLegDamageMultiplier,
        LeftUpperLegDamageMultiplier,
        LeftHandDamageMultiplier,
        LeftLowerArmDamageMultiplier,
        LeftUpperArmDamageMultiplier,
        UpperBodyDamageMultiplier,
        ChestDamageMultiplier,
        BackDamageMultiplier,
        GroinDamageMultiplier,
        HeadDamageMultiplier,
    ];

    /// <summary>Gets the multiplayer damage multipliers, by <see cref="Igi2DamagePart"/>.</summary>
    /// <returns>A new array of seventeen multipliers.</returns>
    public float[] DamageMultipliersMultiplayer() =>
    [
        RightFootDamageMultiplierMultiplayer,
        RightLowerLegDamageMultiplierMultiplayer,
        RightUpperLegDamageMultiplierMultiplayer,
        RightHandDamageMultiplierMultiplayer,
        RightLowerArmDamageMultiplierMultiplayer,
        RightUpperArmDamageMultiplierMultiplayer,
        LeftFootDamageMultiplierMultiplayer,
        LeftLowerLegDamageMultiplierMultiplayer,
        LeftUpperLegDamageMultiplierMultiplayer,
        LeftHandDamageMultiplierMultiplayer,
        LeftLowerArmDamageMultiplierMultiplayer,
        LeftUpperArmDamageMultiplierMultiplayer,
        UpperBodyDamageMultiplierMultiplayer,
        ChestDamageMultiplierMultiplayer,
        BackDamageMultiplierMultiplayer,
        GroinDamageMultiplierMultiplayer,
        HeadDamageMultiplierMultiplayer,
    ];
}
