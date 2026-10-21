namespace OpenIGI.Rendering;

/// <summary>DLSS frame generation: asking for it, and why it cannot run.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IFrameGenerationDevice
{
    /// <summary>Asks for DLSS frame generation this frame, and says what it is actually doing.</summary>
    /// <param name="mode">The multiplier the settings ask for.</param>
    /// <param name="gameplay">
    /// Whether this frame is the game being played. Menus, loading screens, pauses and cuts are not: they
    /// lack a continuous camera and the motion frame generation interpolates along, so it stands down for
    /// them and resumes, without rebuilding anything, when play does.
    /// </param>
    /// <returns>What frame generation is doing, with the reason where that is not what was asked.</returns>
    /// <remarks>
    /// Call before the frame draws anything: turning frame generation on or off for good rebuilds the
    /// swapchain, which cannot happen in the middle of a frame.
    /// </remarks>
    FrameGenerationStatus SetFrameGeneration(FrameGenerationMode mode, bool gameplay);

    /// <summary>Gets why frame generation cannot run here, or an empty string where it can.</summary>
    string FrameGenerationUnavailable { get; }
}
