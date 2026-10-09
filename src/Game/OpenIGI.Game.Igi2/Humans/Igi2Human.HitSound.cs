namespace OpenIGI.Game.Igi2.Humans;

/// <content>The pain a human cries out when hurt.</content>
public abstract partial class Igi2Human
{
    /// <summary>Gets or sets the tick before which no new pain sound starts, <c>Human_s + 0x7B0</c>.</summary>
    public int NextHitSoundTick { get; set; }

    /// <summary>
    /// Cries out, <c>Human_HitSound</c> (<c>igi2 0x503180</c>): unless the last cry is too recent, the one playing stops, the next
    /// may start a second to two seconds on, and one of the sounds is picked by the run stream's next word.
    /// </summary>
    /// <param name="sounds">The sounds to pick from.</param>
    public void PlayHitSound(IReadOnlyList<string> sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        if (Ticks < NextHitSoundTick)
        {
            return;
        }

        HitSound?.Delete();
        HitSound = null;
        Fpu wait = (Fpu)Random.NextDouble() * -30.0;
        NextHitSoundTick = Ticks - Fpu.ToInt32(wait) + 30;
        uint pick = Random.NextRaw();
        HitSound = Surroundings.StartSound(sounds[(int)(pick % (uint)sounds.Count)]);
    }
}
