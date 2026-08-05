namespace OpenIGI.Frontend.Settings;

/// <summary>A re-entrancy guard so a value pushed one way does not echo back the other.</summary>
/// <remarks>
/// Every settings binding is two-way — the widget writes the variable, the variable's change event writes
/// the widget — which is a loop unless one direction stands down while the other is running. Shared by
/// <see cref="CVarBinding"/> and <see cref="QualityBinding"/> because both have exactly that loop, and a
/// preset or a quality step that rewrites a dozen variables at once has to survive it a dozen times.
/// </remarks>
internal sealed class BindingGate
{
    /// <summary>Gets a value indicating whether a push is in progress and the other direction should stand down.</summary>
    public bool Busy { get; private set; }

    /// <summary>Runs an action with <see cref="Busy"/> set, unless one is already running.</summary>
    /// <param name="action">The push to run.</param>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            action();
        }
        finally
        {
            Busy = false;
        }
    }
}
