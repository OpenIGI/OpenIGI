namespace OpenIGI.Game.World;

/// <summary>
/// The four moments a door makes a noise, as the door update <c>0x446D10</c> decides them.
/// </summary>
/// <remarks>
/// The door keeps <c>Last</c>-shadows of both end states — <c>+410</c>/<c>+412</c> for fully open,
/// <c>+409</c>/<c>+411</c> for fully closed — and tests the rising edge of each. The travelling sound
/// is different in kind: it is started when the door is at neither end and no voice of its own is
/// already running, and the task handle is kept at <c>+736</c> so there is something to stop.
/// </remarks>
public enum DoorSoundEvent
{
    /// <summary>The door has just reached fully open. Plays <see cref="PlacedDoor.OpenSound"/>.</summary>
    Opened,

    /// <summary>The door has just reached fully closed. Plays <see cref="PlacedDoor.CloseSound"/>.</summary>
    Closed,

    /// <summary>The door has begun travelling. Starts <see cref="PlacedDoor.MoveSound"/>.</summary>
    StartedMoving,

    /// <summary>The door has stopped travelling, and its travelling sound should end.</summary>
    StoppedMoving,
}
