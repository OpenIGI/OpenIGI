namespace OpenIGI.UI;

/// <summary>Where the interface's own feedback sounds go: moving the selection, and choosing.</summary>
/// <remarks>
/// The retail menu system reaches the same two sounds through <c>IMenuHost.PlaySound</c>; this is the cinematic shell's
/// equivalent, and both end up at the same sound bank. A tree with none set is silent, which is what every test and every
/// headless run wants.
/// </remarks>
public interface IUiSoundSink
{
    /// <summary>Plays a named interface sound.</summary>
    /// <param name="name">The sound's name in the title's sound bank.</param>
    void Play(string name);
}

/// <summary>The names of the interface's feedback sounds.</summary>
public static class UiSounds
{
    /// <summary>The sound the selection makes when it moves, matching the retail menu system's.</summary>
    public const string Move = "menu_high";

    /// <summary>The sound choosing makes, matching the retail menu system's.</summary>
    public const string Choose = "menu_sel";
}
