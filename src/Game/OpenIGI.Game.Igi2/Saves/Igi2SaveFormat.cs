using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// I.G.I. 2's save games as the shared engine writes and reads them (<see cref="SaveScript"/>): version 63, the tasks their own
/// states, and the level's panes of glass kept as lines of their own.
/// </summary>
public static class Igi2SaveFormat
{
    /// <summary>The version this game writes, and the least it reads: <c>63</c> (<c>SaveGame_SaveInfoScriptFunction</c>).</summary>
    public const int Version = 63;

    /// <summary>What a save of a level is written and read with.</summary>
    /// <param name="glass">The level's panes of glass, written after the tasks and put back by a load, or <see langword="null"/>.</param>
    /// <param name="pickups">The level's pickups, whose dropped weapons are kept beside the tasks', or <see langword="null"/>.</param>
    /// <param name="root">A task of the level, by which its <c>Dynamic</c> task is found; needed with <paramref name="pickups"/>.</param>
    /// <param name="mines">The mines laid in the level, kept beneath its <c>Dynamic</c> task, or <see langword="null"/>.</param>
    /// <returns>The context.</returns>
    public static SaveContext Context(
        Igi2GlassContainer? glass = null,
        Pickups.Igi2PickupList? pickups = null,
        Engine.Tasks.GameTask? root = null,
        Player.Igi2LevelMines? mines = null) =>
        new()
        {
            Version = Version,
            States = new Igi2SaveStates(pickups, root is null ? null : Igi2SaveStates.DynamicOf(root), mines),
            Sections = glass is null ? [] : [new GlassSection(glass)],
        };

    /// <summary>A header this game writes.</summary>
    /// <param name="missionId">The mission, <c>Game_iMissionID</c>.</param>
    /// <param name="ticks">The player's mission time.</param>
    /// <returns>The header.</returns>
    public static SaveInfo Info(int missionId, int ticks) => new(Version, missionId, ticks, true);

    // GlassContainer("<key>", <state>): a pane's breakage, gathered as the script runs (GlassContainer_DeSerializeScriptFunction)
    // and put back once it is done (GlassContainer_DeSerializeDoneHandler).
    private sealed class GlassSection(Igi2GlassContainer glass) : ISaveSection
    {
        private readonly Dictionary<string, int> _panes = new(StringComparer.Ordinal);

        public string CallName => Igi2GlassContainer.Call;

        public string Write() => glass.SaveLines();

        public void Read(ISaveArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            if (arguments.Count == 2)
            {
                _panes[arguments.GetText(0)] = arguments.GetInt32(1);
            }
        }

        public void LoadDone()
        {
            glass.Restore(_panes);
            _panes.Clear();
        }
    }
}
