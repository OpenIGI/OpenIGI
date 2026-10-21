using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that records what it is asked to draw instead of drawing it.
/// </summary>
/// <remarks>
/// Everything above the backend — batching, sprite placement, text layout — is testable without a GL
/// context, and testing it here means those tests run in CI where no GPU exists.
/// </remarks>
public sealed partial class RecordingGraphicsDevice : IGraphicsDevice
{
    public void Dispose()
    {
    }
}
