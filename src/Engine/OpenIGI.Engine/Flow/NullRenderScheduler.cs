namespace OpenIGI.Engine.Flow;

/// <summary>
/// An <see cref="IRenderScheduler"/> that draws nothing, for headless runs and tools.
/// </summary>
/// <remarks>
/// The render pass still happens — tasks still receive <see cref="OpenIGI.Engine.Tasks.TaskMessage.Render"/>
/// and the frame counters still advance — so the schedule a headless run produces is the same one a
/// rendering run produces.
/// </remarks>
public sealed class NullRenderScheduler : IRenderScheduler
{
    /// <summary>Gets the shared instance.</summary>
    public static NullRenderScheduler Instance { get; } = new();

    private NullRenderScheduler()
    {
    }

    /// <inheritdoc/>
    public void Flush()
    {
    }

    /// <inheritdoc/>
    public void Reset()
    {
    }

    /// <inheritdoc/>
    public void SetFirstFrame(bool isFirstFrame)
    {
    }

    /// <inheritdoc/>
    public void SetTickRate(int tickRate)
    {
    }
}
