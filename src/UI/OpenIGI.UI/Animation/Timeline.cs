namespace OpenIGI.UI.Animation;

/// <summary>
/// A group of tweens advanced together, each optionally feeding its value to a callback, with helpers
/// for staggering their start.
/// </summary>
/// <remarks>
/// This is what plays an orchestrated entrance: a row of menu items that slide and fade in one after
/// another, a settings panel whose sections cascade. Each tween carries its own <see cref="Tween.Delay"/>,
/// so staggering is just handing successive items a growing delay — see <see cref="StaggerDelay"/>. The
/// timeline itself only fans <see cref="Update"/> out and reports when the whole group has settled.
/// </remarks>
public sealed class Timeline
{
    private readonly List<Entry> _entries = [];

    /// <summary>Gets the number of tweens in the timeline.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets a value indicating whether every tween has finished.</summary>
    public bool IsComplete
    {
        get
        {
            foreach (Entry entry in _entries)
            {
                if (!entry.Tween.IsComplete)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Computes the start delay for a staggered item.</summary>
    /// <param name="index">The item's index in the sequence.</param>
    /// <param name="step">The delay added per item, in seconds.</param>
    /// <param name="baseDelay">The delay before the first item, in seconds.</param>
    /// <returns>The delay for the item at <paramref name="index"/>.</returns>
    public static float StaggerDelay(int index, float step, float baseDelay = 0f) =>
        baseDelay + (index * step);

    /// <summary>Adds a tween to the timeline.</summary>
    /// <param name="tween">The tween to advance each frame.</param>
    /// <param name="apply">An optional callback given the tween's value after each update.</param>
    /// <returns>The tween, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tween"/> is <see langword="null"/>.</exception>
    public Tween Add(Tween tween, Action<float>? apply = null)
    {
        ArgumentNullException.ThrowIfNull(tween);
        _entries.Add(new Entry(tween, apply));
        return tween;
    }

    /// <summary>Advances every tween by a real-time delta and fires their callbacks.</summary>
    /// <param name="elapsedSeconds">The seconds since the last update.</param>
    public void Update(float elapsedSeconds)
    {
        foreach (Entry entry in _entries)
        {
            float value = entry.Tween.Update(elapsedSeconds);
            entry.Apply?.Invoke(value);
        }
    }

    /// <summary>Removes every tween.</summary>
    public void Clear() => _entries.Clear();

    private readonly record struct Entry(Tween Tween, Action<float>? Apply);
}
