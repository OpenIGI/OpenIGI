using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's computer's objectives, log, contacts and marked places, as the remastered computer lists them. See <see cref="ITitlePlayerComputer"/>.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayerComputer
{
    private ComputerObjectivesView? _computerObjectives;

    /// <inheritdoc/>
    public IComputerObjectives? ComputerObjectives => Computer is null ? null : _computerObjectives ??= new ComputerObjectivesView(this);

    /// <inheritdoc/>
    /// <remarks>The computer's map is there while the level has the computer enabled, as its own screens offer the map then.</remarks>
    public bool ComputerMapAvailable => Computer is { IsEnabled: true };

    /// <summary>
    /// Where an objective is on the map: the task it links to, where its camera target stands -- where the objectives screen's
    /// button sends the map (<c>ComputerObjectives_ButtonItemActivateCB</c>).
    /// </summary>
    /// <param name="objective">The objective.</param>
    /// <returns>The place, or <see langword="null"/> for an objective that links to nothing placed.</returns>
    private Vector3d? ObjectivePlace(Igi2ComputerObjective objective) =>
        objective.LinkToPosition != -1
            ? (_mapWorld ??= new Igi2ComputerMapWorld(this)).Task(objective.LinkToPosition)?.CameraTargetPosition
            : null;

    private readonly List<Vector3d> _computerContacts = [];
    private readonly List<ComputerMapHighlight> _computerPlaces = [];

    /// <inheritdoc/>
    /// <remarks>Every human of another side than the player's, alive and active, as the game's own map marks them.</remarks>
    public IReadOnlyList<Vector3d> ComputerMapContacts
    {
        get
        {
            _computerContacts.Clear();
            foreach (Igi2Human human in MapHumans)
            {
                if (!ReferenceEquals(human, Human) && !human.IsDead && human.IsActive && human.Team != Human.Team)
                {
                    _computerContacts.Add(human.Position);
                }
            }

            return _computerContacts;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The level's highlights that are on (<c>ComputerHilight</c>): one on each task a highlight points at, where its camera target
    /// stands, or where the highlight itself is when it points at nothing.
    /// </remarks>
    public IReadOnlyList<ComputerMapHighlight> ComputerMapHighlights
    {
        get
        {
            _computerPlaces.Clear();
            if (Computer is not { } computer)
            {
                return _computerPlaces;
            }

            _mapWorld ??= new Igi2ComputerMapWorld(this);
            foreach (Igi2ComputerHilightTask hilight in computer.Hilights)
            {
                if (!hilight.IsOn)
                {
                    continue;
                }

                string title = hilight.TitleText.Length == 0 ? string.Empty : Strings?.Get(hilight.TitleText) ?? hilight.TitleText;
                string sprite = hilight.Sprite[(hilight.Sprite.LastIndexOf(':') + 1)..];
                IReadOnlyList<int> ids = hilight.TaskIds;
                foreach (int id in ids)
                {
                    if (_mapWorld.Task(id)?.CameraTargetPosition is { } position)
                    {
                        _computerPlaces.Add(new ComputerMapHighlight(position, hilight.TitleText, sprite) { Title = title });
                    }
                }

                if (ids.Count == 0)
                {
                    _computerPlaces.Add(new ComputerMapHighlight(hilight.Position, hilight.TitleText, sprite) { Title = title });
                }
            }

            return _computerPlaces;
        }
    }

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
                    Vector3d? place = player.ObjectivePlace(objective);
                    _objectives.Add(new MissionObjective(objective.TextResource, objective.LinkToPosition, place, state)
                    {
                        Text = player.Strings?.Get(objective.TextResource) ?? objective.TextResource,
                        Slot = i,
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
