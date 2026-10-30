namespace OpenIGI.Input;

/// <summary>How far the device itself turned this frame, as its gyroscope reported it.</summary>
/// <remarks>
/// In the screen's own terms rather than the sensor's: a phone held on its side turns right when its screen turns right,
/// whichever way round it is held. The window turns the sensor's axes into these; what the turn does to the view is the
/// host's to decide.
/// </remarks>
public sealed class MotionState
{
    /// <summary>Gets how far the device turned right this frame, in radians.</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets how far the device tilted up this frame, in radians: the top of the screen away from the player.</summary>
    public float Pitch { get; private set; }

    /// <summary>Starts a frame: nothing has turned yet.</summary>
    public void BeginFrame()
    {
        Yaw = 0f;
        Pitch = 0f;
    }

    /// <summary>The device turned.</summary>
    /// <param name="yaw">How far right, in radians.</param>
    /// <param name="pitch">How far up, in radians.</param>
    public void Add(float yaw, float pitch)
    {
        Yaw += yaw;
        Pitch += pitch;
    }
}
