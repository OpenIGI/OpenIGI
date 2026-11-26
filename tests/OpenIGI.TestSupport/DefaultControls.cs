using OpenIGI.Game.Config;
using OpenIGI.Input;

namespace OpenIGI.TestSupport;

/// <summary>
/// The keys a test presses for the player's actions: the shipped default controls' (<see cref="PlayerProfile.DefaultBindings"/>),
/// which a <c>-lockstep</c> game plays on whoever's machine runs it.
/// </summary>
/// <remarks>
/// Looked up rather than written into each test, so a test presses the use key and not whatever key the use key once was:
/// when the defaults moved use from E to F, every test that held E for it was holding the lean instead.
/// </remarks>
public static class DefaultControls
{
    /// <summary>Gets the key the use action is on: doors, ladders, computers, the bomb.</summary>
    public static KeyCode Use => KeyFor("Activate");

    /// <summary>The keyboard key an action is on by default.</summary>
    /// <param name="action">The action's name in the profile, such as <c>Activate</c>.</param>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">The action is not on a keyboard key by default.</exception>
    public static KeyCode KeyFor(string action)
    {
        foreach (InputBinding binding in PlayerProfile.DefaultBindings)
        {
            if (string.Equals(binding.Action, action, StringComparison.Ordinal)
                && string.Equals(binding.Device, InputBindingKeyNames.KeyboardDevice, StringComparison.Ordinal)
                && InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode key))
            {
                return key;
            }
        }

        throw new InvalidOperationException($"No keyboard key is bound to {action} by default.");
    }
}
