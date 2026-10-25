using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The area search (<c>AISquad_SquadActionSearchArea*</c>): two teams walk a five-point pattern around the place in staggered
/// columns, a round for each difficulty level, each round wider; then it goes idle.
/// </summary>
public sealed class Igi2AiSquadSearchArea : Igi2AiSquadAction
{
    private static readonly Vector3f[] Pattern =
    [
        new(0f, 0f, 0f), new(-61440f, -61440f, 0f), new(61440f, -61440f, 0f), new(61440f, 61440f, 0f), new(-61440f, 61440f, 0f),
    ];

    private static readonly float[] Scales = [1f, 2f, 3f];
    private readonly int[] _destination = new int[2];
    private readonly int[] _patternIndex = new int[2];
    private int _step;
    private int _rounds;
    private int _scale;
    private Vector3d _center;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.SearchArea;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_destination[0]);
        words.Add(_destination[1]);
        words.Add(_patternIndex[0]);
        words.Add(_patternIndex[1]);
        words.Add(_step);
        words.Add(_rounds);
        words.Add(_scale);
        words.Add(_center);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _destination[0] = words.Int();
        _destination[1] = words.Int();
        _patternIndex[0] = words.Int();
        _patternIndex[1] = words.Int();
        _step = words.Int();
        _rounds = words.Int();
        _scale = words.Int();
        _center = words.Double3();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeTwoTeams();
        squad.ClearTargetLists();
        squad.ClearMembersPaths();
        squad.ClearMembersActionStacks();
        Igi2HumanAi first = squad.Teams[0].Leader!;
        Igi2HumanAi? second = squad.Teams[1].Leader;
        _center = change.Position;
        _destination[0] = first.CurrentNode;
        _destination[1] = second?.CurrentNode ?? -1;
        _rounds = squad.World.Difficulty + 1;
        _step = 0;
        _scale = 0;
        _patternIndex[0] = 0;
        _patternIndex[1] = 4;
        squad.Teams[0].FiringMode = 0;
        squad.Teams[1].FiringMode = 0;
        squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        squad.SetFormation(1, Igi2AiFormations.StaggeredColumn);
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanAi?[] leaders = [squad.Teams[0].Leader, squad.Teams[1].Leader];
        if (squad.FormationTimer == 0)
        {
            squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
            squad.SetFormation(1, Igi2AiFormations.StaggeredColumn);
        }

        for (int j = 0; j < 2; j++)
        {
            if (leaders[j] is { } leader && _destination[j] != leader.DestinationNode)
            {
                ResendMoves(squad, leaders);
                return true;
            }
        }

        bool arrived = leaders[0]!.CurrentNode == _destination[0];
        if (leaders[1] is { } second)
        {
            arrived &= second.CurrentNode == _destination[1];
        }

        if (!arrived)
        {
            return true;
        }

        if (_step < 5)
        {
            for (int i = 0; i < 2; i++)
            {
                if (leaders[i] is not { } leader)
                {
                    continue;
                }

                Vector3f offset = Pattern[_patternIndex[i]];
                float scale = Scales[_scale];
                Vector3d at = new(
                    (((Fpu)offset.X * scale) + _center.X).Value, (((Fpu)offset.Y * scale) + _center.Y).Value, ((Fpu)offset.Z * scale).F + _center.Z);
                if (leader.Graph is { } graph && graph.Nearest(at, false) is { } node)
                {
                    _destination[i] = node.Id;
                    leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = node.Id, IsRun = RunsOn(squad) });
                }

                if (i != 0)
                {
                    _patternIndex[1]--;
                    if (_patternIndex[1] < 0)
                    {
                        _patternIndex[1] += 5;
                    }
                }
                else
                {
                    _patternIndex[0]++;
                }
            }

            _step++;
            return true;
        }

        if (_rounds > 1)
        {
            if (squad.FirstMember is { } leader)
            {
                squad.SelectAndPlaySound(3, leader);
            }

            _rounds--;
            _step = 0;
            _patternIndex[0] = 0;
            _scale++;
            _patternIndex[1] = 4;
            return true;
        }

        squad.PopToIdle();
        return true;
    }

    private void ResendMoves(Igi2AiSquad squad, Igi2HumanAi?[] leaders)
    {
        for (int j = 0; j < 2; j++)
        {
            leaders[j]?.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = _destination[j], IsRun = RunsOn(squad) });
        }
    }
}
