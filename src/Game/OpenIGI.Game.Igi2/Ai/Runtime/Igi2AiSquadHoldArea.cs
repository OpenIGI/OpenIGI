using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The hold (<c>AISquad_SquadActionHoldArea*</c>): with no targets for five seconds it goes idle; with targets, each combat-idle
/// member with no live enemy listed looks at the nearest target's last position.
/// </summary>
public sealed class Igi2AiSquadHoldArea : Igi2AiSquadAction
{
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.HoldArea;

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

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        squad.Teams[0].FiringMode = 1;
        squad.MakeOneTeam();
        _timer = 0;
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (squad.Targets.Count <= 0)
        {
            _timer++;
            if (_timer > 150)
            {
                squad.PopToIdle();
            }

            return true;
        }

        foreach (Igi2HumanAi member in squad.Teams[0].Members.ToArray())
        {
            if (!member.IsTop(Igi2AiContent.CombatIdleInit) || member.GetTarget(visibleOnly: true) is not null)
            {
                continue;
            }

            if (member.NearestLiveEnemyListed() is not null)
            {
                continue;
            }

            Vector3d from = member.Soldier.Position;
            Igi2AiSquadTarget? nearest = null;
            float best = 0f;
            foreach (Igi2AiSquadTarget target in squad.Targets)
            {
                float distance = Igi2AiMath.Distance(from, target.LastPosition);
                if (nearest is null || distance < best)
                {
                    best = distance;
                    nearest = target;
                }
            }

            if (nearest is not null)
            {
                member.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.LookAtPosition, Position = nearest.LastPosition });
            }
        }

        return true;
    }
}
