namespace OpenIGI.Game.Igi2.Interaction;

/// <content>What a door is told to do, as a co-op server shares it: open or shut, and which way a two-way door swings.</content>
public sealed partial class Igi2DoorTask
{
    /// <summary>Gets a value indicating whether the door is told to stand open, <c>isOpenDoor</c>: what a player's or a soldier's use toggles.</summary>
    public bool IsCommandedOpen => _isOpenDoor != 0;

    /// <summary>Gets a value indicating whether the door swings the other way from the way it was authored: a two-way door opened from its far side.</summary>
    public bool SwingsBack => MaxAngle * _authoredMaxAngle < 0f;

    /// <summary>Tells the door to stand open or shut, swinging as told, as the server's was told: its own motion takes it there.</summary>
    /// <param name="open">Whether it stands open.</param>
    /// <param name="swingsBack">Whether a two-way door swings the other way from the way it was authored.</param>
    public void Command(bool open, bool swingsBack)
    {
        if (OpenBothWays)
        {
            MaxAngle = swingsBack ? -_authoredMaxAngle : _authoredMaxAngle;
        }

        if (open != IsCommandedOpen)
        {
            _isOpenDoor = open ? (byte)1 : (byte)0;
            _isOpenDoorChanged = 1;
        }
    }
}
