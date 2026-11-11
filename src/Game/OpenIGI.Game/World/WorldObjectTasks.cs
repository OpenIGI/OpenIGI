using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>A door: Project I.G.I.'s <c>Door</c>, whose run handler (<c>0x446D10</c>) runs its children and then its own state machine.</summary>
/// <remarks>
/// <see cref="DoorRegistry"/> keeps the level's doors and each one's state; the task runs its door where the level's walk
/// reaches it. Its parameters are still the ones the level declares, read by name.
/// </remarks>
public sealed class DoorTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Door";

    /// <summary>Gets or sets the level's doors, which step this one; none until they are built.</summary>
    internal DoorRegistry? Doors { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Doors?.RunFromWalk(this);
    }
}

/// <summary>A fan, a dome or a turning sign: Project I.G.I.'s <c>RotatingObject</c>, whose run handler (<c>0x439090</c>) runs no children.</summary>
/// <remarks><see cref="RotatingObjectRegistry"/> keeps each one's speed and angle; the task steps its own where the level's walk reaches it.</remarks>
public sealed class RotatingObjectTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "RotatingObject";

    /// <summary>Gets or sets the level's rotating objects, which step this one; none until they are built.</summary>
    internal RotatingObjectRegistry? Rotators { get; set; }

    /// <inheritdoc/>
    /// <remarks>With nothing to step it, it is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Rotators?.RunFromWalk(this) != true)
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>
/// One of the things a human works by hand -- a switch, a terminal, a cabinet, a timed action, a fence, a wire, a mounted
/// gun -- or a generator: the Project I.G.I. types <see cref="WorldInteractionRegistry"/> keeps the state of.
/// </summary>
/// <remarks>
/// Each type's run handler orders its own work and its children's differently (a switch, terminal, cabinet and generator
/// run their children first; a fence and a mounted gun last; a wire not at all), which the registry knows; the task runs
/// it where the level's walk reaches it.
/// </remarks>
public class WorldInteractionTask : DeclaredLevelTask
{
    /// <summary>The script names level data instantiates this class's types under.</summary>
    public static readonly IReadOnlyList<string> TypeNames =
        ["Switch", "Terminal", "Cabinet", "GenericTBA", "PlaceExplosiveTBA", "Fence", "StationaryGun", "Wire", "Generator"];

    /// <summary>Gets or sets the level's interactions, which step this one; none until they are built.</summary>
    internal WorldInteractionRegistry? Interactions { get; set; }

    /// <inheritdoc/>
    /// <remarks>One the registry does not keep is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Interactions?.RunFromWalk(this, level) != true)
        {
            level.RunChildren(this);
        }
    }
}

/// <summary>A weapon or a box of ammunition lying in the level: Project I.G.I.'s <c>GunPickup</c> and <c>AmmoPickup</c>.</summary>
/// <remarks>
/// Its run handler deletes the task once the pickup has been taken (<see cref="TakenPickupTask"/>), which the registry that
/// keeps the pickup tells it.
/// </remarks>
public sealed class PickupTask : DeclaredLevelTask
{
    /// <summary>The script names level data instantiates this class's types under.</summary>
    public static readonly IReadOnlyList<string> TypeNames = ["GunPickup", "AmmoPickup"];

    /// <summary>Gets or sets what the task does once its pickup is taken; none until a registry keeps the pickup.</summary>
    internal TakenPickupUpdate? Taken { get; set; }

    /// <inheritdoc/>
    /// <remarks>One no registry keeps is walked as a plain container is.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Taken is { } taken)
        {
            taken.Run(this);
        }
        else
        {
            level.RunChildren(this);
        }
    }
}
