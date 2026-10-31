using OpenIGI.Frontend.Screens;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

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
    private readonly Spring _blur = new(0f, smoothTime: 0.18f);
    private float _scale = 1f;

    /// <summary>Gets the context the screens share.</summary>
    public ShellContext Context => _context;

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

    /// <summary>Opens the shell over a live mission, as the pause menu.</summary>
    /// <remarks>
    /// The whole stack is replaced rather than pushed onto, because the pause menu is a front end in its
    /// own right — the player left the shell when the mission started, and whatever page they were on then
    /// is not somewhere Back should walk them to now. It carries the same top bar as every other page, so
    /// the settings and the profile are reachable from inside a mission without a second set of screens.
    /// </remarks>
    public void OpenPauseMenu()
    {
        _context.InGame = true;
        Stack.Reset(new PauseScreen(_context));
    }

    /// <summary>Opens the shell over a mission the player did not survive, as the failure page.</summary>
    /// <remarks>
    /// The same door as <see cref="OpenPauseMenu"/>, and for the same reason: the page is a front end in
    /// its own right, drawn over the level the body is lying in. It is a page rather than a drawn card so
    /// that its choices can be clicked and navigated like every other page, and so the shell's own cursor
    /// is the one on screen.
    /// </remarks>
    public void OpenMissionFailed()
    {
        _context.InGame = true;
        Stack.Reset(new MissionFailedScreen(_context));
    }

    /// <summary>Opens the shell over a co-op client's mission whose room is gone.</summary>
    /// <param name="reason">Why, as the session said it.</param>
    /// <param name="code">The room's code, to join it again by, or <see langword="null"/>.</param>
    public void OpenRoomClosed(string reason, string? code)
    {
        _context.InGame = true;
        Stack.Reset(new RoomClosedScreen(_context, reason, code));
    }

    /// <summary>Tells the shell it is no longer over a live mission.</summary>
    /// <remarks>
    /// Called when the pause menu is left by any door — resuming, restarting, abandoning, or starting
    /// another mission — so the next page the shell builds previews levels again instead of leaving the
    /// backdrop alone.
    /// </remarks>
    public void ClosePauseMenu() => _context.InGame = false;

    /// <summary>Advances the shell for a frame.</summary>
    /// <param name="context">The layout context for this frame.</param>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    public void Update(LayoutContext context, InputState input, float elapsedSeconds)
    {
        Stack.Update(context, input, elapsedSeconds);

        // The backdrop eases between pages rather than snapping: the home page's sharp level blurs as a
        // section opens over it, and the campaign's push-in on a new mission settles over a second.
        ShellScreen? page = Stack.Top as ShellScreen;
        _blur.Target = page?.BackdropBlur ?? 6f;
        _blur.Update(elapsedSeconds);
        _scale = page?.BackdropScale ?? 1f;
    }

    /// <summary>Draws what goes behind every page: the level, blurred and pushed in as the page asks, under the design's vignette.</summary>
    /// <param name="painter">The painter, whose renderer samples the blurred frame for the level.</param>
    /// <remarks>
    /// The host calls this after the 3D scene and before <see cref="Draw"/>. It is drawn here rather than
    /// by a page so it does not fade with a page's transition: switching tabs swaps the page over a backdrop
    /// that holds still.
    /// </remarks>
    public void DrawBackdrop(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        var screen = new UiRect(0f, 0f, painter.Context.Viewport.X, painter.Context.Viewport.Y);
        float blur = _blur.Value;
        if (blur > 0.05f)
        {
            // brightness(0.62) on the picture: a dark wash over the blurred level, deepening with the blur.
            float settle = Math.Clamp(blur / 6f, 0f, 1f);
            painter.DrawBackdrop(screen, painter.Context.Px(blur), UiPalette.InkAt(0.38f * settle), _scale);
        }

        KitDraw.Vignette(painter, screen, VignetteStyle.Menu, glow: blur > 0.05f);
    }

    /// <summary>Draws the shell.</summary>
    /// <param name="painter">The painter to draw into.</param>
    public void Draw(UiPainter painter) => Stack.Draw(painter);
}
