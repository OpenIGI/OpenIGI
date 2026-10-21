using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>
/// The constants the AI's scripts are written against, as <c>HumanAI_RegisterConstants</c> (<c>igi2 0x45C730</c>) and
/// <c>AISquad_Open</c> (<c>0x49B240</c>) register them.
/// </summary>
public static class Igi2AiConstants
{
    /// <summary>The AI types a <c>HumanAI</c> names, in the order of <c>HumanAI_azAITypeStrings</c> (<c>igi2 0x63E268</c>).</summary>
    public static IReadOnlyList<string> AiTypes { get; } =
    [
        "HUMANAI_TYPE_C1_NORMAL_SOLDIER",
        "HUMANAI_TYPE_C1_TOUGH_SOLDIER",
        "HUMANAI_TYPE_C2_NORMAL_SOLDIER",
        "HUMANAI_TYPE_C2_TOUGH_SOLDIER",
        "HUMANAI_TYPE_C3_NORMAL_SOLDIER",
        "HUMANAI_TYPE_C3_TOUGH_SOLDIER",
    ];

    /// <summary>The animation sets a <c>HumanAI</c> names, in the order of the table after the AI types (<c>igi2 0x63E280</c>).</summary>
    public static IReadOnlyList<string> AnimTypes { get; } =
    [
        "HUMANAI_ANIMTYPE_CIVILIAN",
        "HUMANAI_ANIMTYPE_PRIBOY_RIFLE",
        "HUMANAI_ANIMTYPE_SOLDIER_RIFLE",
        "HUMANAI_ANIMTYPE_PRIBOY_PISTOL",
        "HUMANAI_ANIMTYPE_SOLDIER_PISTOL",
        "HUMANAI_ANIMTYPE_PRIBOI_SUBMACHINEGUN",
        "HUMANAI_ANIMTYPE_SOLDIER_SUBMACHINEGUN",
    ];

    /// <summary>Gets every named constant with its value, events first, in registration order.</summary>
    public static IReadOnlyList<(string Name, int Value)> Values { get; } = Build();

    /// <summary>Registers the constants.</summary>
    /// <param name="natives">The script namespace.</param>
    public static void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        foreach ((string name, int value) in Values)
        {
            natives.RegisterConstant(name, value);
        }
    }

    private static (string, int)[] Build()
    {
        List<(string, int)> values = [];
        string[] events =
        [
            "AIEVENT_CREATE", "AIEVENT_DELETE", "AIEVENT_DEAD", "AIEVENT_ANIMATION", "AIEVENT_IDLE", "AIEVENT_ALERT",
            "AIEVENT_ALERT_RESPONSE", "AIEVENT_ALARMON", "AIEVENT_ALARMOFF", "AIEVENT_WALK", "AIEVENT_GROUNDIMPACT", "AIEVENT_DOOR",
            "AIEVENT_FENCE", "AIEVENT_LADDER", "AIEVENT_TAKINGDAMAGE", "AIEVENT_GUNSHOT", "AIEVENT_GRENADETHROWN",
            "AIEVENT_GRENADELAND", "AIEVENT_FLASHBANG", "AIEVENT_GUNSHOTMISS", "AIEVENT_EXPLOSION", "AIEVENT_ENEMYDETECTION",
            "AIEVENT_FRIENDLYDETECTION", "AIEVENT_FRIENDLY_GUNSHOT", "AIEVENT_DEATH_CRY", "AIEVENT_BACKUP_REQUEST",
            "AIEVENT_SEENSOMETHING", "AIEVENT_LOSTSIGHTOFENEMY", "AIEVENT_SQUADCOMMAND", "AIEVENT_INQUIRETIMEOUT",
            "AIEVENT_SQUADCOMBATIDLE", "AIEVENT_WOUNDED_CRY", "AIEVENT_PATROLCOMMANDPROCESSED", "AIEVENT_ENEMYDETECTION_NO_LOF",
            "AIEVENT_FRIENDLY_DYING", "AIEVENT_DESTROYED_CAMERA", "AIEVENT_FRIENDLY_DAMAGE_SEEN", "AIEVENT_GRENADESHOUT",
            "AIEVENT_DANGERSTATETIMEOUT", "AIEVENT_VISUAL_GUNSHOT", "AIEVENT_DONELOOKING", "AIEVENT_DISTRESSCALL",
            "AIEVENT_BULLETIMPACT", "AIEVENT_SEARCHOVER", "AIEVENT_HANDLEWEAPON", "AIEVENT_CAMERAALARMUPDATE",
            "AIEVENT_REACTTOSUSPICION",
        ];
        for (int i = 0; i < events.Length; i++)
        {
            values.Add((events[i], i));
        }

        // AIGraph_Open (0x59D390) registers the node criteria, each the next free bit (GraphNode_RegisterCriteria, 0x5DA6E0).
        values.Add(("NODECRITERIA_VEHICLE", 1));
        values.Add(("NODECRITERIA_DOOR", 2));
        values.Add(("NODECRITERIA_VIEW", 4));
        values.Add(("NODECRITERIA_STAIR", 8));
        values.Add(("AIACTIONFLAG_NONE", 0));
        values.Add(("AIACTIONFLAG_PUSHABLE", 1));
        values.Add(("HUMANAI_DETECTIONEVENT_GUNSHOT_SILENCED_RANGE", 0x5000));
        values.Add(("HUMANAI_DETECTIONEVENT_GUNSHOT_PISTOL_RANGE", 0x32000));
        values.Add(("HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE", 0x64000));
        values.Add(("HUMANAI_DETECTIONEVENT_CANNON_RANGE", 0x64000));
        for (int i = 0; i < AiTypes.Count; i++)
        {
            values.Add((AiTypes[i], i));
        }

        for (int i = 0; i < AnimTypes.Count; i++)
        {
            values.Add((AnimTypes[i], i));
        }

        values.Add(("HUMANAI_ANIMTYPE_PRIBOY", 1));
        values.Add(("HUMANAI_ANIMTYPE_SOLDIER", 2));
        values.Add(("AIALARMACCESS_BEFORECOMBAT", 0));
        values.Add(("AIALARMACCESS_AFTERCOMBAT", 1));
        values.Add(("AILOOKAROUND_PATTERN_180_CLOCKWISE", 0));
        values.Add(("AILOOKAROUND_PATTERN_90_CLOCKWISE_QUICK", 1));
        values.Add(("AILOOKAROUND_PATTERN_180_COUNTERCLOCKWISE", 3));
        values.Add(("AILOOKAROUND_PATTERN_360_SLOW", 4));
        values.Add(("AILOOKAROUND_PATTERN_360_FAST", 5));

        string[] squadStates =
        [
            "AISquadState_Idle", "AISquadState_HoldArea", "AISquadState_Inquire", "AISquadState_SearchArea", "AISquadState_Move",
            "AISquadState_Leapfrog", "AISquadState_Patrol", "AISquadState_MoveSearchInDirection", "AISquadState_LookSearchInDirection",
            "AISquadState_Danger",
        ];
        for (int i = 0; i < squadStates.Length; i++)
        {
            values.Add((squadStates[i], i));
        }

        string[] voices =
        [
            "AIVoice_HurryUp", "AIVoice_Acknowledge", "AIVoice_HoldArea", "AIVoice_SearchArea", "AIVoice_RaiseAlarm",
            "AIVoice_ManStationaryGun", "AIVoice_EnemySpotted", "AIVoice_EnemyFound", "AIVoice_EnemyDetected", "AIVoice_GunShotHeard",
            "AIVoice_WhatsThat", "AIVoice_Engaging", "AIVoice_Grenade", "AIVoice_LostTarget", "AIVoice_TakeCover", "AIVoice_Stop",
            "AIVoice_BackToIdle", "AIVoice_FalseAlarm", "AIVoice_ManDown",
        ];
        for (int i = 0; i < voices.Length; i++)
        {
            values.Add((voices[i], i));
        }

        return [.. values];
    }
}
