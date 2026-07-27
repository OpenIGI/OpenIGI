namespace OpenIGI.Engine.Flow;

/// <summary>
/// An <see cref="IFlowHost"/> that does nothing, so the state machine can be exercised without menus,
/// screens or missions.
/// </summary>
/// <remarks>
/// The state transitions still happen — only their effects are missing — which is what makes a headless
/// boot possible before M5 brings in the menu system.
/// </remarks>
public sealed class NullFlowHost : IFlowHost
{
    /// <summary>Gets the shared instance.</summary>
    public static NullFlowHost Instance { get; } = new();

    private NullFlowHost()
    {
    }

    /// <inheritdoc/>
    public void ShowIntroScreens()
    {
    }

    /// <inheritdoc/>
    public void ShowMainMenu()
    {
    }

    /// <inheritdoc/>
    public void ShowOutroScreens()
    {
    }

    /// <inheritdoc/>
    public void ShowAttractScreens()
    {
    }

    /// <inheritdoc/>
    public void BeginGame(FlowTask flow)
    {
    }

    /// <inheritdoc/>
    public void LoadLevel()
    {
    }

    /// <inheritdoc/>
    public void EndGame()
    {
    }
}
