using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One retail ConditionalSound edge owned by a cutscene timeline.</summary>
/// <param name="Task">The ConditionalSound task that owns a non-simple voice.</param>
/// <param name="Name">The authored SoundDef name.</param>
/// <param name="Position">The world emitter, or null for a microphone-relative sound.</param>
/// <param name="Playing">True on a rising edge; false when a condition-owned voice must stop.</param>
/// <param name="Simple">Whether the rising edge is fire-and-forget.</param>
public readonly record struct CutsceneSoundEvent(
    GameTask Task,
    string Name,
    Vector3d? Position,
    bool Playing,
    bool Simple);
