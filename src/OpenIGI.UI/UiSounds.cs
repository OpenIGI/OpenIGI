namespace OpenIGI.UI;

/// <summary>
/// Where the interface's own feedback sounds go — moving the selection, and choosing.
/// </summary>
/// <remarks>
/// <para>
/// A static sink rather than a dependency threaded through every widget. Interface feedback is
/// cross-cutting: every control that can take focus wants the same sound, and giving each of them a
/// constructor argument for it would touch the whole widget library to say one thing. The retail menu
/// system reaches the same two sounds through <c>IMenuHost.PlaySound</c>; this is the cinematic
/// shell's equivalent, and both end up at the same sound bank.
/// </para>
/// <para>
/// Left unset the interface is silent, which is what every test and every headless run wants.
/// </para>
/// </remarks>
public static class UiSounds
{
    /// <summary>The sound the selection makes when it moves, matching the retail menu system's.</summary>
    public const string Move = "menu_high";

    /// <summary>The sound choosing makes, matching the retail menu system's.</summary>
    public const string Choose = "menu_sel";

    /// <summary>Gets or sets where a named interface sound is played, or <see langword="null"/> for silence.</summary>
    /// <remarks>Set once by the composition root, after the audio device is open.</remarks>
    public static Action<string>? Play { get; set; }

    /// <summary>Plays the selection-moved sound.</summary>
    public static void Moved() => Play?.Invoke(Move);

    /// <summary>Plays the chosen sound.</summary>
    public static void Chosen() => Play?.Invoke(Choose);
}
