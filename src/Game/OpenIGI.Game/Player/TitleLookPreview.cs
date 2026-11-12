using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A title player's turn by mouse motion, from the angles it faces at: its own game's arithmetic.</summary>
/// <param name="mouseX">The motion across, in counts.</param>
/// <param name="mouseY">The motion up and down, in counts.</param>
/// <param name="sensitivity">The profile's mouse sensitivity.</param>
/// <param name="invertMouse">Whether the profile inverts the mouse.</param>
/// <param name="gamma">The heading turned from.</param>
/// <param name="pitch">The pitch turned from.</param>
/// <returns>The heading and pitch turned to.</returns>
public delegate (float Gamma, float Pitch) TitleLookTurn(float mouseX, float mouseY, float sensitivity, bool invertMouse, float gamma, float pitch);

/// <summary>
/// The look a title player shows between its ticks under Smooth Motion (<see cref="ITitlePlayerLook"/>): the mouse gathered
/// since the last tick, turned from where the player faced at it, which the next tick then takes as its own.
/// </summary>
/// <remarks>
/// A deliberate Smooth Motion policy, the same in both games; each supplies its own turn (<see cref="TitleLookTurn"/>) and its
/// own aim, and its retail look runs unchanged when smoothing is off.
/// </remarks>
/// <param name="canPreview">Whether the player may look now: not in a cutscene, not dead, not locked.</param>
/// <param name="facing">Where the player faces as of its last tick.</param>
/// <param name="turn">The game's turn.</param>
/// <param name="aim">The game's orientation for a heading and a pitch.</param>
public sealed class TitleLookPreview(
    Func<bool> canPreview,
    Func<(float Gamma, float Pitch)> facing,
    TitleLookTurn turn,
    Func<float, float, Matrix3f> aim)
{
    private bool _pending;
    private float _gamma;
    private float _pitch;

    /// <summary>Gets whether the player may look now.</summary>
    public bool CanPreview => canPreview();

    /// <summary>Gets the orientation the player faces as of its last tick.</summary>
    public Matrix3f Orientation
    {
        get
        {
            (float gamma, float pitch) = facing();
            return aim(gamma, pitch);
        }
    }

    /// <summary>Gathers a frame's mouse motion onto the look gathered since the last tick.</summary>
    /// <param name="mouseX">The motion across.</param>
    /// <param name="mouseY">The motion up and down.</param>
    /// <param name="sensitivity">The profile's mouse sensitivity.</param>
    /// <param name="invertMouse">Whether the profile inverts the mouse.</param>
    public void Gather(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreview)
        {
            Reset();
            return;
        }

        if (!_pending)
        {
            (_gamma, _pitch) = facing();
        }

        (_gamma, _pitch) = turn(mouseX, mouseY, sensitivity, invertMouse, _gamma, _pitch);
        _pending = true;
    }

    /// <summary>Forgets the look gathered since the last tick.</summary>
    public void Reset() => _pending = false;

    /// <summary>The orientation to draw this frame: the look gathered, else the mouse's motion turned from the last tick's facing.</summary>
    /// <param name="mouseX">The motion across.</param>
    /// <param name="mouseY">The motion up and down.</param>
    /// <param name="sensitivity">The profile's mouse sensitivity.</param>
    /// <param name="invertMouse">Whether the profile inverts the mouse.</param>
    /// <returns>The orientation.</returns>
    public Matrix3f Preview(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreview)
        {
            return Orientation;
        }

        (float gamma, float pitch) = Next(mouseX, mouseY, sensitivity, invertMouse);
        return aim(gamma, pitch);
    }

    /// <summary>The heading and pitch the next tick takes: the look gathered, else the tick's mouse motion turned from its facing.</summary>
    /// <param name="mouseX">The tick's motion across.</param>
    /// <param name="mouseY">The tick's motion up and down.</param>
    /// <param name="sensitivity">The profile's mouse sensitivity.</param>
    /// <param name="invertMouse">Whether the profile inverts the mouse.</param>
    /// <returns>The heading and pitch.</returns>
    public (float Gamma, float Pitch) Next(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (_pending)
        {
            return (_gamma, _pitch);
        }

        (float gamma, float pitch) = facing();
        return turn(mouseX, mouseY, sensitivity, invertMouse, gamma, pitch);
    }
}
