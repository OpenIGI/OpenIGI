namespace OpenIGI.Game.Ai;

/// <summary>
/// The complete vocabulary an AI behaviour script is written against: fifty-five natives and four families
/// of constant, transcribed from the registrars rather than from the string table.
/// </summary>
/// <remarks>
/// <para>
/// Two registrars between them publish the lot. <c>0x44CCA0</c> registers the natives — eighteen
/// <c>AIAction_*</c> and thirty-seven <c>AIFunction_*</c>, each with its argument count and one of three
/// return tags. <c>0x44EEB0</c> registers the constants: the twenty-four <see cref="AiEvent"/> values, the two
/// <see cref="AiActionFlagWord"/>, the twenty-nine <see cref="AiType"/> values (by walking a string table), the
/// two <see cref="AiAlarmAccess"/> values, and two bare distances.
/// </para>
/// <para>
/// This table is the authoritative vocabulary, separate from the native bodies and the event pump
/// (<c>HumanAI</c>'s per-tick, <c>0x44F410</c>). Keeping it independent lets every retail AI script be checked
/// against it: 884 shipped <c>.qvm</c> files, and every AI name any of them mentions has to be in here.
/// </para>
/// </remarks>
public static class AiScriptApi
{
    /// <summary>How far a gunshot carries as an <see cref="AiEvent.Gunshot"/>, in engine units.</summary>
    /// <remarks>
    /// <c>HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE</c>, registered as the literal 122880 — <b>30 metres</b> on
    /// the 4096-units-per-metre scale. M9 read the same number off the weapon module's own constant
    /// (<c>0x1E000</c> at <c>0x44EFF3</c>), so this is the second independent sighting of it.
    /// </remarks>
    public const int GunshotRange = 122880;

    /// <summary>How far a silenced gunshot carries, in engine units.</summary>
    /// <remarks>
    /// <c>HUMANAI_DETECTIONEVENT_GUNSHOT_SILENCED_RANGE</c>, the literal 4096 — <b>one metre</b>. Thirty
    /// times shorter, which is why a silenced weapon is worth carrying.
    /// </remarks>
    public const int SilencedGunshotRange = 4096;

    private static readonly AiNative[] NativeTable =
    [
        // The eighteen actions, in registration order (0x44CCA0).
        new("AIAction_Patrol", 3, AiNativeReturn.Void, 0x44D0E0),
        new("AIAction_Combat", 1, AiNativeReturn.Void, 0x44D160),
        new("AIAction_Dead", 1, AiNativeReturn.Void, 0x44D260),
        new("AIAction_FallFlat", 2, AiNativeReturn.Void, 0x44D2C0),
        new("AIAction_Activate", 3, AiNativeReturn.Void, 0x44D420),
        new("AIAction_WalkToNode", 2, AiNativeReturn.Void, 0x44D4A0),
        new("AIAction_RunToNode", 2, AiNativeReturn.Void, 0x44D510),
        new("AIAction_FireAtNode", 5, AiNativeReturn.Void, 0x44D580),
        new("AIAction_FireAtTask", 5, AiNativeReturn.Void, 0x44D630),
        new("AIAction_PlayAnimation", 2, AiNativeReturn.Void, 0x44D6E0),
        new("AIAction_PlaySound", 3, AiNativeReturn.Void, 0x44D750),
        new("AIAction_MoveToEvent", 2, AiNativeReturn.Void, 0x44D7D0),
        new("AIAction_LookAtEvent", 2, AiNativeReturn.Void, 0x44D870),
        new("AIAction_Stunned", 2, AiNativeReturn.Void, 0x44D8F0),
        new("AIAction_KickGrenade", 1, AiNativeReturn.Void, 0x44D960),
        new("AIAction_RunPanicking", 2, AiNativeReturn.Void, 0x44DA00),
        new("AIAction_Idle", 1, AiNativeReturn.Void, 0x44DA80),
        new("AIAction_SetCombat", 1, AiNativeReturn.Void, 0x44DAE0),

        // The thirty-seven functions.
        new("AIFunction_DefaultHandler", 0, AiNativeReturn.Void, 0x44E060),
        new("AIFunction_RemoveAlarmActions", 0, AiNativeReturn.Void, 0x44DBC0),
        new("AIFunction_SetViewLength", 1, AiNativeReturn.Void, 0x44DC40),
        new("AIFunction_SetAlarmViewLength", 1, AiNativeReturn.Void, 0x44DCD0),
        new("AIFunction_SetViewAlpha", 1, AiNativeReturn.Void, 0x44DD30),
        new("AIFunction_SetViewGamma", 1, AiNativeReturn.Void, 0x44DD90),
        new("AIFunction_SetSecondaryViewLength", 1, AiNativeReturn.Void, 0x44DDF0),
        new("AIFunction_SetSecondaryAlarmViewLength", 1, AiNativeReturn.Void, 0x44DE80),
        new("AIFunction_SetSecondaryViewAlpha", 1, AiNativeReturn.Void, 0x44DEE0),
        new("AIFunction_SetSecondaryViewGamma", 1, AiNativeReturn.Void, 0x44DF40),
        new("AIFunction_SetEventPriority", 1, AiNativeReturn.Void, 0x44E0C0),
        new("AIFunction_SetInvulnerability", 1, AiNativeReturn.Void, 0x44E150),
        new("AIFunction_SetInstantDeath", 1, AiNativeReturn.Void, 0x44E1B0),
        new("AIFunction_SetDeathAnimation", 1, AiNativeReturn.Void, 0x44E210),
        new("AIFunction_SetAlarmTriggerID", 1, AiNativeReturn.Void, 0x44E270),
        new("AIFunction_SetAlarmControlID", 1, AiNativeReturn.Void, 0x44E2D0),
        new("AIFunction_SetAlarmAccess", 1, AiNativeReturn.Void, 0x44E330),
        new("AIFunction_SetGunnerID", 1, AiNativeReturn.Void, 0x44E390),
        new("AIFunction_SetScriptIntegerValue", 2, AiNativeReturn.Void, 0x44E6D0),
        new("AIFunction_SetScriptRealValue", 2, AiNativeReturn.Void, 0x44E790),
        new("AIFunction_GetAlarmTriggerID", 0, AiNativeReturn.Int, 0x44E3F0),
        new("AIFunction_GetAlarmControlID", 0, AiNativeReturn.Int, 0x44E430),
        new("AIFunction_GetAlarmAccess", 0, AiNativeReturn.Int, 0x44E470),
        new("AIFunction_GetGunnerID", 0, AiNativeReturn.Int, 0x44E4B0),
        new("AIFunction_GetAlarmControlStatus", 0, AiNativeReturn.Int, 0x44E4F0),
        new("AIFunction_GetGunnerStatus", 0, AiNativeReturn.Int, 0x44E5E0),
        new("AIFunction_GetScriptIntegerValue", 1, AiNativeReturn.Int, 0x44E740),
        new("AIFunction_GetCurrentEventType", 0, AiNativeReturn.Int, 0x44DFA0),
        new("AIFunction_IsEventBehind", 0, AiNativeReturn.Int, 0x44E860),
        new("AIFunction_GetScriptRealValue", 1, AiNativeReturn.Real, 0x44E800),
        new("AIFunction_GetRandomValue", 1, AiNativeReturn.Real, 0x44E000),
        new("AIFunction_GetEventDistance", 0, AiNativeReturn.Real, 0x44E930),
        new("AIFunction_GetAlarmTriggerDistance", 0, AiNativeReturn.Real, 0x44E9B0),
        new("AIFunction_SetAnimationInterval", 2, AiNativeReturn.Void, 0x44EAF0),
        new("AIFunction_AddAnimationEntry", 2, AiNativeReturn.Void, 0x44EBB0),
        new("AIFunction_GetAnimationToPlay", 0, AiNativeReturn.Int, 0x44ECB0),
        new("AIFunction_SendResponse", 0, AiNativeReturn.Void, 0x44EE40),
    ];

    // 0x53C94C - AIType -> AiArchetype. The identity prefix is what proves script 0 is rpg, not guard.
    private static readonly AiArchetype[] ArchetypeTable =
    [
        AiArchetype.Rpg, AiArchetype.Gunner, AiArchetype.Sniper, AiArchetype.Anya, AiArchetype.Ekk,
        AiArchetype.Priboi, AiArchetype.Civilian,
        AiArchetype.Patrol, AiArchetype.Patrol, AiArchetype.Patrol, AiArchetype.Patrol,
        AiArchetype.Guard, AiArchetype.Guard, AiArchetype.Guard, AiArchetype.Guard,
        AiArchetype.Patrol, AiArchetype.Patrol,
        AiArchetype.Patrol, AiArchetype.Patrol, AiArchetype.Patrol,
        AiArchetype.Guard, AiArchetype.Guard, AiArchetype.Guard,
        AiArchetype.Patrol, AiArchetype.Patrol, AiArchetype.Patrol,
        AiArchetype.Guard, AiArchetype.Guard, AiArchetype.Guard,
    ];

    private static readonly Dictionary<string, AiNative> NativesByName = BuildNatives();

    private static readonly Dictionary<string, int> ConstantsByName = BuildConstants();

    /// <summary>Gets every native, in the order <c>0x44CCA0</c> registers them.</summary>
    public static IReadOnlyList<AiNative> Natives => NativeTable;

    /// <summary>Gets every constant the AI module registers, by name.</summary>
    public static IReadOnlyDictionary<string, int> Constants => ConstantsByName;

    /// <summary>The path the engine loads an archetype's behaviour script from.</summary>
    /// <param name="archetype">The archetype.</param>
    /// <returns>The virtual path, as the original spells it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="archetype"/> is not one of the nine.</exception>
    public static string ScriptPath(AiArchetype archetype) => archetype switch
    {
        AiArchetype.Rpg => "LOCAL:common/ai/rpg.qsc",
        AiArchetype.Gunner => "LOCAL:common/ai/gunner.qsc",
        AiArchetype.Sniper => "LOCAL:common/ai/sniper.qsc",
        AiArchetype.Anya => "LOCAL:common/ai/anya.qsc",
        AiArchetype.Ekk => "LOCAL:common/ai/ekk.qsc",
        AiArchetype.Priboi => "LOCAL:common/ai/priboi.qsc",
        AiArchetype.Civilian => "LOCAL:common/ai/civilian.qsc",
        AiArchetype.Patrol => "LOCAL:common/ai/patrol.qsc",
        AiArchetype.Guard => "LOCAL:common/ai/guard.qsc",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype, "Not a shipped archetype."),
    };

    /// <summary>The path the AI settings table is loaded from.</summary>
    /// <remarks>Keyed by <c>(AIType, Difficulty)</c>; a miss is the fatal
    /// <c>"No configuration found for AIType: #%d, Difficulty: #%d"</c>.</remarks>
    public const string SettingsScriptPath = "LOCAL:common/ai/settings.qsc";

    /// <summary>The per-task AI override script for a task, from the original's <c>"MISSION:AI/%d.qsc"</c>.</summary>
    /// <param name="taskId">The owning task's id.</param>
    /// <returns>The virtual path.</returns>
    /// <remarks>
    /// The same rule the nav graph's file names follow — a level's <c>ai/</c> directory is named by task id,
    /// not by any sequence.
    /// </remarks>
    public static string MissionScriptPath(int taskId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MISSION:AI/{taskId}.qsc");

    /// <summary>Which behaviour script a soldier archetype runs.</summary>
    /// <param name="type">The archetype.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered type.</exception>
    public static AiArchetype ArchetypeOf(AiType type)
    {
        int index = (int)type;
        if (index < 0 || index >= ArchetypeTable.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a registered AI type.");
        }

        return ArchetypeTable[index];
    }

    /// <summary>Looks a native up by the name a script calls it by.</summary>
    /// <param name="name">The native's name.</param>
    /// <param name="native">Receives the entry.</param>
    /// <returns><see langword="true"/> when the name is one of the fifty-five.</returns>
    public static bool TryGetNative(string name, out AiNative native) =>
        NativesByName.TryGetValue(name, out native);

    /// <summary>Looks a constant up by name.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">Receives its value.</param>
    /// <returns><see langword="true"/> when the AI module registers it.</returns>
    public static bool TryGetConstant(string name, out int value) =>
        ConstantsByName.TryGetValue(name, out value);

    private static Dictionary<string, AiNative> BuildNatives()
    {
        Dictionary<string, AiNative> map = new(StringComparer.Ordinal);
        foreach (AiNative native in NativeTable)
        {
            if (!map.TryAdd(native.Name, native))
            {
                throw new InvalidOperationException($"'{native.Name}' is registered twice.");
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildConstants()
    {
        Dictionary<string, int> map = new(StringComparer.Ordinal)
        {
            ["AIACTIONFLAG_NONE"] = (int)AiActionFlagWord.None,
            ["AIACTIONFLAG_PUSHABLE"] = (int)AiActionFlagWord.Pushable,
            ["AIALARMACCESS_BEFORECOMBAT"] = (int)AiAlarmAccess.BeforeCombat,
            ["AIALARMACCESS_AFTERCOMBAT"] = (int)AiAlarmAccess.AfterCombat,
            ["HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE"] = GunshotRange,
            ["HUMANAI_DETECTIONEVENT_GUNSHOT_SILENCED_RANGE"] = SilencedGunshotRange,
        };

        foreach (AiEvent value in Enum.GetValues<AiEvent>())
        {
            map.Add(EventName(value), (int)value);
        }

        foreach (AiType value in Enum.GetValues<AiType>())
        {
            map.Add(TypeName(value), (int)value);
        }

        return map;
    }

    /// <summary>The name the engine registers an event under.</summary>
    /// <param name="value">The event.</param>
    /// <returns>The constant's name.</returns>
    public static string EventName(AiEvent value) => value switch
    {
        AiEvent.AlertResponse => "AIEVENT_ALERT_RESPONSE",
        _ => "AIEVENT_" + value.ToString().ToUpperInvariant(),
    };

    /// <summary>The name the engine registers a soldier archetype under.</summary>
    /// <param name="value">The archetype.</param>
    /// <returns>The constant's name.</returns>
    /// <remarks>
    /// Every name is the enum name with an underscore before each capital that starts a word, so the
    /// mapping is mechanical rather than a second table to keep in step. The faction and weapon parts are
    /// separated the same way: <c>MafiaGuardAk</c> is <c>AITYPE_MAFIA_GUARD_AK</c>.
    /// </remarks>
    public static string TypeName(AiType value)
    {
        string name = value.ToString();
        var text = new System.Text.StringBuilder("AITYPE_", name.Length + 12);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append('_');
            }

            text.Append(char.ToUpperInvariant(name[i]));
        }

        return text.ToString();
    }
}
