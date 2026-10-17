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
/// <param name="Flag">
/// The fourth argument. <see langword="true"/> for every retail entry except the four
/// <c>walk_fence_*</c> surface sounds, whose meaning is not established.
/// </param>
public readonly record struct AnimTriggerDefinition(int EventId, string TaskType, string Resource, bool Flag);
