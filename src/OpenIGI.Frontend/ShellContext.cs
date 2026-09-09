using OpenIGI.Core.Configuration;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Config;
using OpenIGI.Rendering.Presets;
using OpenIGI.Rendering;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend;

/// <summary>
/// The data and services every shell screen shares: the theme, the host, the game and mission lists, the
/// CVar and preset registries, the profiles and the save slots — plus the stack, so a screen can push
/// the next one.
/// </summary>
/// <remarks>
/// Bundling these into one context passed to every screen keeps each screen's constructor small and
/// means the composition root wires the shell up in exactly one place. Nothing here is a backend type;
/// it is all game data and abstractions, which is what lets the whole shell be built and much of it
/// tested without a device.
/// </remarks>
public sealed class ShellContext
{
    /// <summary>Initializes a new instance of the <see cref="ShellContext"/> class.</summary>
    /// <param name="theme">The theme screens draw from.</param>
    /// <param name="host">The application seam for actions the shell cannot perform itself.</param>
    /// <param name="stack">The screen stack, for pushing further screens.</param>
    /// <param name="games">The game titles for the selector.</param>
    /// <param name="missions">The campaign's missions, resolved for display.</param>
    /// <param name="cvars">The CVar registry the settings screen binds to.</param>
    /// <param name="presets">The graphics-preset registry.</param>
    /// <param name="config">The player-profile configuration.</param>
    /// <param name="saves">The save-slot store.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public ShellContext(
        UiTheme theme,
        IShellHost host,
        ScreenStack stack,
        IReadOnlyList<GameProfile> games,
        IReadOnlyList<MissionInfo> missions,
        CVarRegistry cvars,
        GraphicsPresetRegistry presets,
        GameConfig config,
        SaveSlotStore saves)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(cvars);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(saves);

        Theme = theme;
        Host = host;
        Stack = stack;
        Games = games;
        Missions = missions;
        Cvars = cvars;
        Presets = presets;
        Config = config;
        Saves = saves;
    }

    /// <summary>Gets or sets a value indicating whether the shell is open over a live mission.</summary>
    /// <remarks>
    /// Set while the shell is being used as the pause menu, and the one thing that tells the screens the
    /// backdrop behind them is already the right picture: the level the player is standing in. They ask
    /// for no preview while it is set — <see cref="IShellHost.PreviewMission"/> would stream a second
    /// level over the one being played — and Back leads to the pause menu rather than to the landing page.
    /// </remarks>
    public bool InGame { get; set; }

    /// <summary>Gets or sets the conditions the campaign is being played under.</summary>
    /// <remarks>
    /// The shell's own copy of what it last told <see cref="IShellHost.SetMissionConditions"/>, kept here
    /// rather than on the campaign screen because the screen is rebuilt every time the section is opened
    /// and would otherwise offer "default" back to a player who is three missions into a night run.
    /// </remarks>
    public MissionConditions Conditions { get; set; } = MissionConditions.Default;

    /// <summary>Gets the theme screens draw from.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets the application seam for actions the shell cannot perform itself.</summary>
    public IShellHost Host { get; }

    /// <summary>Gets the screen stack, for pushing further screens.</summary>
    public ScreenStack Stack { get; }

    /// <summary>Gets the game titles for the selector.</summary>
    public IReadOnlyList<GameProfile> Games { get; }

    /// <summary>Gets the campaign's missions, resolved for display.</summary>
    public IReadOnlyList<MissionInfo> Missions { get; }

    /// <summary>Gets the CVar registry the settings screen binds to.</summary>
    public CVarRegistry Cvars { get; }

    /// <summary>Gets the graphics-preset registry.</summary>
    public GraphicsPresetRegistry Presets { get; }

    /// <summary>Gets the player-profile configuration.</summary>
    public GameConfig Config { get; }

    /// <summary>Gets the save-slot store.</summary>
    public SaveSlotStore Saves { get; }
}
