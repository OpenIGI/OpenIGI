using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// Which of the four sweep ticks each detection record a level loads takes -- the round-robin counter
/// <c>dword_5C8B08</c> that <c>0x4896C0</c> hands out, one value per record, as each one initialises.
/// </summary>
/// <remarks>
/// <para>
/// One counter serves every kind of record, and the order is the level's: the camera lenses as they are built
/// (<c>0x4415C0</c>), then a post-load walk of the whole task tree, dormant containers included -- each HumanAI as
/// its init (<c>0x4505D0</c>) runs, each Car and Heli before its children (<c>0x42F530</c>, <c>0x432950</c>), each
/// AIStationaryGunHolder after them (<c>0x46E7F0</c> post-loads its children first). A plain StationaryGun has no
/// record of its own -- mission 4's gun 775 took none. A GuardGenerator's children are templates and are not
/// post-loaded; a clone takes the next value when it is made.
/// </para>
/// <para>
/// Probed on mission 4 (hook on <c>0x4896C0</c>): 48 records on tick 0, cutscene HumanAIs and both helicopters among
/// them, so Harrison's squad 400..405 took phases 0, 1, 2, 3, 0, 1 and guard 411 phase 3. The port had numbered
/// soldiers by their own load order alone and vehicles on a counter of their own, which put 411 on phase 2 and squad
/// member 404 on phase 2 -- and 404 saw guard 411 a sweep before retail's did.
/// </para>
/// </remarks>
public static class AiVisionPhases
{
    /// <summary>How many sweep phases the counter cycles through.</summary>
    public const int PhaseCount = AiDetectionSchedule.PhaseCount;

    /// <summary>Assigns every detection record a level loads its phase.</summary>
    /// <param name="levelRoot">The level's root task.</param>
    /// <returns>Each record owner's phase, keyed by its task, and the counter's value once the load is done.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static (IReadOnlyDictionary<GameTask, int> Phases, int Counter) Assign(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        Dictionary<GameTask, int> phases = [];
        int counter = 0;

        // The lenses initialise as they are constructed, before any post-load runs.
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (IsType(task, "SCamera"))
            {
                counter = (counter + 1) % PhaseCount;
            }
        }

        Visit(levelRoot);
        return (phases, counter);

        void Visit(GameTask parent)
        {
            foreach (GameTask task in parent.Children)
            {
                if (IsType(task, "GuardGenerator"))
                {
                    continue;
                }

                bool before = task is HumanAiTask || IsType(task, "Car") || IsType(task, "Heli");
                if (before)
                {
                    phases[task] = counter;
                    counter = (counter + 1) % PhaseCount;
                }

                Visit(task);

                if (IsType(task, "AIStationaryGunHolder"))
                {
                    phases[task] = counter;
                    counter = (counter + 1) % PhaseCount;
                }
            }
        }
    }

    private static bool IsType(GameTask task, string name) =>
        string.Equals(task.Type?.Name, name, StringComparison.Ordinal);
}
