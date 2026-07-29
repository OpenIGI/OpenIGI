namespace OpenIGI.Game.Ai;

/// <summary>When an AI is willing to go and raise an alarm.</summary>
/// <remarks>
/// Registered literally as 0 and 1 by <c>0x44EEB0</c>. Which of the two an AI has is set by
/// <c>AIFunction_SetAlarmAccess</c> and read back by <c>AIFunction_GetAlarmAccess</c>, so it is script
/// state rather than a fixed property of the archetype.
/// </remarks>
public enum AiAlarmAccess
{
    /// <summary>Run for the alarm rather than engaging.</summary>
    BeforeCombat = 0,

    /// <summary>Fight first, and raise the alarm afterwards.</summary>
    AfterCombat = 1,
}
