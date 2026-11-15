namespace OpenIGI.Game.World;

/// <summary>What the fields tasks export say about their task, in plain words, for both games.</summary>
/// <remarks>
/// Both games name their fields after what they hold and the binder that binds them (<c>is*</c> a truth, <c>n*</c> a count),
/// and give the same name the same meaning on every type that has it, so one list of words by field name serves every
/// type of both. A field not listed is shown by its name.
/// </remarks>
public static class MissionWording
{
    /// <summary>Gets each field's words, as they follow the task's name (<c>Terminal 12</c> <c>is hacked</c>).</summary>
    public static IReadOnlyDictionary<string, string> States { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["isOn"] = "is on",
        ["isLastOn"] = "was on a tick ago",
        ["isExploded"] = "has exploded",
        ["isDestroyed"] = "is destroyed",
        ["isHacked"] = "is hacked",
        ["isHackedThisTick"] = "was hacked this tick",
        ["isInUse"] = "is being used",
        ["isFinished"] = "has finished",
        ["isFinishedThisTick"] = "finished this tick",
        ["isPressed"] = "is pressed",
        ["isSearched"] = "has been searched",
        ["isLocked"] = "is locked",
        ["isOpen"] = "is open",
        ["isClosed"] = "is closed",
        ["isPicked"] = "has had its lock picked",
        ["isPickedUp"] = "has been picked up",
        ["isMoving"] = "is moving",
        ["isTrigger"] = "has been set off",
        ["isAlarm"] = "is sounding the alarm",
        ["isDetection"] = "has spotted someone",
        ["isSpawned"] = "has sent a guard in",
        ["isRun"] = "is running",
        ["isPlaying"] = "is playing",
        ["isComplete"] = "is complete",
        ["isFailed"] = "has failed",
        ["isSendt"] = "has been shown",
        ["isFinishedDisplay"] = "is off the screen again",
        ["isDead"] = "is dead",
        ["nActive"] = "count inside",
        ["nValue"] = "value",
        ["nTick"] = "ticks run",
        ["nSpawns"] = "guards sent in",
        ["nDetectionTime"] = "ticks watching someone",
        ["eTeam"] = "team",
        ["eDifficulty"] = "difficulty",
        ["vFloor"] = "floor",
    };
}
