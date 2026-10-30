namespace OpenIGI.Rendering.Headless;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that records what it is asked to draw instead of drawing it.
/// </summary>
/// <remarks>
/// <para>
/// Everything above the backend — batching, sprite placement, text layout — is testable without a GL
/// context, and testing it here means those tests run in CI where no GPU exists.
/// </para>
/// <para>
/// A dedicated server runs the whole host on one with <see cref="IsRecording"/> off: the frame is composed as on a
/// client and goes nowhere, and nothing it asked for is kept.
/// </para>
/// </remarks>
public sealed partial class RecordingGraphicsDevice : IGraphicsDevice
{
    /// <summary>Gets or sets a value indicating whether what the device is asked for is kept in its lists; on by default.</summary>
    public bool IsRecording { get; set; } = true;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private void Record<T>(List<T> list, T item)
    {
        if (IsRecording)
        {
            list.Add(item);
        }
    }
}
