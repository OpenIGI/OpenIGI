namespace OpenIGI.Game.Ai;

/// <summary>
/// The twenty-four events an AI script dispatches on. An archetype script is one long
/// <c>if (AIFunction_GetCurrentEventType() == AIEVENT_…)</c> chain, so this enum *is* the AI's interface.
/// </summary>
/// <remarks>
/// <para>
/// The numbering is read from the registrar <c>0x44EEB0</c>, which names all twenty-four in one run of
/// literal <c>Script_RegisterConstant(name, n)</c> calls. Worth doing rather than reading the string table:
/// the strings sit in <c>.rdata</c> in the <em>reverse</em> of this order, and M9 was caught out by exactly
/// that — <c>MISSION_TYPE_*</c> is not in string order either.
/// </para>
/// <para>
/// The events divide into three kinds, though nothing in the engine labels them: lifecycle
/// (<see cref="Create"/>, <see cref="Delete"/>, <see cref="Dead"/>), state
/// (<see cref="Idle"/>, <see cref="Alert"/>, <see cref="Combat"/>, the two alarm events), and sensed —
/// everything from <see cref="Walk"/> down, which other systems post with a position so the script can ask
/// for its distance and whether it came from behind.
/// </para>
/// </remarks>
public enum AiEvent
{
    /// <summary>The AI has just been spawned. Posted on its first tick by the initialised latch.</summary>
    Create = 0,

    /// <summary>The AI is being destroyed.</summary>
    Delete = 1,

    /// <summary>The AI has died.</summary>
    Dead = 2,

    /// <summary>The idle-animation countdown has expired.</summary>
    Animation = 3,

    /// <summary>Nothing is happening — where a guard picks up its patrol.</summary>
    Idle = 4,

    /// <summary>Something has been noticed but not identified.</summary>
    Alert = 5,

    /// <summary>A response to another AI's alert.</summary>
    AlertResponse = 6,

    /// <summary>The AI is fighting.</summary>
    Combat = 7,

    /// <summary>An alarm has been raised.</summary>
    AlarmOn = 8,

    /// <summary>An alarm has been cleared.</summary>
    AlarmOff = 9,

    /// <summary>Footsteps were heard.</summary>
    Walk = 10,

    /// <summary>Something struck the ground.</summary>
    GroundImpact = 11,

    /// <summary>A door was used.</summary>
    Door = 12,

    /// <summary>A fence was climbed.</summary>
    Fence = 13,

    /// <summary>A ladder was used.</summary>
    Ladder = 14,

    /// <summary>The AI is being hit.</summary>
    TakingDamage = 15,

    /// <summary>A shot was heard. Audible at <see cref="AiScriptApi.GunshotRange"/>, or the silenced range.</summary>
    Gunshot = 16,

    /// <summary>A grenade was thrown.</summary>
    GrenadeThrown = 17,

    /// <summary>A grenade landed.</summary>
    GrenadeLand = 18,

    /// <summary>A flashbang went off. Note that only the player has a handler for the flash itself (M9).</summary>
    Flashbang = 19,

    /// <summary>A shot went past without hitting.</summary>
    GunshotMiss = 20,

    /// <summary>An explosion was heard.</summary>
    Explosion = 21,

    /// <summary>An enemy came into view.</summary>
    EnemyDetection = 22,

    /// <summary>A friendly came into view.</summary>
    FriendlyDetection = 23,
}
