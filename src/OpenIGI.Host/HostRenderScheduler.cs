using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Flow;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The frame arbiter's render hooks, wired to the graphics device.
/// </summary>
/// <remarks>
/// <para>
/// The original calls <c>Render_Flush</c> and then <c>Render_Reset(0, 0)</c> at the head of a
/// <em>tick</em> — not around a render — and that is where the present lands. It matches the
/// present-then-begin shape of <c>DisplaySwapBuffers</c>: the buffer drawn last frame is shown, and
/// the new one is cleared to the colour <c>Render_Reset</c> is given. See
/// the original and the original.
/// </para>
/// <para>
/// Putting it here rather than in the loop body is the point of the seam: the arbiter decides
/// <em>when</em>, the shell only says what it means on this platform.
/// </para>
/// </remarks>
public sealed class DesktopRenderScheduler : IRenderScheduler
{
    private readonly IGraphicsDevice _device;

    /// <summary>Initializes a new instance of the <see cref="DesktopRenderScheduler"/> class.</summary>
    /// <param name="device">The device to present and clear.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public DesktopRenderScheduler(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets or sets the colour the back buffer is cleared to.</summary>
    /// <remarks>The original passes zero for both of <c>Render_Reset</c>'s arguments.</remarks>
    public Bgra32 ClearColour { get; set; } = Bgra32.Black;

    /// <inheritdoc/>
    public void Flush()
    {
        // Timed only when the pacing probe is listening: this is the hot path, and the present block
        // is exactly what the probe needs to separate a slow frame from a badly scheduled one.
        if (!Diagnostics.PresentationPacingProbe.IsEnabled)
        {
            _device.SwapBuffers();
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _device.SwapBuffers();
        Diagnostics.PresentationPacingProbe.RecordPresentBlock(
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency);
    }

    /// <inheritdoc/>
    public void Reset() => _device.Clear(ClearColour);

    /// <inheritdoc/>
    /// <remarks>The original uses this to exclude the first frames' cost from the schedule; nothing here needs it yet.</remarks>
    public void SetFirstFrame(bool isFirstFrame)
    {
    }

    /// <inheritdoc/>
    public void SetTickRate(int tickRate)
    {
    }
}
