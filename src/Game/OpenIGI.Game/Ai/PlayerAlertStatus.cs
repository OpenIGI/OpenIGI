namespace OpenIGI.Game.Ai;

/// <summary>How far the soldiers are onto the player, as a display can say it in a word.</summary>
/// <remarks>Ordered by how much it matters: where several hold at once, the greatest is the one told.</remarks>
public enum PlayerAlertStatus
{
    /// <summary>No soldier is onto the player.</summary>
    None,

    /// <summary>A soldier who has lost sight of the player is still looking for him.</summary>
    Searching,

    /// <summary>A soldier is making out a figure he has glimpsed, but is not yet sure.</summary>
    Suspicious,

    /// <summary>An alarm is sounding.</summary>
    Alarm,

    /// <summary>A soldier sees the player and is sure of him.</summary>
    Detected,
}
