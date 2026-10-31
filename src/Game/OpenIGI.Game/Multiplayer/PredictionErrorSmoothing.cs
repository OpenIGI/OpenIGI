using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A predicting client's view, eased over a correction rather than snapped by it.</summary>
/// <remarks>
/// When the server puts a client's own player somewhere other than where the client predicted it, the player is moved at
/// once, so its next commands start from the truth; only the eye keeps the difference, and lets it go over
/// <see cref="SmoothTicks"/>. A second correction before the first has faded adds to what is left of it. This is Source's
/// <c>cl_smooth</c> and <c>cl_smoothtime</c>.
/// </remarks>
public sealed class PredictionErrorSmoothing
{
    private Vector3d _error;
    private int _age = int.MaxValue;

    /// <summary>Gets or sets how many ticks an error takes to fade (<c>cl_smoothtime</c>, a tenth of a second at 30).</summary>
    public int SmoothTicks { get; set; } = 3;

    /// <summary>Notes a correction: the player moved by <paramref name="correction"/>, the eye stays where it was.</summary>
    /// <param name="correction">How far the player was moved, in world units.</param>
    public void Note(Vector3d correction)
    {
        _error = Offset(0f) - correction;
        _age = 0;
    }

    /// <summary>Ages the error by a tick; call once each tick the player runs.</summary>
    public void Tick()
    {
        if (_age < int.MaxValue)
        {
            _age++;
        }
    }

    /// <summary>Gets what to add to the eye now.</summary>
    /// <param name="tickBlend">How far through the tick the frame is, 0 to 1.</param>
    /// <returns>The offset, in world units; zero once the error has faded.</returns>
    public Vector3d Offset(float tickBlend)
    {
        if (SmoothTicks <= 0 || _age == int.MaxValue)
        {
            return default;
        }

        double remaining = 1.0 - ((_age + Math.Clamp(tickBlend, 0f, 1f)) / SmoothTicks);
        return remaining <= 0 ? default : _error * remaining;
    }
}
