namespace OpenIGI.Frontend.Screens;

/// <summary>Runs its action once, on the first dispose.</summary>
/// <param name="action">What unhooks the subscription it stands for.</param>
internal sealed class Unsubscribe(Action action) : IDisposable
{
    private Action? _action = action;

    /// <inheritdoc/>
    public void Dispose()
    {
        _action?.Invoke();
        _action = null;
    }
}
