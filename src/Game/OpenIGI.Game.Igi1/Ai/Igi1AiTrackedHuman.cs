using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>One human the scan has been offered, and the slot it keeps for it.</summary>
/// <remarks>
/// The original's slot holds a reference to the human's task (<c>AIDetectSlot.ref</c>, <c>0x489500</c>) and reads the human
/// through it when it needs to: its description (<c>0x45FE30</c>) only on the detector's scan tick, and where the human stands
/// whenever the soldier aims at or measures against it. Offered by reference (<see cref="AiHumanReference"/>), this reads the
/// human the first time the owning soldier's tick asks, and keeps that until the next offer: within the soldier's tick only the
/// soldier itself acts, so the human is as it was when the tick began. A soldier pays for the humans it actually looks at rather
/// than describing every human in the level every tick.
/// </remarks>
internal sealed class Igi1AiTrackedHuman
{
    private AiDetectionCandidate _candidate;
    private AiHumanReference _reference;
    private bool _pending;

    /// <summary>Initialises a new instance of the <see cref="Igi1AiTrackedHuman"/> class from a description.</summary>
    /// <param name="candidate">How the human described itself the first time it was offered.</param>
    public Igi1AiTrackedHuman(AiDetectionCandidate candidate)
    {
        _candidate = candidate;
        Id = candidate.Id;
    }

    /// <summary>Initialises a new instance of the <see cref="Igi1AiTrackedHuman"/> class from a reference.</summary>
    /// <param name="reference">The human, as the scan first offered it.</param>
    public Igi1AiTrackedHuman(AiHumanReference reference) => Offer(reference);

    /// <summary>Gets the human's id: the player's, or its soldier's.</summary>
    public int Id { get; private set; }

    /// <summary>Gets or sets how the human described itself this tick.</summary>
    public AiDetectionCandidate Candidate
    {
        get
        {
            if (_pending)
            {
                _candidate = _reference.Describe();
                _pending = false;
            }

            return _candidate;
        }

        set
        {
            _candidate = value;
            _pending = false;
            Id = value.Id;
        }
    }

    /// <summary>Gets whether the human is on the other side from the soldier looking.</summary>
    public bool OtherSide => _pending ? _reference.OtherSide : _candidate.OtherSide;

    /// <summary>Gets whether the human is alive, read now if it has not been described since it was offered.</summary>
    public bool Alive => _pending ? _reference.IsAlive() : _candidate.Alive;

    /// <summary>Gets the detection slot the scan keeps for the human.</summary>
    public AiDetectionSlot Slot { get; } = new();

    /// <summary>Takes this tick's offer of the human: it is read from the reference when first asked.</summary>
    /// <param name="reference">The human.</param>
    public void Offer(in AiHumanReference reference)
    {
        _reference = reference;
        Id = reference.Id;
        _pending = true;
    }

    /// <summary>Reads the human now if it has not been read since it was offered, so what is read later in the frame is the same.</summary>
    public void Settle() => _ = Candidate;

    /// <summary>Gets or sets what the sweep itself last said of him, while a mod's behaviour judges what counts as seen (IGuardBehaviour.Sighting).</summary>
    public bool SweepVisible { get; set; }
}
