using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The sounds of a player's timed work by hand: the loop while it picks a lock or types, and the cue as it is done.</summary>
public static class TimedActionSounds
{
    /// <summary>The loop a timed state holds while it lasts.</summary>
    /// <param name="state">The interaction's locomotion state.</param>
    /// <returns>The loop's sound definition, or <see langword="null"/> for a state with none.</returns>
    public static string? LoopOf(int state) => state switch
    {
        HumanInteractionDispatch.PickLockState => "picklock_loop",
        HumanInteractionDispatch.ComputerState => "typecomp_loop",
        _ => null,
    };

    /// <summary>The cue played as a timed state completes (<c>0x40C160</c>, <c>0x40C870</c>): never on a cancel.</summary>
    /// <param name="state">The state that completed.</param>
    /// <returns>The cue's sound definition, or <see langword="null"/> for a state with none.</returns>
    public static string? EndOf(int state) => state switch
    {
        HumanInteractionDispatch.PickLockState => "pickloop_end",
        HumanInteractionDispatch.ComputerState => "typecomp_end",
        _ => null,
    };

    /// <summary>The grenade's cook tick, held while a throw charges (<c>0x40F590</c> starts it, <c>0x40F640</c> stops it).</summary>
    public const string GrenadeCook = "grenade_tick_1";

    /// <summary>The under-barrel launcher's report (<c>GunM16A2</c>'s native callback, <c>0x473510</c>).</summary>
    public const string M203Launch = "m203_launch_1";
}
