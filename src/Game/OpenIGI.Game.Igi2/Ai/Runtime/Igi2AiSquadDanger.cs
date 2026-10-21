using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The danger (<c>AISquad_SquadActionDanger*</c>): after half a second everyone takes cover from it; once no one is still moving
/// or activating for a second, each sweeps its eyes (crouched on a coin toss, unless its cover forbids); after the danger timeout the
/// squad is told <c>AIEVENT_DANGER_TIMEOUT</c> (38) and, a tick later, goes idle. A danger during a danger skips to the end.
/// </summary>
public sealed class Igi2AiSquadDanger : Igi2AiSquadAction
{
    private Vector3d _position;
    private Vector3f _direction;
    private int _stage;
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Danger;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_position);
        words.Add(_direction);
        words.Add(_stage);
        words.Add(_timer);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _position = words.Double3();
        _direction = words.Single3();
        _stage = words.Int();
        _timer = words.Int();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        _stage = 0;
        _timer = 0;
        _direction = change.Direction;
        _position = change.Position;
        if ((squad.Flags & 2) != 0)
        {
            _stage = 2;
            _timer = -1 - Fpu.ToInt32((Fpu)squad.DangerTimeout * -30.0);
        }
        else
        {
            squad.MakeOneTeam();
            squad.ClearMembersPaths();
            squad.PopMembersActionStacks();
        }

        squad.Flags |= 2;
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        switch (_stage)
        {
            case 0:
                if (_timer < 15)
                {
                    _timer++;
                    return true;
                }

                _timer = 0;
                _stage = 1;
                squad.ClearMembersPaths();
                squad.PopMembersActionStacks();
                squad.SendOrderToSquad(new Igi2AiSquadOrder
                {
                    Kind = Igi2AiSquadOrderKind.TakeCover,
                    Delay = squad.OrderDelay(),
                    Position = _position,
                    // The order's direction is read from what the C left on its stack past the position: its first value is the
                    // danger's third, and the rest are whatever was there before.
                    Direction = new Vector3f(_direction.Z, 0f, 0f),
                });
                return true;
            case 1:
                foreach (Igi2HumanAi member in squad.Teams[0].Members)
                {
                    if (!member.IsEmptyMovementQueue || member.IsTop(Igi2AiContent.ActivateInit))
                    {
                        return true;
                    }
                }

                int waited = _timer + 1;
                _timer++;
                if (waited > 30)
                {
                    _stage = 2;
                    _timer = 0;
                }

                return true;
            case 2:
                _timer++;
                if (_timer == 1)
                {
                    foreach (Igi2HumanAi member in squad.Teams[0].Members.ToArray())
                    {
                        bool crouch = (squad.World.Random.NextRaw() & 1) == 0;
                        if (member.Actions.Top is { } top && top.Run == Igi2AiContent.TakeCoverRun && !Igi2AiContent.CoverAllowsCrouch(top))
                        {
                            crouch = false;
                        }

                        member.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.VisualSweep, IsCrouch = crouch });
                    }

                    return true;
                }

                int timeout = Fpu.ToInt32((Fpu)squad.DangerTimeout * 30.0);
                if (_timer == timeout)
                {
                    squad.PopMembersActionStacks();
                    squad.ReceiveEvent(new Igi2AiSquadEvent { Kind = 38, Position = _position, Direction = _direction });
                    return true;
                }

                if (_timer > timeout)
                {
                    squad.PopMembersActionStacks();
                    squad.PopToIdle();
                    return false;
                }

                return true;
            default:
                return true;
        }
    }
}
