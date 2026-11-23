using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Igi1.Saves;

/// <summary>
/// What a save game keeps of the player's mission score beside its clock and loads (<see cref="Igi1PlayerSave"/>): the times it was
/// spotted, and the rounds its guns fired with the hits and kills they scored on the soldiers. <b>Not retail</b>, as I.G.I. 2's
/// save keeps its player's <c>tMissionScoreData</c>. A statement of its own, so a save written before it still loads.
/// </summary>
internal static class Igi1MissionScoreSave
{
    /// <summary>The statement a save keeps the score in.</summary>
    /// <param name="world">The played mission.</param>
    /// <returns>The section.</returns>
    public static ISaveSection Section(MissionWorld world) => new ExchangedSection("Igi1MissionScore", exchange =>
    {
        OpenIGI.Game.Player.ShotTally? shots = world.Player?.Shots;
        int spotted = exchange.Int(world.PlayerTask?.TimesSpotted ?? 0);
        int rounds = exchange.Int(shots?.RoundsFired ?? 0);
        int hits = exchange.Int(shots?.SoldierHits ?? 0);
        int kills = exchange.Int(shots?.SoldierKills ?? 0);
        if (!exchange.IsReading)
        {
            return;
        }

        if (world.PlayerTask is { } player)
        {
            player.TimesSpotted = spotted;
        }

        shots?.Restore(rounds, hits, kills);
    });
}
