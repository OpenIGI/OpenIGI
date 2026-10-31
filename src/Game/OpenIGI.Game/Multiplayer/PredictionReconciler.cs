using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>Keeps a client's own player where the server has it, without waiting for the server to move it.</summary>
/// <remarks>
/// <para>
/// The client runs its own player on its own controls at once: that is the prediction, and it is the title's ordinary tick.
/// Each command's result is noted here by the tick it was sent for. When a snapshot says the server has run the client's
/// commands up to tick N and put its player at S, the prediction for N is compared with S. Agreeing (as it does when both
/// ran the same controls over the same world), nothing happens. Disagreeing, the difference is the client's error, and the
/// player's position now is moved by it: every tick since N was predicted from a start that was out by that much.
/// </para>
/// <para>
/// Source goes further, putting the player back at S and running every command since N again. An I.G.I. human is far too
/// much state to save and run again each tick (its animation, its state machine of over a hundred states, its weapons), so
/// this keeps to the position, the part an error shows in. The simulated position takes the whole correction at once, so
/// the next comparison measures only what is new; smoothing what the eye sees belongs to the camera, not the simulation.
/// </para>
/// </remarks>
public sealed class PredictionReconciler
{
    private const int History = 128;

    private readonly Dictionary<long, Vector3d> _predicted = [];
    private long _lastChecked = long.MinValue;

    /// <summary>Gets or sets how far off a prediction may be, in world units, before it is corrected at all.</summary>
    public double Tolerance { get; set; } = 64;

    /// <summary>Gets the size of the last error found, in world units.</summary>
    public double LastError { get; private set; }

    /// <summary>Gets how many corrections have been made.</summary>
    public long Corrections { get; private set; }

    /// <summary>Notes where the client's own player stood after the command for a tick.</summary>
    /// <param name="tick">The tick the command was sent for.</param>
    /// <param name="position">Where the player stood after running it.</param>
    public void Record(long tick, Vector3d position)
    {
        _predicted[tick] = position;
        if (_predicted.Count > History)
        {
            foreach (long old in _predicted.Keys.Where(t => t < tick - History).ToArray())
            {
                _predicted.Remove(old);
            }
        }
    }

    /// <summary>Compares where the server put the player after a tick's command with where the client predicted it.</summary>
    /// <param name="commandTick">The newest tick whose command the server has run for this client.</param>
    /// <param name="server">Where the server put the player then.</param>
    /// <returns>How far to move the player now, or <see langword="null"/> when the prediction agreed or was already checked.</returns>
    public Vector3d? Check(long commandTick, Vector3d server)
    {
        if (commandTick <= _lastChecked || !_predicted.TryGetValue(commandTick, out Vector3d predicted))
        {
            return null;
        }

        _lastChecked = commandTick;
        Vector3d error = server - predicted;
        LastError = error.Length;
        if (LastError <= Tolerance)
        {
            return null;
        }

        // Every later prediction started from the same wrong place: correct them too, so they are not counted again.
        Corrections++;
        foreach (long tick in _predicted.Keys.Where(t => t > commandTick).ToArray())
        {
            _predicted[tick] += error;
        }

        return error;
    }
}
