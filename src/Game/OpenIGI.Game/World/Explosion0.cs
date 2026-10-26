using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// An explosion going off, <c>Explosion0</c>: it waits out its timer, goes off, and waits a random share of its period before
/// each further time. Both titles run it the same way (Project I.G.I.'s <c>0x480F40</c>, I.G.I. 2's
/// <c>Explosion0_RunHandler</c>, <c>igi2 0x50A100</c>); what going off does is each title's own.
/// </summary>
public abstract class Explosion0 : IDynamicTask
{
    private readonly IgiRandom _random;

    /// <summary>Initializes a new instance of the <see cref="Explosion0"/> class.</summary>
    /// <param name="random">The stream its waits are drawn from.</param>
    /// <param name="count">How many times it goes off, <c>nExplosion</c>.</param>
    /// <param name="period">The ticks before it first goes off, and the bound on each wait after.</param>
    protected Explosion0(IgiRandom random, int count, int period)
    {
        _random = random;
        Count = count;
        Period = period;
        Timer = period;
    }

    /// <summary>Gets how many more times it goes off.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the ticks before it first goes off, and the bound on each wait after.</summary>
    public int Period { get; }

    /// <summary>Gets the ticks before it next goes off.</summary>
    public int Timer { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Each time but the last is followed by a wait of the stream's next word modulo the period.</remarks>
    public bool Run()
    {
        if (Timer != 0)
        {
            Timer--;
        }

        if (Timer != 0 || Count == 0)
        {
            return true;
        }

        GoOff();
        Count--;
        if (Count == 0)
        {
            return false;
        }

        Timer = (int)(_random.NextRaw() % (uint)Period);
        return true;
    }

    /// <summary>What going off does: the title's fireballs, smoke, fragments, sound and blast.</summary>
    protected abstract void GoOff();
}
