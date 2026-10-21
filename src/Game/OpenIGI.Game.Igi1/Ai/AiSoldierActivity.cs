namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What an AI soldier is currently doing.</summary>
public enum AiSoldierActivity
{
    /// <summary>Standing still, waiting for its next patrol leg.</summary>
    Idle,

    /// <summary>Walking a route between graph nodes.</summary>
    Patrolling,

    /// <summary>It can see an enemy and has stopped to face it.</summary>
    Engaging,

    /// <summary>Firing at a script-named task or graph node.</summary>
    FiringAtTask,

    /// <summary>Running away from the event that installed <c>AIAction_RunPanicking</c>.</summary>
    Panicking,

    /// <summary>Holding the prone channel for <c>AIAction_FallFlat</c>.</summary>
    FallenFlat,

    /// <summary>Playing animation 38 for the scripted stun duration.</summary>
    Stunned,

    /// <summary>Dead.</summary>
    Dead,
}
