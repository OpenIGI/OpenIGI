using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The filter word a posted AI event carries at <c>+8</c>, deciding which of seven admission tests the
/// receiving AI applies before the event reaches its queue.
/// </summary>
/// <remarks>
/// <para>
/// Read from <c>0x450110</c>, <c>HumanAI</c>'s handler for the post-event message. The tests are applied in a
/// fixed order and <b>each clears its own bits from the working copy as it runs</b>, which is what lets the
/// enemy/friendly and same-side/other-side pairs ride on shared bits: the engine tests <c>0x0C</c> and then
/// <c>0x14</c>, so bit <c>0x04</c> participates in both and is consumed by the first.
/// </para>
/// <para>
/// A word of zero skips every test and is admitted unconditionally — that is the short path
/// <c>0x450D80</c> takes for the alarm and idle-animation events it posts itself.
/// </para>
/// <para>
/// I.G.I. 2's <c>HumanAI_SendEvent</c> applies the same bits, and adds its own above them (<c>0x100</c> not the originator,
/// <c>0x200</c> in view at any distance, <c>0x400</c> heard through the level's noise).
/// </para>
/// </remarks>
[Flags]
public enum AiEventFilter
{
    /// <summary>No tests — admitted unconditionally.</summary>
    None = 0,

    /// <summary>The event's position must be inside the receiver's view cone, chosen by its alert latch.</summary>
    InView = 0x01,

    /// <summary>The event's position must be within the radius the event itself carries at <c>+12</c>.</summary>
    InRange = 0x02,

    /// <summary>The event's subject must exist and be of the expected task kind.</summary>
    SubjectIsHuman = 0x04,

    /// <summary>The event's subject must be alive: <c>0x450110</c> refuses it under <c>0x0C</c> when its death latch (<c>Human+3317</c>) is set.</summary>
    SubjectAlive = 0x08,

    /// <summary>The event's subject must be dead: refused under <c>0x14</c> while its death latch is clear.</summary>
    SubjectDead = 0x10,

    /// <summary>The event's side must equal the receiver's own.</summary>
    SameSide = 0x20,

    /// <summary>The event's side must differ from the receiver's own.</summary>
    OtherSide = 0x40,
}
