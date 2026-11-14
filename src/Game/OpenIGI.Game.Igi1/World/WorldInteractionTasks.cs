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
public abstract class WorldInteractionTask : DeclaredLevelTask, IExplodeObjectTask
{
    /// <summary>Gets or sets the level's interactions, whose clock the update runs by, or <see langword="null"/> before they are built.</summary>
    internal WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the interaction this task owns, or <see langword="null"/> for a task that has none.</summary>
    internal WorldInteraction? Item { get; set; }

    /// <inheritdoc/>
    /// <remarks>Set for the types the original registers under <c>ExplodeObject</c>: a terminal, a timed action that can be blown up, a generator.</remarks>
    public ExplodeObjectUpdate? Explosion { get; set; }

    /// <summary>Gets the expression that says whether the task is on ("On Expression", or "On" where a type names it so).</summary>
    public virtual string OnExpression => DeclaredTaskValues.TextOrFallback(
        this, "On Expression", DeclaredTaskValues.TextOrFallback(this, "On", string.Empty));

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
            Explosion?.Run();
        }
    }

    /// <summary>Gets a value indicating whether the task has an update of its own to run.</summary>
    private protected virtual bool HasUpdate => Item is not null;

    /// <summary>Runs the task's update and its children in the type's own order.</summary>
    /// <param name="level">The level's run.</param>
    /// <param name="now">The interactions' clock, in ticks.</param>
    private protected virtual void RunInWalk(LevelRun level, int now)
    {
        // The ExplodeObject's handler first, where the type is built on one: its children, then its own blast.
        level.RunChildren(this);
        Explosion?.Run();
        RunItem(Item!, now);
    }

    /// <summary>Runs the type's own update on the interaction it owns.</summary>
    /// <param name="item">The interaction.</param>
    /// <param name="now">The interactions' clock, in ticks.</param>
    private protected abstract void RunItem(WorldInteraction item, int now);
}

/// <summary>A switch (<c>Switch</c>): its <c>ExplodeObject</c>'s run, then its own.</summary>
public sealed class SwitchTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Switch";

    /// <summary>Gets a value indicating whether a round breaks the switch ("Destructable").</summary>
    public bool IsDestructable => DeclaredTaskValues.BoolStrict(this, "Destructable");

    /// <inheritdoc/>
    private protected override void RunItem(WorldInteraction item, int now) => item.RunSwitch(now);
}

/// <summary>A terminal (<c>Terminal</c>): its <c>ExplodeObject</c>'s run, then its own. The types built on it run it first.</summary>
public class TerminalTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Terminal";

    /// <summary>Gets how long the terminal takes to hack, in seconds ("Hack Time (s)").</summary>
    public float HackSeconds => DeclaredTaskValues.RealFromNumber(this, "Hack Time (s)", 4f);

    /// <inheritdoc/>
    private protected sealed override void RunItem(WorldInteraction item, int now) => item.RunTimed(now);
}

/// <summary>A cabinet (<c>Cabinet</c>): its children, then its own update.</summary>
public sealed class CabinetTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Cabinet";

    /// <summary>Gets how long the cabinet takes to search, in seconds ("Search Time (s)").</summary>
    public float SearchSeconds => DeclaredTaskValues.RealFromNumber(this, "Search Time (s)", 4f);

    /// <inheritdoc/>
    private protected override void RunItem(WorldInteraction item, int now) => item.RunCabinet(now);
}

/// <summary>
/// A timed action (<c>GenericTBA</c>, and <c>PlaceExplosiveTBA</c> built on it): one that can be blown up runs its
/// <c>ExplodeObject</c>'s run and then its own; one that cannot runs only its own, and no child.
/// </summary>
public abstract class TimedActionTask : WorldInteractionTask
{
    /// <summary>Gets how long the action takes, in seconds ("Activate Time (s)").</summary>
    public float ActivateSeconds => DeclaredTaskValues.RealFromNumber(this, "Activate Time (s)", 4f);

    /// <summary>Gets the sprite the prompt shows ("Click to select sprite"), as the level names it.</summary>
    public string ActionSprite => DeclaredTaskValues.TextOrFallback(this, "Click to select sprite", "a_c4.spr");

    /// <summary>Gets a value indicating whether the action can be blown up, and so runs its <c>ExplodeObject</c> ("Explodable").</summary>
    public bool IsExplodable => DeclaredTaskValues.BoolStrict(this, "Explodable");

    /// <summary>Gets the animation the body plays while at it ("Activate Anim"), or -1 for none.</summary>
    /// <remarks>An Int16 parameter, which the task holds as a short.</remarks>
    public int ActivateAnimation => DeclaredTaskValues.IntTruncating(this, "Activate Anim", -1);

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        if (Item!.IsExplodable)
        {
            base.RunInWalk(level, now);
        }
        else
        {
            RunItem(Item, now);
        }
    }

    /// <inheritdoc/>
    private protected sealed override void RunItem(WorldInteraction item, int now) => item.RunTimed(now);
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

    /// <summary>Gets the id of the task the plant puts in place ("Task ID to place"), or -1 for none.</summary>
    public int TaskIdToPlace => DeclaredTaskValues.IntTruncating(this, "Task ID to place", -1);

    /// <summary>Gets how far from the eye the spot is offered, in metres ("Activate radius (meters)").</summary>
    /// <remarks>The constructor (<c>0x4389A0</c>) leaves it at 0.75 for a task that names none.</remarks>
    public float ActivateRadiusMetres => DeclaredTaskValues.RealFromNumber(
        this, "Activate radius (meters)", DeclaredTaskValues.RealFromNumber(this, "Activate radius(meters)", 0.75f));
}

/// <summary>A fence (<c>Fence</c>): its own update, then its children.</summary>
public sealed class FenceTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Fence";

    /// <summary>Gets the expression that says whether the fence is live ("Electric Expression").</summary>
    public string ElectricExpression => DeclaredTaskValues.TextOrFallback(this, "Electric Expression", string.Empty);

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        RunItem(Item!, now);
        level.RunChildren(this);
    }

    /// <inheritdoc/>
    private protected override void RunItem(WorldInteraction item, int now) => item.RunFence();
}

/// <summary>A mounted gun (<c>StationaryGun</c>): its own update, the gun's while nobody mans it, then its children.</summary>
public sealed class StationaryGunTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "StationaryGun";

    /// <summary>Gets how far the gun lifts, in degrees ("Max up angle").</summary>
    public float MaxUpAngle => DeclaredTaskValues.RealFromNumber(this, "Max up angle", 25f);

    /// <summary>Gets how far the gun dips, in degrees ("Max down angle").</summary>
    public float MaxDownAngle => DeclaredTaskValues.RealFromNumber(this, "Max down angle", -25f);

    /// <summary>Gets how far the gun turns to either side, in degrees ("Max sideways angle").</summary>
    public float MaxSidewaysAngle => DeclaredTaskValues.RealFromNumber(this, "Max sideways angle", 90f);

    /// <summary>Gets the weapon the stand carries ("Weapon ID").</summary>
    public string WeaponId => DeclaredTaskValues.TextOrFallback(this, "Weapon ID", "WEAPON_ID_M2HB");

    /// <summary>Gets the rounds the gun starts with ("Ammo"), or -1 for no limit.</summary>
    public int Ammo => (int)DeclaredTaskValues.RealFromNumber(this, "Ammo", -1f);

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        RunItem(Item!, now);
        if (Item!.StationaryGun is { IsOccupied: false } emptyGun)
        {
            emptyGun.TickEmpty();
        }

        level.RunChildren(this);
    }

    /// <inheritdoc/>
    private protected override void RunItem(WorldInteraction item, int now) => item.RunHeld();
}

/// <summary>A wire to slide down (<c>Wire</c>): its own update, and no child.</summary>
public sealed class WireTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Wire";

    /// <summary>Gets where the wire ends ("Stop position"), or <see langword="null"/> where the level gives none.</summary>
    public OpenIGI.Core.Numerics.Vector3d? StopPosition =>
        TryGetValue("Stop position", out object? value) && value is OpenIGI.Core.Numerics.Vector3d stop ? stop : null;

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now) => RunItem(Item!, now);

    /// <inheritdoc/>
    private protected override void RunItem(WorldInteraction item, int now) => item.RunHeld();
}

/// <summary>A generator (<c>Generator</c>): its <c>ExplodeObject</c>'s run, then its power (<c>0x43B500</c>).</summary>
public sealed class GeneratorTask : WorldInteractionTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Generator";

    /// <summary>Gets or sets the generator's power, or <see langword="null"/> before the level's interactions are built.</summary>
    internal GeneratorPowerState? Power { get; set; }

    /// <summary>Gets the definition it hums with while on ("Run sound").</summary>
    public string RunSound => DeclaredTaskValues.TextOrFallback(this, "Run sound", string.Empty);

    /// <summary>Gets the definition played as it goes off ("Shutdown sound").</summary>
    public string ShutdownSound => DeclaredTaskValues.TextOrFallback(this, "Shutdown sound", string.Empty);

    /// <summary>Gets the expression that says whether it is on ("On expression").</summary>
    public override string OnExpression => DeclaredTaskValues.TextOrFallback(
        this, "On expression", DeclaredTaskValues.TextOrFallback(this, "On Expression", string.Empty));

    /// <summary>Gets a value indicating whether it comes back on by itself ("Use backup timer").</summary>
    public bool UsesBackupTimer => DeclaredTaskValues.BoolStrict(this, "Use backup timer");

    /// <summary>Gets how long it stays off before it comes back on, in seconds ("Seconds until power backup").</summary>
    public float BackupSeconds => DeclaredTaskValues.RealFromNumber(this, "Seconds until power backup", 10f);

    /// <inheritdoc/>
    private protected override bool HasUpdate => Power is not null;

    /// <inheritdoc/>
    private protected override void RunInWalk(LevelRun level, int now)
    {
        level.RunChildren(this);
        Explosion?.Run();
        Power!.Tick();
    }

    /// <inheritdoc/>
    /// <remarks>A generator owns no interaction: nobody works it by hand.</remarks>
    private protected override void RunItem(WorldInteraction item, int now)
    {
    }
}
