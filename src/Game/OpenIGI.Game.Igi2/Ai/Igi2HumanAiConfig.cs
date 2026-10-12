using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>The AI's tuning table, I.G.I. 2's <c>HumanAIConfig</c>: its items are the soldier types' base values and the difficulty scales.</summary>
public sealed partial class Igi2HumanAiConfigTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "HumanAIConfig";

    /// <summary>Gets or sets the editor's save button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Save all settings")]
    public bool SaveAllSettings { get; set; }
}

/// <summary>
/// One row of the AI's tuning, I.G.I. 2's <c>HumanAIConfigItem</c>: a soldier type's base values, or a difficulty's scale of
/// them (<c>HumanAIConfigValues_t</c>).
/// </summary>
/// <remarks>
/// Its defaults (<c>HumanAIConfigItem_CreateHandler</c>, <c>igi2 0x41D650</c>) are a base row's when it is made as one and a
/// scale's ones otherwise; reading it (<c>HumanAIConfigItem_UpdateInternalDataHandler</c>, <c>0x41DB70</c>) takes its type from
/// its <c>AI Type</c> as a script constant.
/// </remarks>
public sealed partial class Igi2HumanAiConfigItemTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "HumanAIConfigItem";

    /// <summary>The number of body parts a soldier is damaged by.</summary>
    public const int DamageParts = 17;

    private readonly float[] _damage = [.. Enumerable.Repeat(1f, DamageParts)];

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanAiConfigItemTask"/> class, as a scale row.</summary>
    public Igi2HumanAiConfigItemTask() => Reset(isBaseData: false);

    /// <summary>Gets or sets the soldier type or difficulty it is for, a script constant.</summary>
    [ScriptParam(ScriptParamType.String256, "AI Type")]
    public string AiTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it is a soldier type's base values rather than a difficulty's scale.</summary>
    [ScriptParam(ScriptParamType.Bool8, "isBaseData")]
    public bool IsBaseData { get; set; }

    /// <summary>Gets or sets the seconds a soldier stays in danger, <c>vDangerTimeout</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "DangerTimeout[0..100]")]
    public float DangerTimeout { get; set; }

    /// <summary>Gets or sets the seconds a soldier inquires, <c>vInquireTimeout</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "InquireTimeout[0..100]")]
    public float InquireTimeout { get; set; }

    /// <summary>Gets or sets how distance slows detection, <c>vDetectionTimeDistanceFactor</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "DetectionTimeDistanceFactor[0..100]")]
    public float DetectionTimeDistanceFactor { get; set; }

    /// <summary>Gets or sets the accuracy at short range, in per cent, <c>vMinRangeAccuracy</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MinRangeAccuracy[0..100]")]
    public float MinRangeAccuracy { get; set; }

    /// <summary>Gets or sets the accuracy at medium range, <c>vMedRangeAccuracy</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MedRangeAccuracy[0..100]")]
    public float MedRangeAccuracy { get; set; }

    /// <summary>Gets or sets the accuracy at long range, <c>vMaxRangeAccuracy</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MaxRangeAccuracy[0..100]")]
    public float MaxRangeAccuracy { get; set; }

    /// <summary>Gets or sets the medium range, in metres, <c>vAccuracyMedRange</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "AccuracyMedRange[0..]")]
    public float AccuracyMedRange { get; set; }

    /// <summary>Gets or sets the long range, in metres, <c>vAccuracyMaxRange</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "AccuracyMaxRange[0..]")]
    public float AccuracyMaxRange { get; set; }

    /// <summary>Gets or sets the animation speed, <c>vAnimSpeedFactor</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "AnimSpeed[0..1]")]
    public float AnimSpeedFactor { get; set; }

    /// <summary>Gets or sets the close combat damage, <c>vCloseCombatDamage</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "CloseCombatDamage[0..10]")]
    public float CloseCombatDamage { get; set; }

    /// <summary>Gets or sets the combat view's first cone, in degrees, <c>vCombatViewCone1Alpha</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "CombatView1Alpha[0..180]")]
    public float CombatViewCone1Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "CombatView1Length[0..500]")]
    public float CombatViewCone1Length { get; set; }

    /// <summary>Gets or sets the combat view's second cone, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "CombatView2Alpha[0..180]")]
    public float CombatViewCone2Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "CombatView2Length[0..500]")]
    public float CombatViewCone2Length { get; set; }

    /// <summary>Gets or sets the chance of evasive action, in per cent.</summary>
    [ScriptParam(ScriptParamType.Real32, "EvasiveActionProb[0..100]")]
    public float EvasiveActionProb { get; set; }

    /// <summary>Gets or sets a grenade's fuse, in seconds, <c>vGrenadeDetonationTimer</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "GrenadeTimer[0..100]")]
    public float GrenadeDetonationTimer { get; set; }

    /// <summary>Gets or sets the chance of reacting to a grenade landing, in per cent.</summary>
    [ScriptParam(ScriptParamType.Real32, "GrenadeReactionProb[0..100]")]
    public float GrenadeLandingReactionProb { get; set; }

    /// <summary>Gets or sets the chance of throwing a grenade, in per cent.</summary>
    [ScriptParam(ScriptParamType.Real32, "GrenadeThrowProb[0..100]")]
    public float GrenadeThrowProb { get; set; }

    /// <summary>Gets or sets the hit points, in per cent of the soldier's.</summary>
    [ScriptParam(ScriptParamType.Real32, "HitPoints[0..100]")]
    public float HitPoints { get; set; }

    /// <summary>Gets or sets the damage, in per cent, past which a hit makes the soldier flinch, <c>vHitRecoilThreshold</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "HitAnimThreshold[0..100]")]
    public float HitRecoilThreshold { get; set; }

    /// <summary>Gets or sets the seconds between idle animations, <c>vIdleAnimationFrequency</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "IdleAnimFreq[0..100]")]
    public float IdleAnimationFrequency { get; set; }

    /// <summary>Gets or sets the idle view's first cone, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "IdleView1Alpha[0..180]")]
    public float IdleViewCone1Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "IdleView1Length[0..500]")]
    public float IdleViewCone1Length { get; set; }

    /// <summary>Gets or sets the idle view's second cone, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "IdleView2Alpha[0..180]")]
    public float IdleViewCone2Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "IdleView2Length[0..500]")]
    public float IdleViewCone2Length { get; set; }

    /// <summary>Gets or sets the seconds between investigation shouts, <c>vInvestigateShoutFrequency</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "InvShoutFreq[0..100]")]
    public float InvestigateShoutFrequency { get; set; }

    /// <summary>Gets or sets the investigating view's first cone, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "InvView1Alpha[0..180]")]
    public float InvestigateViewCone1Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "InvView1Length[0..500]")]
    public float InvestigateViewCone1Length { get; set; }

    /// <summary>Gets or sets the investigating view's second cone, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "InvView2Alpha[0..180]")]
    public float InvestigateViewCone2Alpha { get; set; }

    /// <summary>Gets or sets its length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "InvView2Length[0..500]")]
    public float InvestigateViewCone2Length { get; set; }

    /// <summary>Gets or sets the seconds between shots, <c>vMinDelayBetweenFiring</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MinFireInterval[0..1]")]
    public float MinDelayBetweenFiring { get; set; }

    /// <summary>Gets or sets the share of a burst fired, in per cent, <c>vPercentageFiredPerBurst</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "PercentFirecPerBurst[0..100]")]
    public float PercentageFiredPerBurst { get; set; }

    /// <summary>Gets or sets how far a target is tracked, in metres, <c>vTrackingMaxDistance</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MaxTrackingDist[0..500]")]
    public float TrackingMaxDistance { get; set; }

    /// <summary>Gets or sets the seconds a target is tracked, <c>vTrackingTimeout</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "MaxTrackingTime[0..500]")]
    public float TrackingTimeout { get; set; }

    /// <summary>Gets or sets the seconds from detection to attack, <c>vDetectionToAttackTime</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "DetectionToAttackTime[0..100]")]
    public float DetectionToAttackTime { get; set; }

    /// <summary>Gets or sets the right foot's damage multiplier, <c>avDamageMultiplier[0]</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Foot [0..100]")]
    public float DamageRightFoot { get => _damage[0]; set => _damage[0] = value; }

    /// <summary>Gets or sets the right lower leg's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Lower Leg [0..100]")]
    public float DamageRightLowerLeg { get => _damage[1]; set => _damage[1] = value; }

    /// <summary>Gets or sets the right upper leg's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Upper Leg [0..100]")]
    public float DamageRightUpperLeg { get => _damage[2]; set => _damage[2] = value; }

    /// <summary>Gets or sets the right hand's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Hand [0..100]")]
    public float DamageRightHand { get => _damage[3]; set => _damage[3] = value; }

    /// <summary>Gets or sets the right lower arm's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Lower Arm [0..100]")]
    public float DamageRightLowerArm { get => _damage[4]; set => _damage[4] = value; }

    /// <summary>Gets or sets the right upper arm's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Right Upper Arm [0..100]")]
    public float DamageRightUpperArm { get => _damage[5]; set => _damage[5] = value; }

    /// <summary>Gets or sets the left foot's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Foot [0..100]")]
    public float DamageLeftFoot { get => _damage[6]; set => _damage[6] = value; }

    /// <summary>Gets or sets the left lower leg's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Lower Leg [0..100]")]
    public float DamageLeftLowerLeg { get => _damage[7]; set => _damage[7] = value; }

    /// <summary>Gets or sets the left upper leg's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Upper Leg [0..100]")]
    public float DamageLeftUpperLeg { get => _damage[8]; set => _damage[8] = value; }

    /// <summary>Gets or sets the left hand's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Hand [0..100]")]
    public float DamageLeftHand { get => _damage[9]; set => _damage[9] = value; }

    /// <summary>Gets or sets the left lower arm's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Lower Arm [0..100]")]
    public float DamageLeftLowerArm { get => _damage[10]; set => _damage[10] = value; }

    /// <summary>Gets or sets the left upper arm's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Left Upper Arm [0..100]")]
    public float DamageLeftUpperArm { get => _damage[11]; set => _damage[11] = value; }

    /// <summary>Gets or sets the upper body's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Upper Body [0..100]")]
    public float DamageUpperBody { get => _damage[12]; set => _damage[12] = value; }

    /// <summary>Gets or sets the chest's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Chest [0..100]")]
    public float DamageChest { get => _damage[13]; set => _damage[13] = value; }

    /// <summary>Gets or sets the back's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Back [0..100]")]
    public float DamageBack { get => _damage[14]; set => _damage[14] = value; }

    /// <summary>Gets or sets the groin's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Groin [0..100]")]
    public float DamageGroin { get => _damage[15]; set => _damage[15] = value; }

    /// <summary>Gets or sets the head's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damagemultiplier Head [0..100]")]
    public float DamageHead { get => _damage[16]; set => _damage[16] = value; }

    /// <summary>Gets the body parts' damage multipliers, <c>avDamageMultiplier</c>, right foot first.</summary>
    public IReadOnlyList<float> DamageMultipliers => _damage;

    /// <summary>Gets the soldier type or difficulty its <see cref="AiTypeName"/> names, <c>eAIType</c>.</summary>
    public int AiType { get; private set; }

    /// <summary>
    /// Resets it to the values <c>HumanAIConfigItem_CreateHandler</c> (<c>igi2 0x41D650</c>) gives a base row or a scale row.
    /// </summary>
    /// <param name="isBaseData">Whether it is made as a base row.</param>
    public void Reset(bool isBaseData)
    {
        float Pick(float base_) => isBaseData ? base_ : 1f;
        IsBaseData = isBaseData;
        AiType = 0;
        DangerTimeout = Pick(5f);
        InquireTimeout = Pick(5f);
        DetectionTimeDistanceFactor = Pick(0.1f);
        MinRangeAccuracy = Pick(50f);
        MedRangeAccuracy = Pick(10f);
        MaxRangeAccuracy = Pick(5f);
        AccuracyMedRange = Pick(50f);
        AccuracyMaxRange = Pick(100f);
        AnimSpeedFactor = 1f;
        CloseCombatDamage = 1f;
        CombatViewCone1Alpha = Pick(30f);
        CombatViewCone1Length = Pick(100f);
        CombatViewCone2Alpha = Pick(90f);
        CombatViewCone2Length = Pick(10f);
        EvasiveActionProb = Pick(30f);
        GrenadeDetonationTimer = Pick(4f);
        GrenadeLandingReactionProb = Pick(50f);
        GrenadeThrowProb = Pick(50f);
        HitPoints = Pick(100f);
        HitRecoilThreshold = Pick(10f);
        IdleAnimationFrequency = Pick(30f);
        IdleViewCone1Alpha = Pick(20f);
        IdleViewCone1Length = Pick(40f);
        IdleViewCone2Alpha = Pick(90f);
        IdleViewCone2Length = Pick(5f);
        InvestigateShoutFrequency = Pick(10f);
        InvestigateViewCone1Alpha = Pick(40f);
        InvestigateViewCone1Length = Pick(60f);
        InvestigateViewCone2Alpha = Pick(90f);
        InvestigateViewCone2Length = Pick(10f);
        MinDelayBetweenFiring = Pick(0.3f);
        PercentageFiredPerBurst = Pick(10f);
        TrackingMaxDistance = Pick(100f);
        TrackingTimeout = Pick(30f);
        DetectionToAttackTime = isBaseData ? 2f : 0.5f;
        Array.Fill(_damage, 1f);
    }

    /// <summary>Takes its type from its <see cref="AiTypeName"/>, as a one-line script.</summary>
    /// <param name="natives">The script namespace, with the AI's constants.</param>
    /// <param name="functions">The natives script text may call.</param>
    internal void ResolveType(NativeRegistry natives, Igi2ScriptFunctions functions) =>
        AiType = Igi2LineScript.Evaluate(AiTypeName, natives, functions);
}

/// <summary>
/// The AI's tuning for the session, read from <c>COMMON/ai/settings.qvm</c> (<c>HumanAIConfig_LoadSettings</c>,
/// <c>igi2 0x41E5B0</c>): each soldier type's base row and each difficulty's scale.
/// </summary>
/// <remarks>
/// It first names the difficulty scales <c>DIFF_SCALE_GD_1</c> to <c>_3</c>, then runs the script; each item it made is the base
/// row or the scale row of the type its <c>AI Type</c> names. A type the script gave no row has one made with the defaults.
/// </remarks>
public sealed class Igi2HumanAiConfig
{
    /// <summary>The script the tuning is read from.</summary>
    public const string Script = "LOCAL:common/ai/settings.qvm";

    /// <summary>The names of the difficulties a scale row is for, <c>Config_azGameDifficulty</c>, each after <c>DIFF_SCALE_</c>.</summary>
    public static IReadOnlyList<string> Difficulties { get; } = ["GD_1", "GD_2", "GD_3"];

    private readonly Igi2HumanAiConfigItemTask[] _base = new Igi2HumanAiConfigItemTask[Igi2AiConstants.AiTypes.Count];
    private readonly Igi2HumanAiConfigItemTask[] _scale = new Igi2HumanAiConfigItemTask[Difficulties.Count];

    /// <summary>Gets each soldier type's base row, by type.</summary>
    public IReadOnlyList<Igi2HumanAiConfigItemTask> BaseRows => _base;

    /// <summary>Gets each difficulty's scale row, by difficulty.</summary>
    public IReadOnlyList<Igi2HumanAiConfigItemTask> ScaleRows => _scale;

    /// <summary>Names the difficulty scales, runs the script and files its rows.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with the AI's constants.</param>
    /// <param name="functions">The natives script text may call.</param>
    /// <param name="tree">The task tree the table is declared into.</param>
    /// <exception cref="EngineFatalException">The script is missing or faults.</exception>
    public void Load(ResourceRegistry resources, NativeRegistry natives, Igi2ScriptFunctions functions, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(tree);
        for (int i = 0; i < Difficulties.Count; i++)
        {
            natives.RegisterConstant("DIFF_SCALE_" + Difficulties[i], i);
        }

        Igi2SessionScript.Run(resources, natives, Script);
        foreach (Igi2HumanAiConfigItemTask item in Rows(tree.Root))
        {
            item.ResolveType(natives, functions);
            Igi2HumanAiConfigItemTask[] table = item.IsBaseData ? _base : _scale;
            if ((uint)item.AiType < (uint)table.Length)
            {
                table[item.AiType] = item;
            }
        }

        Fill(_base, isBaseData: true);
        Fill(_scale, isBaseData: false);
    }

    /// <summary>
    /// A soldier's tuning at a difficulty, <c>HumanAIConfig_SetAISettingsFromConfig</c> (<c>igi2 0x41E8B0</c>): its type's base
    /// row times the difficulty's scale, in the units the AI uses.
    /// </summary>
    /// <param name="aiType">The soldier's type.</param>
    /// <param name="difficulty">The difficulty, nought to two.</param>
    /// <returns>The tuning, or <see langword="null"/> for a type or difficulty the table has no row for.</returns>
    public Igi2AiTweaks? Tweaks(int aiType, int difficulty)
    {
        if ((uint)aiType >= (uint)_base.Length || (uint)difficulty >= (uint)_scale.Length
            || _base[aiType] is not { } b || _scale[difficulty] is not { } s)
        {
            return null;
        }

        return Igi2AiTweaks.From(b, s);
    }

    private static IEnumerable<Igi2HumanAiConfigItemTask> Rows(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is Igi2HumanAiConfigItemTask item)
            {
                yield return item;
            }

            foreach (Igi2HumanAiConfigItemTask inner in Rows(child))
            {
                yield return inner;
            }
        }
    }

    private static void Fill(Igi2HumanAiConfigItemTask[] table, bool isBaseData)
    {
        for (int i = 0; i < table.Length; i++)
        {
            if (table[i] is null)
            {
                Igi2HumanAiConfigItemTask made = new();
                made.Reset(isBaseData);
                table[i] = made;
            }
        }
    }
}

/// <summary>
/// A soldier's tuning as its AI uses it, <c>HumanAI_s.tTweakParameters</c>: a base row times a difficulty's scale, the
/// percentages as shares, the metres as world units, the seconds as ticks and the degrees as radians.
/// </summary>
/// <param name="DangerTimeout">Seconds in danger.</param>
/// <param name="InquireTimeout">Seconds inquiring.</param>
/// <param name="DetectionTimeDistanceFactor">How distance slows detection, nought to one.</param>
/// <param name="PercentageFiredPerBurst">The share of a burst fired.</param>
/// <param name="EvasiveActionProb">The chance of evasive action.</param>
/// <param name="MinRangeAccuracy">The accuracy at short range, at least a hundredth.</param>
/// <param name="MedRangeAccuracy">At medium range, at least a hundredth.</param>
/// <param name="MaxRangeAccuracy">At long range, no less than nought.</param>
/// <param name="AccuracyMedRange">The medium range, in world units, at least one.</param>
/// <param name="AccuracyMaxRange">The long range, at least one.</param>
/// <param name="MedRangeAccuracyFactor">The accuracy's fall per unit to medium range.</param>
/// <param name="MaxRangeAccuracyFactor">Its fall per unit from medium to long range.</param>
/// <param name="MinDelayBetweenFiring">Ticks between shots.</param>
/// <param name="TrackingTimeout">Ticks a target is tracked.</param>
/// <param name="TrackingMaxDistance">How far, in world units.</param>
/// <param name="GrenadeThrowProb">The chance of throwing a grenade.</param>
/// <param name="GrenadeDetonationTimer">A grenade's fuse in ticks, the base row's alone.</param>
/// <param name="GrenadeLandingReactionProb">The chance of reacting to a grenade.</param>
/// <param name="CloseCombatDamage">Close combat damage.</param>
/// <param name="HitPoints">Hit points, as a share.</param>
/// <param name="HitRecoilThreshold">The share past which a hit makes the soldier flinch.</param>
/// <param name="CombatViewCone1Alpha">The combat view's first cone, in radians.</param>
/// <param name="CombatViewCone1Length">Its length, in world units.</param>
/// <param name="CombatViewCone2Alpha">The second cone.</param>
/// <param name="CombatViewCone2Length">Its length.</param>
/// <param name="IdleViewCone1Alpha">The idle view's first cone.</param>
/// <param name="IdleViewCone1Length">Its length.</param>
/// <param name="IdleViewCone2Alpha">The second cone.</param>
/// <param name="IdleViewCone2Length">Its length.</param>
/// <param name="IdleAnimationFrequency">Ticks between idle animations.</param>
/// <param name="InvestigateViewCone1Alpha">The investigating view's first cone.</param>
/// <param name="InvestigateViewCone1Length">Its length.</param>
/// <param name="InvestigateViewCone2Alpha">The second cone.</param>
/// <param name="InvestigateViewCone2Length">Its length.</param>
/// <param name="InvestigateShoutFrequency">Ticks between investigation shouts.</param>
/// <param name="AnimSpeedFactor">The animation speed, held to between 0.8 and 1.2.</param>
/// <param name="DetectionToAttackTime">Ticks from detection to attack.</param>
public sealed record Igi2AiTweaks(
    float DangerTimeout,
    float InquireTimeout,
    float DetectionTimeDistanceFactor,
    float PercentageFiredPerBurst,
    float EvasiveActionProb,
    float MinRangeAccuracy,
    float MedRangeAccuracy,
    float MaxRangeAccuracy,
    float AccuracyMedRange,
    float AccuracyMaxRange,
    float MedRangeAccuracyFactor,
    float MaxRangeAccuracyFactor,
    float MinDelayBetweenFiring,
    float TrackingTimeout,
    float TrackingMaxDistance,
    float GrenadeThrowProb,
    float GrenadeDetonationTimer,
    float GrenadeLandingReactionProb,
    float CloseCombatDamage,
    float HitPoints,
    float HitRecoilThreshold,
    float CombatViewCone1Alpha,
    float CombatViewCone1Length,
    float CombatViewCone2Alpha,
    float CombatViewCone2Length,
    float IdleViewCone1Alpha,
    float IdleViewCone1Length,
    float IdleViewCone2Alpha,
    float IdleViewCone2Length,
    float IdleAnimationFrequency,
    float InvestigateViewCone1Alpha,
    float InvestigateViewCone1Length,
    float InvestigateViewCone2Alpha,
    float InvestigateViewCone2Length,
    float InvestigateShoutFrequency,
    float AnimSpeedFactor,
    float DetectionToAttackTime)
{
    private const float Percent = 0.0099999998f;
    private const float Metre = 4096f;
    private const float Ticks = 30f;
    private const float Degree = 0.017453292f;

    /// <summary>The tuning from a base row and a scale row, <c>HumanAIConfig_SetAISettingsFromConfig</c> (<c>igi2 0x41E8B0</c>).</summary>
    /// <param name="b">The soldier type's base row.</param>
    /// <param name="s">The difficulty's scale row.</param>
    /// <returns>The tuning.</returns>
    /// <remarks>Each product in the order the original multiplies it; the accuracy factors are <c>_CIpow</c>'s.</remarks>
    public static Igi2AiTweaks From(Igi2HumanAiConfigItemTask b, Igi2HumanAiConfigItemTask s)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(s);
        static float Floor(Fpu value, float floor) => value.Value > floor ? value.F : floor;

        Fpu detection = (Fpu)s.DetectionTimeDistanceFactor * b.DetectionTimeDistanceFactor;
        float detectionFactor = detection.Value >= 0.0 ? (detection.Value > 1.0 ? 1f : detection.F) : 0f;
        float minAccuracy = Floor(((Fpu)s.MinRangeAccuracy * b.MinRangeAccuracy) * Percent, Percent);
        float medAccuracy = Floor(((Fpu)s.MedRangeAccuracy * b.MedRangeAccuracy) * Percent, Percent);
        float maxAccuracy = Floor(((Fpu)s.MaxRangeAccuracy * b.MaxRangeAccuracy) * Percent, 0f);
        float medRange = Floor(((Fpu)s.AccuracyMedRange * b.AccuracyMedRange) * Metre, 1f);
        float maxRange = Floor(((Fpu)s.AccuracyMaxRange * b.AccuracyMaxRange) * Metre, 1f);
        float medFactor = (float)Math.Pow(((Fpu)medAccuracy / minAccuracy).Value, ((Fpu)1f / medRange).Value);
        float maxFactor = (float)Math.Pow(((Fpu)maxAccuracy / medAccuracy).Value, ((Fpu)1f / ((Fpu)maxRange - medRange)).Value);
        Fpu anim = (Fpu)s.AnimSpeedFactor * b.AnimSpeedFactor;
        float animSpeed = anim.Value >= 0.80000001f ? (anim.Value > 1.2f ? 1.2f : anim.F) : 0.80000001f;

        return new Igi2AiTweaks(
            DangerTimeout: ((Fpu)s.DangerTimeout * b.DangerTimeout).F,
            InquireTimeout: ((Fpu)s.InquireTimeout * b.InquireTimeout).F,
            DetectionTimeDistanceFactor: detectionFactor,
            PercentageFiredPerBurst: (((Fpu)b.PercentageFiredPerBurst * Percent) * s.PercentageFiredPerBurst).F,
            EvasiveActionProb: (((Fpu)b.EvasiveActionProb * Percent) * s.EvasiveActionProb).F,
            MinRangeAccuracy: minAccuracy,
            MedRangeAccuracy: medAccuracy,
            MaxRangeAccuracy: maxAccuracy,
            AccuracyMedRange: medRange,
            AccuracyMaxRange: maxRange,
            MedRangeAccuracyFactor: medFactor,
            MaxRangeAccuracyFactor: maxFactor,
            MinDelayBetweenFiring: (((Fpu)s.MinDelayBetweenFiring * b.MinDelayBetweenFiring) * Ticks).F,
            TrackingTimeout: (((Fpu)s.TrackingTimeout * b.TrackingTimeout) * Ticks).F,
            TrackingMaxDistance: (((Fpu)s.TrackingMaxDistance * b.TrackingMaxDistance) * Metre).F,
            GrenadeThrowProb: (((Fpu)b.GrenadeThrowProb * Percent) * s.GrenadeThrowProb).F,
            GrenadeDetonationTimer: ((Fpu)b.GrenadeDetonationTimer * Ticks).F,
            GrenadeLandingReactionProb: (((Fpu)b.GrenadeLandingReactionProb * Percent) * s.GrenadeLandingReactionProb).F,
            CloseCombatDamage: ((Fpu)s.CloseCombatDamage * b.CloseCombatDamage).F,
            HitPoints: (((Fpu)b.HitPoints * Percent) * s.HitPoints).F,
            HitRecoilThreshold: (((Fpu)b.HitRecoilThreshold * Percent) * s.HitRecoilThreshold).F,
            CombatViewCone1Alpha: (((Fpu)b.CombatViewCone1Alpha * Degree) * s.CombatViewCone1Alpha).F,
            CombatViewCone1Length: (((Fpu)s.CombatViewCone1Length * b.CombatViewCone1Length) * Metre).F,
            CombatViewCone2Alpha: (((Fpu)b.CombatViewCone2Alpha * Degree) * s.CombatViewCone2Alpha).F,
            CombatViewCone2Length: (((Fpu)s.CombatViewCone2Length * b.CombatViewCone2Length) * Metre).F,
            IdleViewCone1Alpha: (((Fpu)b.IdleViewCone1Alpha * Degree) * s.IdleViewCone1Alpha).F,
            IdleViewCone1Length: (((Fpu)s.IdleViewCone1Length * b.IdleViewCone1Length) * Metre).F,
            IdleViewCone2Alpha: (((Fpu)b.IdleViewCone2Alpha * Degree) * s.IdleViewCone2Alpha).F,
            IdleViewCone2Length: (((Fpu)s.IdleViewCone2Length * b.IdleViewCone2Length) * Metre).F,
            IdleAnimationFrequency: (((Fpu)b.IdleAnimationFrequency * Ticks) * s.IdleAnimationFrequency).F,
            InvestigateViewCone1Alpha: (((Fpu)b.InvestigateViewCone1Alpha * Degree) * s.InvestigateViewCone1Alpha).F,
            InvestigateViewCone1Length: (((Fpu)s.InvestigateViewCone1Length * b.InvestigateViewCone1Length) * Metre).F,
            InvestigateViewCone2Alpha: (((Fpu)b.InvestigateViewCone2Alpha * Degree) * s.InvestigateViewCone2Alpha).F,
            InvestigateViewCone2Length: (((Fpu)s.InvestigateViewCone2Length * b.InvestigateViewCone2Length) * Metre).F,
            InvestigateShoutFrequency: (((Fpu)s.InvestigateShoutFrequency * b.InvestigateShoutFrequency) * Ticks).F,
            AnimSpeedFactor: animSpeed,
            DetectionToAttackTime: (((Fpu)s.DetectionToAttackTime * b.DetectionToAttackTime) * Ticks).F);
    }
}
