using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's computer's objectives and log, as the remastered computer lists them. See <see cref="ITitlePlayerComputer"/>.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayerComputer
{
    private ComputerObjectivesView? _computerObjectives;

    /// <inheritdoc/>
    public IComputerObjectives? ComputerObjectives => Computer is null ? null : _computerObjectives ??= new ComputerObjectivesView(this);

    /// <inheritdoc/>
    /// <remarks>The computer's map is there while the level has the computer enabled, as its own screens offer the map then.</remarks>
    public bool ComputerMapAvailable => Computer is { IsEnabled: true };

    // The objectives the level's computer defines, each with its text in the language and its state; and the lines said.
    private sealed class ComputerObjectivesView(Igi2TitlePlayer player) : IComputerObjectives
    {
        private readonly List<MissionObjective> _objectives = [];
        private readonly List<MissionComputerLogEntry> _log = [];
        private long _builtTick = -1;

        public IReadOnlyList<MissionObjective> Objectives
        {
            get
            {
                Build();
                return _objectives;
            }
        }

        public IReadOnlyList<MissionComputerLogEntry> Log
        {
            get
            {
                Build();
                return _log;
            }
        }

        private void Build()
        {
            if (_builtTick == player._ticks)
            {
                return;
            }

            _builtTick = player._ticks;
            _objectives.Clear();
            if (player.Computer is { } computer)
            {
                for (int i = 0; i < Igi2ComputerDisplay.ObjectiveCount; i++)
                {
                    if (computer.GetObjective(i) is not { TextResource.Length: > 0 } objective)
                    {
                        continue;
                    }

                    MissionObjectiveState state = objective.IsFailed ? MissionObjectiveState.Failed
                        : objective.IsComplete ? MissionObjectiveState.Complete : MissionObjectiveState.Incomplete;
                    _objectives.Add(new MissionObjective(objective.TextResource, objective.LinkToPosition, null, state)
                    {
                        Text = player.Strings?.Get(objective.TextResource) ?? objective.TextResource,
                    });
                }
            }

            _log.Clear();
            foreach (string line in player.StatusMessages.Log)
            {
                _log.Add(new MissionComputerLogEntry(line) { Text = line });
            }
        }
    }
}
