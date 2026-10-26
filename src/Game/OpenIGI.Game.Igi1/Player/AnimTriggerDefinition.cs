using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// One entry of the animation-trigger table: what an animation event with a given id spawns.
/// </summary>
/// <param name="EventId">The id an animation's <c>BOED</c> event carries.</param>
/// <param name="TaskType">
/// The task type to spawn. Every retail entry is <c>TASKTYPE_ANIMSOUND</c>, whose task is a four-byte
/// marker with no constructor — it exists only to receive the sound messages.
/// </param>
/// <param name="Resource">The sound name for an <c>AnimSound</c>.</param>
/// <param name="Attached">
/// The fourth argument, an <c>AnimSound</c>'s attached flag (<c>0x51B4B0</c>): its sound plays on the human and follows it,
/// where without it the sound stays where the event fired (<c>0x51B4F0</c>). Every retail entry is attached but the four
/// <c>walk_fence_*</c> surface sounds.
/// </param>
public readonly record struct AnimTriggerDefinition(int EventId, string TaskType, string Resource, bool Attached);
