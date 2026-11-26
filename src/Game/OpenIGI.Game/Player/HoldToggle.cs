using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Player;

/// <summary>
/// A held control made a toggle, for players who would rather press once than hold: each press flips it on or off, and the
/// game sees it held while it is on. With the toggle off it passes the key through, held while held, as the original has it.
/// </summary>
public sealed class HoldToggle
{
    private bool _wasDown;

    /// <summary>Gets a value indicating whether it is on, while toggling.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Takes a tick's key and says whether the control is held.</summary>
    /// <param name="down">Whether the key is down this tick.</param>
    /// <param name="toggles">Whether the player has it toggle.</param>
    /// <returns>Whether the game sees the control held.</returns>
    public bool Apply(bool down, bool toggles)
    {
        bool pressed = down && !_wasDown;
        _wasDown = down;
        if (!toggles)
        {
            IsOn = false;
            return down;
        }

        if (pressed)
        {
            IsOn = !IsOn;
        }

        return IsOn;
    }

    /// <summary>Lets it go: off, as a new mission or a death starts it.</summary>
    public void Reset()
    {
        IsOn = false;
        _wasDown = false;
    }
}

/// <summary>The player's control preferences that are not bindings.</summary>
[CVarContainer]
public static class PlayerControlCVars
{
    /// <summary>Whether crouch toggles with a press rather than being held.</summary>
    public static readonly BoolCVar CrouchToggle = CVar.Bool(
        "in_crouch_toggle",
        false,
        CVarOptions.Archive,
        "Press crouch once to crouch and again to stand, rather than holding it.");
}
