namespace OpenIGI.Engine.Flow;

/// <summary>
/// The three conditions that take the frame off the fixed-timestep schedule.
/// </summary>
/// <remarks>
/// <para>
/// While any of these holds, the arbiter stops consulting the clock and runs exactly one update and one
/// render per frame instead.
/// </para>
/// <para>
/// The original reads three globals, each itself a pair of bytes OR-ed together: <c>byte_5C8E00</c> for
/// the pause, <c>0x4EE940</c> for the loading screen and <c>0x491220</c> for a playing movie. They are
/// owned by three different subsystems, which is why they arrive here as one interface rather than as
/// state on the flow task.
/// </para>
/// </remarks>
public interface IFrameSuspension
{
    /// <summary>Gets a value indicating whether the game is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Gets a value indicating whether a level transition screen is up.</summary>
    bool IsLoading { get; }

    /// <summary>Gets a value indicating whether a movie is playing.</summary>
    /// <remarks>Also suppresses the render pass, because the movie owns the screen.</remarks>
    bool IsCutscenePlaying { get; }
}
