using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>A co-op partner's senses on the server: each reaction recorded, for the partner's own game to make.</summary>
/// <param name="entity">The partner's entity in the snapshots.</param>
/// <remarks>
/// The server has no screen for a partner, and its sound stream is its own player's: so nothing is drawn or played here.
/// The partner's client takes each record from its snapshot and makes the same call on its own
/// <see cref="LocalPlayerSenses"/> (<see cref="Replay"/>), drawing on its own stream as its own handlers would.
/// </remarks>
internal sealed class RemotePlayerSenses(Func<int> entity) : IPlayerSenses
{
    private readonly List<ReplicatedSense> _pending = [];

    /// <summary>Takes what the partner has felt since the last snapshot.</summary>
    /// <param name="into">Where they go.</param>
    public void TakeInto(List<ReplicatedSense> into)
    {
        into.AddRange(_pending);
        _pending.Clear();
    }

    /// <summary>Makes a recorded reaction on this machine's own senses: the partner's client, feeling what its server recorded.</summary>
    /// <param name="sense">The record.</param>
    /// <param name="senses">The machine's own senses.</param>
    public static void Replay(in ReplicatedSense sense, IPlayerSenses senses)
    {
        ArgumentNullException.ThrowIfNull(senses);
        switch ((SenseKind)sense.Kind)
        {
            case SenseKind.Grunt:
                senses.Grunt();
                break;
            case SenseKind.Blow:
                senses.Blow();
                break;
            case SenseKind.HitShake:
                senses.HitShake();
                break;
            case SenseKind.Shake:
                senses.Shake(sense.A, sense.B, sense.Ticks);
                break;
            case SenseKind.FlashFrom:
                senses.FlashFrom(sense.Vector);
                break;
            case SenseKind.FlashCorners:
                senses.FlashCorners();
                break;
            case SenseKind.FlashCentre:
                senses.FlashCentre();
                break;
            case SenseKind.Sound:
                senses.Sound(sense.Text);
                break;
        }
    }

    /// <inheritdoc/>
    public void Grunt() => Record(SenseKind.Grunt);

    /// <inheritdoc/>
    public void Blow() => Record(SenseKind.Blow);

    /// <inheritdoc/>
    public void HitShake() => Record(SenseKind.HitShake);

    /// <inheritdoc/>
    public void Shake(float positionUnits, float angleRadians, int ticks) =>
        _pending.Add(new ReplicatedSense(entity(), (byte)SenseKind.Shake, A: positionUnits, B: angleRadians, Ticks: ticks));

    /// <inheritdoc/>
    public void FlashFrom(Vector3f travel) => _pending.Add(new ReplicatedSense(entity(), (byte)SenseKind.FlashFrom, travel));

    /// <inheritdoc/>
    public void FlashCorners() => Record(SenseKind.FlashCorners);

    /// <inheritdoc/>
    public void FlashCentre() => Record(SenseKind.FlashCentre);

    /// <inheritdoc/>
    public void Sound(string definition) => _pending.Add(new ReplicatedSense(entity(), (byte)SenseKind.Sound, Text: definition));

    /// <inheritdoc/>
    /// <remarks>
    /// Not sent: the partner's game takes its death from the snapshot that first has it fallen, and runs the whole death
    /// itself (<see cref="Igi1ClientWorld.ApplyOwn"/>), its fall and camera included; every other screen draws the body
    /// fall from the same snapshot.
    /// </remarks>
    public void Died(in PlayerFall fall)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Not sent, as a death isn't: the snapshot that has it alive again brings it back on its own screen.</remarks>
    public void Revived()
    {
    }

    private void Record(SenseKind kind) => _pending.Add(new ReplicatedSense(entity(), (byte)kind));

    // The senses, by the number a record carries.
    private enum SenseKind : byte
    {
        Grunt,
        Blow,
        HitShake,
        Shake,
        FlashFrom,
        FlashCorners,
        FlashCentre,
        Sound,
    }
}
