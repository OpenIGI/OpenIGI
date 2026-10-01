namespace OpenIGI.Game.Ai;

/// <summary>The flags an <c>AIAction_*</c> call can carry.</summary>
/// <remarks>Registered literally as 0 and 1 by <c>0x44EEB0</c>.</remarks>
[Flags]
public enum AiActionFlagWord
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The action can be displaced by a higher-priority one rather than having to finish.</summary>
    Pushable = 1,
}
