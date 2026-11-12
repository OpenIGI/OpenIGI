using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Saves;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Saves;

/// <summary>
/// What a save game keeps of a Project I.G.I. level that belongs to none of its tasks and to no one registry: the streams of
/// chance, so what is drawn after a load is what would have been drawn; each minefield's wait; and the panes of glass a car
/// breaks. Not retail: the original kept no save games.
/// </summary>
internal static class Igi1LevelSave
{
    /// <summary>The statement a save keeps them in.</summary>
    /// <param name="world">The played mission.</param>
    /// <returns>The section.</returns>
    public static ISaveSection Section(MissionWorld world) => new ExchangedSection("Igi1Level", exchange =>
    {
        Stream(exchange, world.SoundRandom);
        Stream(exchange, world.MissileStream.Random);
        Stream(exchange, world.Soldiers?.AiRandom);

        int[] waits = exchange.Run([.. world.MineFields.Select(static f => f.Pause)], static (e, ticks) => e.Int(ticks));
        if (exchange.IsReading)
        {
            for (int i = 0; i < world.MineFields.Count && i < waits.Length; i++)
            {
                world.MineFields[i].Pause = waits[i];
            }
        }

        if (world.RetailWorld is RetailCarWorld { Objects: { } objects })
        {
            objects.ExchangeGlass(exchange);
        }
        else
        {
            exchange.Run<bool>([], static (e, broken) => e.Bool(broken));
        }
    });

    // A generator's three words; one the level does not have still takes its place in the statement.
    private static void Stream(StateExchange exchange, IgiRandom? random)
    {
        (uint a, uint b, uint c) = random?.State ?? default;
        uint keptA = unchecked((uint)exchange.Int(unchecked((int)a)));
        uint keptB = unchecked((uint)exchange.Int(unchecked((int)b)));
        uint keptC = unchecked((uint)exchange.Int(unchecked((int)c)));
        if (exchange.IsReading && random is not null)
        {
            random.Seed(keptA, keptB, keptC);
        }
    }
}
