using System.Diagnostics;

namespace OpenIGI.Host.Infrastructure;

/// <summary>
/// Holds a loop to a target frame rate by waiting out the remainder of each frame's interval, for the
/// menu, which renders uncapped and has nothing to gain past the display's own rate.
/// </summary>
/// <remarks>
/// <para>
/// A menu that renders as fast as the machine can pegs the GPU at full load for a picture that changes
/// only as fast as the display shows it. That costs heat and noise for nothing, and it costs something
/// visible too: with the queue always full, the small uploads the menu does between frames -- a
/// backdrop's residency, a preview being adopted -- land as hitches, because there is no slack for them
/// to land in.
/// </para>
/// <para>
/// The wait is a sleep for the coarse part and a spin for the last stretch, because a bare sleep on
/// Windows is granted in whole scheduler ticks and would overshoot by up to one.
/// </para>
/// </remarks>
internal sealed class FramePacer
{
    private const double SpinMilliseconds = 1.5;

    private long _nextDeadline = -1;

    /// <summary>Waits until the current frame's interval has elapsed, then schedules the next.</summary>
    /// <param name="targetHertz">The rate to hold to; nothing happens at or below zero.</param>
    public void Pace(int targetHertz)
    {
        if (targetHertz <= 0)
        {
            Reset();
            return;
        }

        long interval = Stopwatch.Frequency / targetHertz;
        long now = Stopwatch.GetTimestamp();

        if (_nextDeadline < 0 || now - _nextDeadline > interval)
        {
            // First frame, or the loop fell far behind: do not try to catch up, just start a fresh cadence.
            _nextDeadline = now + interval;
            return;
        }

        long spinTicks = (long)(SpinMilliseconds * Stopwatch.Frequency / 1000.0);
        while (true)
        {
            long remaining = _nextDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                break;
            }

            if (remaining > spinTicks)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(64);
            }
        }

        _nextDeadline += interval;
    }

    /// <summary>Forgets the cadence, so the next <see cref="Pace"/> starts fresh rather than catching up.</summary>
    public void Reset() => _nextDeadline = -1;
}
