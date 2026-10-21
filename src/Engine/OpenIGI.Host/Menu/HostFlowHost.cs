using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;

namespace OpenIGI.Host.Menu;

/// <summary>The host's side of the flow state machine: what each of its states asks the shell to show.</summary>
/// <remarks>
/// <para>
/// The flow owns when the menu exists -- <see cref="ShowMainMenu"/> is what <see cref="FlowEvent.MainMenu"/> calls -- and
/// the host says what that means: the retail menus where they are wanted, through <see cref="LoadMainMenu"/>.
/// </para>
/// <para>
/// The states this cannot serve yet report through the log rather than pretending: there are no intro screens, no outro
/// and no game to begin.
/// </para>
/// </remarks>
/// <param name="logger">Where the flow's requests are reported.</param>
internal sealed class HostFlowHost(IOpenIgiLogger logger) : IFlowHost
{
    private const string LogCategory = "Menu";

    /// <summary>Gets or sets what <see cref="ShowMainMenu"/> does.</summary>
    public Action? LoadMainMenu { get; set; }

    /// <inheritdoc/>
    public void ShowMainMenu()
    {
        logger.Information(LogCategory, "Flow entered the main menu.");
        LoadMainMenu?.Invoke();
    }

    /// <inheritdoc/>
    public void ShowIntroScreens() => NotYet(nameof(ShowIntroScreens));

    /// <inheritdoc/>
    public void ShowOutroScreens() => NotYet(nameof(ShowOutroScreens));

    /// <inheritdoc/>
    public void ShowAttractScreens() => NotYet(nameof(ShowAttractScreens));

    /// <inheritdoc/>
    public void BeginGame(FlowTask flow) => NotYet(nameof(BeginGame));

    /// <inheritdoc/>
    public void LoadLevel() => NotYet(nameof(LoadLevel));

    /// <inheritdoc/>
    public void EndGame() => NotYet(nameof(EndGame));

    private void NotYet(string what) =>
        logger.Information(LogCategory, $"Flow asked for {what}, which is not implemented yet.");
}
