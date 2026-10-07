namespace OpenIGI.Game.Igi2.Player;

/// <summary>The animation layer a human's states drive (<c>Human_s.tHumanAnim</c>).</summary>
/// <remarks>
/// The states choose clips by state number — <c>HumanAnim_SetAnimationAndTimeFromState</c> looks the
/// state's weapon-type animation id up through the held weapon's animation table — and read back how far
/// the main controller has got. That is the whole surface the locomotion code needs.
/// </remarks>
public interface IIgi2HumanAnimation
{
    /// <summary>Gets the main controller's time, in clip ticks.</summary>
    int MainTime { get; }

    /// <summary>Gets the length of the main controller's clip, in clip ticks; 0 when none.</summary>
    int MainTotalTime { get; }

    /// <summary>Gets or sets the main controller's time step per tick, in clip ticks.</summary>
    int MainTimeStep { get; set; }

    /// <summary>Gets a value indicating whether the main controller has played to the end (<c>nFlags &amp; 4</c>).</summary>
    bool MainEnded { get; }

    /// <summary>Makes the main controller loop its clip (<c>nFlags |= 1</c>).</summary>
    void LoopMain();

    /// <summary>Starts the clip a state plays, at a time, blending in over a duration.</summary>
    /// <param name="state">The locomotion state whose animation to play.</param>
    /// <param name="time">The clip time to start at, in clip ticks.</param>
    /// <param name="blendSeconds">The blend from the current pose; 0 for a cut.</param>
    void PlayState(int state, int time, float blendSeconds);
}

/// <summary>The questions a human's states ask of the world around it.</summary>
public interface IIgi2HumanSurroundings
{
    /// <summary>Gets a value indicating whether the human would fit lying flat where it stands.</summary>
    /// <remarks>
    /// The states test it by setting the posture to lying flat and running the object collision on the
    /// spot: the posture is refused when the collision reports signal bit 4 (<c>Human_ObjectCollision</c>).
    /// </remarks>
    bool FitsLyingFlat();

    /// <summary>Tells the AI the human is moving, so it can be heard (<c>HumanPlayer_SendWalkEvent</c>).</summary>
    void SendWalkEvent();
}
