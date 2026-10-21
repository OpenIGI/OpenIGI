using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The inquiry (<c>AISquad_SquadActionInquire*</c>): the member who heard leads; everyone looks at the place, the leader goes to
/// see; after the inquire timeout the squad is told <c>AIEVENT_INQUIRE_TIMEOUT</c> (29) at its first target or its leader.
/// </summary>
public sealed class Igi2AiSquadInquire : Igi2AiSquadAction
{
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Inquire;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_timer);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _timer = words.Int();
    }

    /// <summary>Gets the place being looked into.</summary>
    public Vector3d Position { get; private set; }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        _timer = 0;
        Position = change.Position;
        squad.MakeOneTeam();
        squad.Teams[0].FiringMode = 0;
        int index = change.Receiver is { } receiver ? squad.Teams[0].Members.IndexOf(receiver) : -1;
        if (index < 0)
        {
            squad.PopToIdle();
            return false;
        }

        squad.Teams[0].MakeLeader(index);
        squad.SendOrderToSquad(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.LookAtPosition, Delay = squad.OrderDelay(), Position = Position });
        int delay = squad.OrderDelay();
        squad.FirstMember?.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.Inquire, Delay = delay, Position = change.Position });
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (_timer >= Fpu.ToInt32((Fpu)squad.InquireTimeout * 30.0))
        {
            Vector3d at = squad.Targets.Count > 0 ? squad.Targets[0].LastPosition : squad.FirstMember!.Soldier.Position;
            squad.ReceiveEvent(29, at);
            return true;
        }

        _timer++;
        return true;
    }
}
