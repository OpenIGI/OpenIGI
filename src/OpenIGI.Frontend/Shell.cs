using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.Frontend.Screens;

namespace OpenIGI.Frontend;

/// <summary>
/// The cinematic shell: the screen stack rooted at the main menu, driven each frame by the application.
/// </summary>
/// <remarks>
/// The composition root builds one <see cref="Shell"/> with a theme, the application seam
/// (<see cref="IShellHost"/>) and the game data, then calls <see cref="Update"/> and <see cref="Draw"/>
/// while the shell is on screen. The shell owns nothing of the window or device; it draws through a
/// <see cref="UiPainter"/> the app hands it and asks the host for everything it cannot do itself.
/// </remarks>
public sealed class Shell
{
    private readonly ShellContext _context;

    /// <summary>Initializes a new instance of the <see cref="Shell"/> class rooted at the main menu.</summary>
    /// <param name="context">The shared context: theme, host and game data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public Shell(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Stack = context.Stack;
        Stack.Reset(new MainMenuScreen(context), animate: false);
    }

    /// <summary>Gets the screen stack the shell drives.</summary>
    public ScreenStack Stack { get; }

    /// <summary>Opens the shell on a named section instead of the landing page.</summary>
    /// <param name="tab">The section name — <c>campaign</c>, <c>settings</c>, <c>profile</c> or <c>extras</c>.</param>
    /// <returns><see langword="true"/> when the name matched a section.</returns>
    /// <remarks>
    /// For the composition root's <c>-shelltab</c> development shortcut, which is the cinematic shell's
    /// counterpart to <c>-screen</c>: a capture of the campaign or the settings should not need someone to
    /// click their way there first. An unknown name leaves the shell on the landing page.
    /// </remarks>
    public bool OpenOn(string? tab)
    {
        Screen? screen = tab?.ToLowerInvariant() switch
        {
            "campaign" => new CampaignScreen(_context),
            "settings" => new SettingsScreen(_context),
            "profile" or "profiles" => new ProfilesScreen(_context),
            "extras" => new ExtrasScreen(_context),
            _ => null,
        };

        if (screen is null)
        {
            return false;
        }

        Stack.Reset(screen, animate: false);
        return true;
    }

    /// <summary>Advances the shell for a frame.</summary>
    /// <param name="context">The layout context for this frame.</param>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    public void Update(LayoutContext context, InputState input, float elapsedSeconds) =>
        Stack.Update(context, input, elapsedSeconds);

    /// <summary>Draws the shell.</summary>
    /// <param name="painter">The painter to draw into.</param>
    public void Draw(UiPainter painter) => Stack.Draw(painter);
}
