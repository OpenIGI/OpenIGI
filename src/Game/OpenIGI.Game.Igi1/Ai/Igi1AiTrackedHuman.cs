using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>One human the scan has been offered, and the slot it keeps for it.</summary>
/// <param name="candidate">How the human described itself the first time it was offered.</param>
internal sealed class Igi1AiTrackedHuman(AiDetectionCandidate candidate)
{
    /// <summary>Gets or sets how the human described itself this tick.</summary>
    public AiDetectionCandidate Candidate { get; set; } = candidate;

    /// <summary>Gets the detection slot the scan keeps for the human.</summary>
    public AiDetectionSlot Slot { get; } = new();
}
