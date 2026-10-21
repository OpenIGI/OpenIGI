using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>The idle action (<c>AISquad_SquadActionIdle*</c>): one team firing on order, no targets, no danger, members cleared.</summary>
public sealed class Igi2AiSquadIdle : Igi2AiSquadAction
{
    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Idle;

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        squad.MakeOneTeam();
        squad.ClearTargetLists();
        squad.Flags &= ~6;
        squad.Teams[0].FiringMode = 1;
        squad.ClearMembersActionStacks();
        return true;
    }
}
