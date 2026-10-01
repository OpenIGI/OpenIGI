namespace OpenIGI.Engine.Flow;

/// <summary>
/// A settable <see cref="IFrameSuspension"/>, for headless runs and for subsystems that have nowhere
/// better to keep the flag yet.
/// </summary>
/// <remarks>Every flag starts clear, which is the state a booted game is in.</remarks>
public sealed class FrameSuspensionState : IFrameSuspension
{
    /// <summary>Gets a shared instance whose flags are never set.</summary>
    /// <remarks>Do not set flags on this one; create an instance instead.</remarks>
    public static FrameSuspensionState None { get; } = new();

    /// <inheritdoc cref="IFrameSuspension.IsPaused"/>
    public bool IsPaused { get; set; }

    /// <inheritdoc cref="IFrameSuspension.IsLoading"/>
    public bool IsLoading { get; set; }

    /// <inheritdoc cref="IFrameSuspension.IsCutscenePlaying"/>
    public bool IsCutscenePlaying { get; set; }

    /// <summary>Gets a value indicating whether any flag is set.</summary>
    public bool IsSuspended => IsPaused || IsLoading || IsCutscenePlaying;
}
