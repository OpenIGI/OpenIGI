namespace OpenIGI.Core.Configuration;

/// <summary>
/// Describes a console variable's transition from one value to another.
/// </summary>
/// <remarks>
/// Subsystems subscribe to react to a change without polling — the renderer rebuilds its targets when
/// the render scale moves, the audio mixer re-applies gains, the shell repaints a settings row.
/// </remarks>
/// <param name="variable">The variable that changed.</param>
/// <param name="previousText">The previous value, rendered as text.</param>
/// <param name="currentText">The new value, rendered as text.</param>
public sealed class CVarChangedEventArgs(ICVar variable, string previousText, string currentText) : EventArgs
{
    /// <summary>Gets the variable that changed.</summary>
    public ICVar Variable { get; } = variable;

    /// <summary>Gets the previous value, rendered as text.</summary>
    public string PreviousText { get; } = previousText;

    /// <summary>Gets the new value, rendered as text.</summary>
    public string CurrentText { get; } = currentText;
}
