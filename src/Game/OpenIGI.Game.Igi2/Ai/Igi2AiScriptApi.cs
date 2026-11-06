using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>
/// I.G.I. 2's soldier and squad script API as the editor's vocabulary: every native <c>HumanAI_RegisterScriptFunctions</c> and
/// <c>AISquad_Open</c> register, with the arity they declare and each argument typed by how its callback reads it.
/// </summary>
/// <remarks>
/// <para>
/// Forty-four natives are Project I.G.I.'s, kept by name with the same reads (an int, a real or a string at each position),
/// so their pins come from <see cref="AiScriptVocabulary"/> rather than a second copy. What is listed here is only what
/// I.G.I. 2 added: its new actions, the thirty-two combat and perception tunings, and the squad script's natives.
/// </para>
/// <para>
/// I.G.I. 2's callbacks test <c>nArguments</c> against the declared count and do nothing on a mismatch, so arity matters
/// more here than in the first game, where the VM's check is only a declaration.
/// </para>
/// </remarks>
public static class Igi2AiScriptApi
{
    /// <summary>The family of the <c>NODECRITERIA_*</c> constants.</summary>
    public const string NodeCriteriaFamily = "NodeCriteria";

    /// <summary>The family of the <c>HUMANAI_TYPE_*</c> constants.</summary>
    public const string SoldierTypeFamily = "AiSoldierType";

    /// <summary>The family of the <c>HUMANAI_ANIMTYPE_*</c> constants.</summary>
    public const string AnimTypeFamily = "AiAnimType";

    /// <summary>The family of the <c>AILOOKAROUND_PATTERN_*</c> constants.</summary>
    public const string LookAroundFamily = "AiLookAround";

    /// <summary>The family of the <c>AISquadState_*</c> constants.</summary>
    public const string SquadStateFamily = "AiSquadState";

    /// <summary>The family of the <c>AIVoice_*</c> constants.</summary>
    public const string VoiceFamily = "AiVoice";

    /// <summary>The family of the <c>AIType_*</c> squad-type constants.</summary>
    public const string SquadTypeFamily = "AiSquadType";

    private const string ActionsCategory = "AI|Actions";
    private const string FunctionsCategory = "AI|Functions";
    private const string TuningCategory = "AI|Tuning";
    private const string SquadCategory = "AI|Squad";

    // Project I.G.I.'s natives I.G.I. 2 keeps, by name; their reads are the same at every position.
    private static readonly string[] Shared =
    [
        "AIAction_Patrol", "AIAction_Dead", "AIAction_FallFlat", "AIAction_Activate", "AIAction_WalkToNode", "AIAction_RunToNode",
        "AIAction_FireAtNode", "AIAction_FireAtTask", "AIAction_PlayAnimation", "AIAction_PlaySound", "AIAction_MoveToEvent",
        "AIAction_LookAtEvent", "AIAction_Stunned", "AIAction_KickGrenade", "AIAction_RunPanicking", "AIAction_Idle",
        "AIFunction_DefaultHandler", "AIFunction_RemoveAlarmActions", "AIFunction_SetViewLength", "AIFunction_SetViewAlpha",
        "AIFunction_SetViewGamma", "AIFunction_SetSecondaryViewLength", "AIFunction_SetSecondaryViewAlpha",
        "AIFunction_SetSecondaryViewGamma", "AIFunction_SetEventPriority", "AIFunction_SetInvulnerability",
        "AIFunction_SetInstantDeath", "AIFunction_SetDeathAnimation", "AIFunction_SetAlarmAccess", "AIFunction_SetGunnerID",
        "AIFunction_SetScriptIntegerValue", "AIFunction_SetScriptRealValue", "AIFunction_GetGunnerID", "AIFunction_GetGunnerStatus",
        "AIFunction_GetScriptIntegerValue", "AIFunction_GetCurrentEventType", "AIFunction_IsEventBehind",
        "AIFunction_GetScriptRealValue", "AIFunction_GetRandomValue", "AIFunction_GetEventDistance",
        "AIFunction_SetAnimationInterval", "AIFunction_AddAnimationEntry", "AIFunction_GetAnimationToPlay", "AIFunction_SendResponse",
    ];

    // The tunings, each one real the callback scales as it stores (seconds to ticks, metres to units, per cent to a share).
    private static readonly string[] Tunings =
    [
        "SetMinRangeAccuracy", "SetMedRangeAccuracy", "SetMaxRangeAccuracy", "SetAccuracyMedRange", "SetAccuracyMaxRange",
        "SetPercentageFiredPerBurst", "SetEvasiveActionProb", "SetMinDelayBetweenFiring", "SetTrackingTimeout",
        "SetTrackingMaxDistance", "SetGrenadeThrowProb", "SetGrenadeDetonationTimer", "SetGrenadeLandingReactionProb",
        "SetCloseCombatDamage", "SetHitPoints", "SetHitRecoilThreshold", "SetCombatViewCone1Alpha", "SetCombatViewCone1Length",
        "SetCombatViewCone2Alpha", "SetCombatViewCone2Length", "SetIdleViewCone1Alpha", "SetIdleViewCone1Length",
        "SetIdleViewCone2Alpha", "SetIdleViewCone2Length", "SetIdleAnimationFrequency", "SetInvestigateViewCone1Alpha",
        "SetInvestigateViewCone1Length", "SetInvestigateViewCone2Alpha", "SetInvestigateViewCone2Length",
        "SetInvestigateShoutFrequency", "SetAnimSpeedFactor", "SetHearingRangeFactor",
    ];

    private static readonly NativeParameter Flags =
        new("Flags", ScriptType.EnumOf(AiScriptApi.ActionFlagFamily), "AIACTIONFLAG_PUSHABLE lets a later action push over this one.");

    /// <summary>Gets every native's signature: the shared ones first, then I.G.I. 2's own.</summary>
    public static IReadOnlyList<NativeSignature> Signatures { get; } = Build();

    /// <summary>Gets the AI module's constants, grouped into families.</summary>
    public static IReadOnlyList<ConstantFamily> Families { get; } = BuildFamilies();

    /// <summary>The family an I.G.I. 2 AI constant belongs to, by its prefix.</summary>
    /// <param name="name">The constant's name.</param>
    /// <returns>The family's name.</returns>
    public static string FamilyOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        (string Prefix, string Family)[] prefixes =
        [
            ("NODECRITERIA_", NodeCriteriaFamily), ("HUMANAI_TYPE_", SoldierTypeFamily), ("HUMANAI_ANIMTYPE_", AnimTypeFamily),
            ("AILOOKAROUND_", LookAroundFamily), ("AISquadState_", SquadStateFamily), ("AIVoice_", VoiceFamily),
            ("AIType_", SquadTypeFamily),
        ];
        foreach ((string prefix, string family) in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return family;
            }
        }

        return AiScriptVocabulary.FamilyOf(name);
    }

    private static NativeSignature[] Build()
    {
        Dictionary<string, NativeSignature> first = AiScriptVocabulary.Signatures.ToDictionary(static s => s.Name, StringComparer.Ordinal);
        List<NativeSignature> all = [.. Shared.Select(name => first[name])];

        all.AddRange(Tunings.Select(static tuning => Action("AIFunction_" + tuning, FunctionsCategory, [new("Value", ScriptType.Real)]) with
        {
            Category = TuningCategory,
        }));

        all.AddRange(
        [
            Action("AIFunction_SetSideKick", FunctionsCategory, [new("On", ScriptType.Bool)]),
            Action("AIFunction_UseCombatView", FunctionsCategory, []),
            Action("AIFunction_UseIdleView", FunctionsCategory, []),
            Pure("AIFunction_HasSeenDeadFriendlyBefore", ScriptType.Bool),
            Action("AIFunction_SetStandActionAnimation", FunctionsCategory, [new("Animation", ScriptType.Text, "The animation's name, up to 31 characters.")]),
            Pure("AIFunction_HasTarget", ScriptType.Bool),
            Action("AIFunction_LimitViewRange", FunctionsCategory, [new("On", ScriptType.Bool)]),
            Action("AIFunction_PassEventOnToSquad", FunctionsCategory, []),

            Action("AIAction_DelayedEventPassOn", ActionsCategory, [Flags]),
            Action("AIAction_LookInApproximateEventDirection", ActionsCategory, [new("Seconds", ScriptType.Real), new("Sectors", ScriptType.Int, "How finely the guess is made, at least one."), Flags]),
            Action("AIAction_CombatIdle", ActionsCategory, [Flags]),
            Action("AIAction_LookAround", ActionsCategory, [new("Pattern", ScriptType.EnumOf(LookAroundFamily), "Read and discarded: the sweep cycles through the patterns itself."), Flags]),
            Action("AIAction_CallForBackup", ActionsCategory, [new("Metres", ScriptType.Real, "How far the call carries.")]),
            Action("AIAction_EnemyDetection", ActionsCategory, [Flags]),
            Action("AIAction_LookAtNode", ActionsCategory, [new("Node", ScriptType.NodeRef), Flags]),
            Action("AIAction_FriendlyDetection", ActionsCategory, [Flags]),
            Action("AIAction_RunToCover", ActionsCategory, [new("Node", ScriptType.NodeRef, "The cover node."), Flags]),
            Action("AIAction_Wait", ActionsCategory, [new("Seconds", ScriptType.Real), Flags]),
            Action("AIAction_OffensiveCombat", ActionsCategory, [Flags]),
            Action("AIAction_DefensiveCombat", ActionsCategory, [Flags]),

            Squad(Pure("AISquad_GetState", ScriptType.EnumOf(SquadStateFamily))),
            Squad(Pure("AISquad_GetEvent", ScriptType.EnumOf(AiScriptApi.EventFamily))),
            Squad(Action("AISquad_SwitchState", SquadCategory, [new("State", ScriptType.EnumOf(SquadStateFamily)), new("Voice", ScriptType.EnumOf(VoiceFamily), "Optional: the line the leader says.")]) with { OptionalCount = 1 }),
            Squad(Pure("AISquad_GetSquadType", ScriptType.EnumOf(SquadTypeFamily))),
            Squad(Pure("AISquad_HasTarget", ScriptType.Bool)),
            Squad(Action("AISquad_ReceiverLookAtEvent", SquadCategory, [])),
            Squad(Action("AISquad_ReceiverLookInApproxEventDirection", SquadCategory, [])),
            Squad(Action("AISquad_TriggerAlarm", SquadCategory, [])),
            Squad(Action("AISquad_ManStationaryGun", SquadCategory, [])),
            Squad(Action("AISquad_MoveToNode", SquadCategory, [new("Node", ScriptType.NodeRef)])),
            Squad(Action("AISquad_Patrol", SquadCategory, [new("Path", ScriptType.PathRef)])),
            Squad(Action("AISquad_ThrowGrenade", SquadCategory, [])),
            Squad(Pure("AISquad_GetCurrentPatrolID", ScriptType.PathRef)),
            Squad(Pure("AISquad_GetEventSpecificInt32", ScriptType.Int)),
            Squad(Pure("AISquad_GetDistanceToEvent", ScriptType.Real)),
            Squad(Action("AISquad_DefaultHandler", SquadCategory, [])),
            Squad(Pure("AISquad_ReceiverHasTarget", ScriptType.Bool)),
            Squad(Action("AISquad_PlaySoundAtReceiversPos", SquadCategory, [new("Voice", ScriptType.EnumOf(VoiceFamily))])),
            Squad(Action("AISquad_TriggerEvent", SquadCategory, [new("Event", ScriptType.EnumOf(AiScriptApi.EventFamily)), new("Metres", ScriptType.Real, "Who hears it.")])),
        ]);

        return [.. all];
    }

    private static NativeSignature Action(string name, string category, NativeParameter[] parameters) =>
        new(name, parameters, null, NativeEffect.Action, ScriptContexts.Ai) { Category = category };

    private static NativeSignature Pure(string name, ScriptType returns) =>
        new(name, [], returns, NativeEffect.Pure, ScriptContexts.Ai) { Category = FunctionsCategory };

    private static NativeSignature Squad(NativeSignature signature) =>
        signature with { Contexts = ScriptContexts.Squad, Category = SquadCategory };

    private static ConstantFamily[] BuildFamilies()
    {
        IEnumerable<(string Name, int Value)> all = Igi2AiConstants.Values.Concat(
            Igi2AiConstants.SquadTypes.Select(static (name, index) => (name, index)));
        return [.. all
            .GroupBy(static c => FamilyOf(c.Name), StringComparer.Ordinal)
            .Select(static g => new ConstantFamily(g.Key, [.. g.Select(static c => new KeyValuePair<string, int>(c.Name, c.Value))]))];
    }
}
