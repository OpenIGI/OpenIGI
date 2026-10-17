using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What the interaction resolver is currently offering the player, in the terms the prompt needs.
/// </summary>
/// <remarks>
/// <para>
/// A flat description rather than the live <see cref="WorldInteraction"/> so the prompt's decisions —
/// its wording, its method, its ring — are pure functions of data a test can write down. The player's rig
/// builds one of these from whatever it resolved this tick; nothing here can reach the simulation.
/// </para>
/// <para>
/// <paramref name="AnchorUnits"/> is in world units (4096 per metre), the same space the interaction
/// registry works in, because that is what the camera has to hand. The renderer converts once.
/// </para>
/// </remarks>
/// <param name="Id">A stable identity for this offer, so the marker can pop when the target changes.</param>
/// <param name="Type">The resolved <c>GetActivate</c> type.</param>
/// <param name="ActionSprite">The retail action sprite the task names, which is what the title reads first.</param>
/// <param name="AnchorUnits">Where the marker is pinned, in world units.</param>
/// <param name="DurationTicks">The authored hold duration in 30 Hz ticks, or zero for an instant action.</param>
/// <param name="Locked">Whether a door is locked and therefore offers its lock rather than its handle.</param>
/// <param name="DoorOpen">Whether the door this offers is already open, so the verb is "close".</param>
/// <param name="Progress">The running interaction's progress in <c>[0,1]</c>, or <see langword="null"/> when idle.</param>
public readonly record struct InteractionPromptTarget(
    int Id,
    InteractionActivateType Type,
    string? ActionSprite,
    Vector3d AnchorUnits,
    int DurationTicks,
    bool Locked,
    bool DoorOpen,
    float? Progress);
