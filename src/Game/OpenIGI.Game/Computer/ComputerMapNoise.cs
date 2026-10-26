using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Computer;

/// <summary>
/// The interference over the map computer's picture, in both games: a level that fades by a thirtieth a tick and flares up
/// again at random, and flares hard whenever the zoom changes (Project I.G.I.'s <c>0x46A0D0</c> and <c>0x469FE0</c>).
/// </summary>
/// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
/// <remarks>Each flare takes two draws of the game's stream: the level, then the ticks until the next one.</remarks>
public sealed class ComputerMapNoise<TFpu>
    where TFpu : struct, IX87<TFpu>
{
    /// <summary>Gets or sets the interference's level; the picture's line filter is set to it each tick.</summary>
    public float Level { get; set; } = 1f;

    /// <summary>Gets or sets the ticks until it next flares.</summary>
    public int Timer { get; set; } = 60;

    /// <summary>Starts it as the map is made: full, and steady for two seconds.</summary>
    public void Reset()
    {
        Level = 1f;
        Timer = 60;
    }

    /// <summary>Runs it a tick: a flare to between a tenth and a half when its timer runs out, then the fade.</summary>
    /// <param name="random">The game's stream.</param>
    public void Run(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (Timer != 0)
        {
            Timer--;
        }
        else
        {
            Level = ((TFpu)random.NextDouble() * 0.4000000059604645 + 0.10000000149011612).F;
            Timer = 15 + (int)random.NextBounded(75);
        }

        Level = ((TFpu)Level - 0.03333333507180214).F;
    }

    /// <summary>Flares it as the zoom changes: to nearly full, for between half a second and three.</summary>
    /// <param name="random">The game's stream.</param>
    public void Flare(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Level = ((TFpu)random.NextDouble() * 0.05000000074505806 + 0.949999988079071).F;
        Timer = (int)random.NextBounded(75) + 15;
    }
}
