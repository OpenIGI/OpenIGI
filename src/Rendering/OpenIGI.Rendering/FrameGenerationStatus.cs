namespace OpenIGI.Rendering;

/// <summary>What frame generation is doing this frame, against what was asked of it.</summary>
/// <param name="Requested">The multiplier the settings ask for.</param>
/// <param name="Active">Whether generated frames are being presented.</param>
/// <param name="Multiplier">How many frames reach the screen per rendered one: one when it is off.</param>
/// <param name="Reason">Why it is not running as asked, or empty.</param>
public readonly record struct FrameGenerationStatus(
    FrameGenerationMode Requested,
    bool Active,
    int Multiplier,
    string Reason)
{
    /// <summary>Gets the status of frame generation that was never asked for.</summary>
    public static FrameGenerationStatus Off { get; } = new(FrameGenerationMode.Off, false, 1, string.Empty);
}
