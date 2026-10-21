using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What a squad does, <c>AISquadActions_e</c>, the value <c>AISquad_GetState</c> gives the script.</summary>
public enum Igi2AiSquadActionKind
{
    /// <summary>Nothing.</summary>
    Idle = 0,

    /// <summary>Hold where it is, looking at its targets.</summary>
    HoldArea = 1,

    /// <summary>Send its leader to see what happened.</summary>
    Inquire = 2,

    /// <summary>Search around a position in two teams.</summary>
    SearchArea = 3,

    /// <summary>Move to a node.</summary>
    Move = 4,

    /// <summary>Advance on a target, one team covering the other.</summary>
    Leapfrog = 5,

    /// <summary>Walk a patrol.</summary>
    Patrol = 6,

    /// <summary>Move some way in a direction and search.</summary>
    MoveSearchInDirection = 7,

    /// <summary>Look a direction, then move that way.</summary>
    LookSearchInDirection = 8,

    /// <summary>Take cover from a danger, then search.</summary>
    Danger = 9,
}
