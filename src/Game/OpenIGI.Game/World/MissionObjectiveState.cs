namespace OpenIGI.Game.World;

/// <summary>The three objective states IGI1's objectives screen labels.</summary>
public enum MissionObjectiveState
{
    /// <summary>The objective has neither completed nor failed.</summary>
    Incomplete,
    /// <summary>The objective's authored completion expression is true.</summary>
    Complete,
    /// <summary>The objective's authored failure expression is true.</summary>
    Failed,
}
