using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>
/// An event raised for the AI to perceive, <c>HumanAIEventContext_t</c> as <c>HumanAI_SendEvent</c> (<c>igi2 0x460A90</c>) takes
/// it from a human.
/// </summary>
/// <param name="Event">The event, an <c>AIEVENT_*</c> value (<see cref="Igi2AiConstants"/>).</param>
/// <param name="Team">The team it concerns, <c>eTeam</c>; <c>-1</c> for every team.</param>
/// <param name="Flags">Its flags, <c>bFlags</c>.</param>
/// <param name="Radius">How far it reaches, in world units, <c>vRadius</c>.</param>
/// <param name="Position">Where it happens, <c>*ptPosition</c>.</param>
/// <param name="Direction">Its direction, <c>tDirection</c>.</param>
/// <param name="Originator">The human who raised it, <c>ptHumanOriginatorQTask</c>.</param>
/// <param name="Reference">What it is about, <c>ptReferenceQTask</c>, or <see langword="null"/>.</param>
public readonly record struct Igi2AiEvent(
    int Event,
    int Team,
    int Flags,
    float Radius,
    Vector3d Position,
    Vector3f Direction,
    object? Originator,
    object? Reference = null)
{
    /// <summary>A human landing or a body hitting the ground, <c>AIEVENT_GROUNDIMPACT</c>.</summary>
    public const int GroundImpact = 10;

    /// <summary>A human hurt, <c>AIEVENT_TAKINGDAMAGE</c>.</summary>
    public const int TakingDamage = 14;

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
}
