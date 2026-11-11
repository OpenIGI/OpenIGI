using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// One of the things a human works by hand in Project I.G.I. -- a switch, a terminal, a cabinet, a timed action, a fence,
/// a wire, a mounted gun -- or a generator: a task that runs its own update where the level's walk reaches it.
/// </summary>
/// <remarks>
/// <para>
/// Each type's run handler orders its own work and its children's in its own way, so each type is a class that says which:
/// the types built on <c>ExplodeObject</c> run that first, which is their children's turn, and then themselves (this
/// class's order); a fence and a mounted gun run themselves and then their children; a wire runs no child at all.
/// </para>
/// <para>
/// The state the update works on is still kept beside the task (<see cref="WorldInteraction"/>, which
/// <see cref="WorldInteractionRegistry"/> collects); the task holds the one it owns, so the walk asks nobody whose it is.
/// </para>
/// </remarks>
public abstract class WorldInteractionTask : DeclaredLevelTask
{
    /// <summary>Gets or sets the level's interactions, whose clock the update runs by, or <see langword="null"/> before they are built.</summary>
    internal WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the interaction this task owns, or <see langword="null"/> for a task that has none.</summary>
    internal WorldInteraction? Item { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Interactions is { RunsInWalk: true } interactions && HasUpdate)
        {
            RunInWalk(level, interactions.Now);
        }
        else
        {
            level.RunChildren(this);
        }
    }

    /// <summary>Gets a value indicating whether the task has an update of its own to run.</summary>
    private protected virtual bool HasUpdate => Item is not null;

    /// <summary>Runs the task's update and its children in the type's own order.</summary>
    /// <param name="level">The level's run.</param>
    /// <param name="now">The interactions' clock, in ticks.</param>
    private protected virtual void RunInWalk(LevelRun level, int now)
    {
        level.RunChildren(this);
        Item!.Run(now);
    }
}

/// <summary>A switch (<c>Switch</c>): its <c>ExplodeObject</c>'s run, then its own.</summary>
public sealed class SwitchTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Switch";
}

/// <summary>A terminal (<c>Terminal</c>): its <c>ExplodeObject</c>'s run, then its own.</summary>
public sealed class TerminalTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Terminal";
}

/// <summary>A cabinet (<c>Cabinet</c>): its children, then its own update.</summary>
public sealed class CabinetTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Cabinet";
}

/// <summary>
/// A timed action (<c>GenericTBA</c>, and <c>PlaceExplosiveTBA</c> built on it): one that can be blown up runs its
/// <c>ExplodeObject</c>'s run and then its own; one that cannot runs only its own, and no child.
/// </summary>
public abstract class TimedActionTask : WorldInteractionTask
{
    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        if (Item!.IsExplodable)
        {
            base.RunInWalk(level, now);
        }
        else
        {
            Item.Run(now);
        }
    }
}

/// <summary>A timed action (<c>GenericTBA</c>).</summary>
public sealed class GenericTbaTask : TimedActionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GenericTBA";
}

/// <summary>A place to plant a charge (<c>PlaceExplosiveTBA</c>).</summary>
public sealed class PlaceExplosiveTbaTask : TimedActionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "PlaceExplosiveTBA";
}

/// <summary>A fence (<c>Fence</c>): its own update, then its children.</summary>
public sealed class FenceTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Fence";

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        Item!.Run(now);
        level.RunChildren(this);
    }
}

/// <summary>A mounted gun (<c>StationaryGun</c>): its own update, the gun's while nobody mans it, then its children.</summary>
public sealed class StationaryGunTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "StationaryGun";

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        Item!.Run(now);
        if (Item.StationaryGun is { IsOccupied: false } emptyGun)
        {
            emptyGun.TickEmpty();
        }

        level.RunChildren(this);
    }
}

/// <summary>A wire to slide down (<c>Wire</c>): its own update, and no child.</summary>
public sealed class WireTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Wire";

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now) => Item!.Run(now);
}

/// <summary>A generator (<c>Generator</c>): its <c>ExplodeObject</c>'s run, then its power (<c>0x43B500</c>).</summary>
public sealed class GeneratorTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Generator";

    /// <summary>Gets or sets the generator's power, or <see langword="null"/> before the level's interactions are built.</summary>
    internal GeneratorPowerState? Power { get; set; }

    /// <inheritdoc/>
    private protected override bool HasUpdate => Power is not null;

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        level.RunChildren(this);
        Power!.Tick();
    }
}
