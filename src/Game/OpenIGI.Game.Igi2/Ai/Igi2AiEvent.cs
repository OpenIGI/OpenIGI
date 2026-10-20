using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>
/// An event raised for the AI to perceive, <c>HumanAIEventContext_t</c> as <c>HumanAI_SendEvent</c> (<c>igi2 0x460A90</c>) takes
/// it from a human.
/// </summary>
/// <param name="Event">The event, an <c>AIEVENT_*</c> value (<see cref="Igi2AiConstants"/>).</param>
/// <param name="Team">The team it concerns, <c>eTeam</c>; <c>-1</c> for every team.</param>
/// <param name="Flags">Its flags, <c>bFlags</c>.</param>
/// <param name="Radius">How far it reaches, in world units, <c>vRadius</c>.</param>
/// <param name="Position">Where it happens, <c>*ptPosition</c>, or <see langword="null"/> when nowhere.</param>
/// <param name="Direction">Its direction, <c>tDirection</c>.</param>
/// <param name="Originator">The human who raised it, <c>ptHumanOriginatorQTask</c>.</param>
/// <param name="Reference">What it is about, <c>ptReferenceQTask</c>, or <see langword="null"/>.</param>
/// <param name="Specific">The 24 bytes only this kind of event carries, <c>uEventSpecificData</c>, or <see langword="null"/>.</param>
public readonly record struct Igi2AiEvent(
    int Event,
    int Team,
    int Flags,
    float Radius,
    Vector3d? Position,
    Vector3f Direction,
    object? Originator,
    object? Reference = null,
    object? Specific = null)
{
    /// <summary>An alarm starting, event 7, <c>AIEVENT_ALARMON</c> (<c>AlarmControl_RunHandler</c>).</summary>
    public const int AlarmOn = 7;

    /// <summary>An alarm stopping, event 8, <c>AIEVENT_ALARMOFF</c> (<c>AlarmControl_RunHandler</c>).</summary>
    public const int AlarmOff = 8;

    /// <summary>A security camera destroyed, event 35, <c>AIEVENT_DESTROYED_CAMERA</c> (<c>SCamera_RunHandler</c>).</summary>
    public const int DestroyedCamera = 35;

    /// <summary>A camera's first sight of someone while the alarm sounds, event 45, <c>AIEVENT_CAMERAALARMUPDATE</c>.</summary>
    public const int CameraAlarmUpdate = 45;

    /// <summary>A weapon reloaded or changed, event 44, <c>AIEVENT_HANDLEWEAPON</c> (<c>HumanPlayer_SendHandleWeaponAIEvent</c>).</summary>
    public const int HandleWeapon = 44;

    /// <summary>A door opened or shut, event 11, <c>AIEVENT_DOOR</c> (<c>Human_SendDoorAIEvent</c>).</summary>
    public const int Door = 11;

    /// <summary>A human climbing over a fence, event 12, <c>AIEVENT_FENCE</c> (<c>HumanPlayer_ClimbFence</c>).</summary>
    public const int Fence = 12;

    /// <summary>A human climbing a ladder other than at a walk, event 13, <c>AIEVENT_LADDER</c> (<c>HumanPlayer_RunStateClimbLadder</c>).</summary>
    public const int Ladder = 13;

    /// <summary>
    /// A human heard moving, <c>AIEVENT_WALK</c>, event 9: the player's steps (<c>HumanPlayer_SendWalkEvent</c>) and its bumping
    /// into another human (<c>Human_HumanCollision</c>).
    /// </summary>
    public const int Walk = 9;

    /// <summary>A human landing or a body hitting the ground, <c>AIEVENT_GROUNDIMPACT</c>.</summary>
    public const int GroundImpact = 10;

    /// <summary>A human hurt, <c>AIEVENT_TAKINGDAMAGE</c>.</summary>
    public const int TakingDamage = 14;

    /// <summary>A bullet heard going by, event 19, <c>AIEVENT_GUNSHOTMISS</c> (<c>Human_EvaluateGunShotHandler</c>).</summary>
    public const int GunShotMiss = 19;

    /// <summary>A death cry, <c>AIEVENT_DEATH_CRY</c>.</summary>
    public const int DeathCry = 24;

    /// <summary>A wounded cry, <c>AIEVENT_WOUNDED_CRY</c>.</summary>
    public const int WoundedCry = 31;

    /// <summary>A friend dying, <c>AIEVENT_FRIENDLY_DYING</c>.</summary>
    public const int FriendlyDying = 34;

    /// <summary>A friend seen hurt, <c>AIEVENT_FRIENDLY_DAMAGE_SEEN</c>.</summary>
    public const int FriendlyDamageSeen = 36;

    /// <summary><c>AIEVENT_GRENADESHOUT</c>: a soldier's grenade is on its way to where the event is.</summary>
    public const int GrenadeShout = 37;

    /// <summary>
    /// Another game's event in this game's terms (<see cref="HumanAiEvent"/>, numbered as <see cref="AiEvent"/>): the engine's
    /// events in the same order, Project I.G.I.'s <c>AIEVENT_COMBAT</c> (7) without a counterpart, so every later one a
    /// number lower here.
    /// </summary>
    /// <param name="shared">The event.</param>
    /// <param name="aiEvent">Receives the event.</param>
    /// <returns>Whether this game has the event and the human it concerns passes the tests only this game's humans answer.</returns>
    /// <remarks>
    /// <para>
    /// The flags are the engine's own bits, which both games test alike, except those about a human: this game asks its
    /// own humans whether they live, so a human of the other game is tested here and those bits are cleared. A near
    /// miss is not taken: this game hears another's rounds through the level's bodies.
    /// </para>
    /// </remarks>
    public static bool TryFromShared(in HumanAiEvent shared, out Igi2AiEvent aiEvent)
    {
        aiEvent = default;
        int kind = shared.Type switch
        {
            < 0 or (int)Game.Ai.AiEvent.Combat or (int)Game.Ai.AiEvent.GunshotMiss => -1,
            < (int)Game.Ai.AiEvent.Combat => shared.Type,
            <= (int)Game.Ai.AiEvent.FriendlyDetection => shared.Type - 1,
            _ => -1,
        };
        if (kind < 0)
        {
            return false;
        }

        const int AboutAHuman = 0x04 | 0x08 | 0x10;
        int flags = shared.Flags;
        if ((flags & AboutAHuman) != 0)
        {
            HumanTask? subject = shared.Subject as HumanTask;
            if (subject is null
                || ((flags & 0x08) != 0 && subject.IsDead)
                || ((flags & 0x10) != 0 && !subject.IsDead))
            {
                return false;
            }

            flags &= ~AboutAHuman;
        }

        aiEvent = new Igi2AiEvent(kind, shared.Team, flags, shared.Radius, shared.Position, default, shared.Source, shared.Subject);
        return true;
    }
}
