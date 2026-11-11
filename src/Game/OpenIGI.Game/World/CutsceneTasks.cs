using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// A task of one of the Project I.G.I. types the cutscene runtime keeps the state of: the vehicles a level's story brings
/// in, the animations that move them, and the sounds and guns that go with them.
/// </summary>
/// <remarks>
/// Each has a run handler of its own in the original; <see cref="CutsceneRuntime"/> holds what they share -- the vehicles'
/// bodies, the animation packets, the scene's clock -- and each task runs its part where the level's walk reaches it. One
/// the runtime does not keep, or a level with no runtime, is walked as a plain container is.
/// </remarks>
public abstract class CutsceneRuntimeTask : DeclaredLevelTask
{
    /// <summary>Gets or sets the level's cutscene runtime, which steps this task; none until it is built.</summary>
    internal CutsceneRuntime? Cutscenes { get; set; }

    /// <inheritdoc/>
    public sealed override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Cutscenes?.RunFromWalk(this, level) != true)
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>An animation played on another task: Project I.G.I.'s <c>AnimTask</c> (run handler <c>0x4F3B40</c>).</summary>
public sealed class AnimTaskTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AnimTask";
}

/// <summary>A sound that plays while its script holds: Project I.G.I.'s <c>ConditionalSound</c>.</summary>
public sealed class ConditionalSoundTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ConditionalSound";
}

/// <summary>A wheeled vehicle: Project I.G.I.'s <c>Car</c>.</summary>
public sealed class CarTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Car";
}

/// <summary>A helicopter: Project I.G.I.'s <c>Heli</c>.</summary>
public sealed class HeliTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Heli";
}

/// <summary>What drives a car along a patrol path: Project I.G.I.'s <c>CarAI</c> (callback <c>0x425DE0</c>).</summary>
public sealed class CarAiTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "CarAI";
}

/// <summary>What mans a mounted gun for the level: Project I.G.I.'s <c>AIStationaryGunHolder</c>.</summary>
public sealed class StationaryGunHolderTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = CutsceneGunHolders.TypeName;
}
