using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The weapons a level draws of its own as models: those lying where they were dropped, and the grenades and missiles in flight.
/// </summary>
/// <remarks>
/// Each is named by itself and the tick, so the picture draws it between where it was a tick ago and where it is and stamps its
/// motion; drawn at the tick's pose alone, a grenade stepped at the tick rate and trailed a ghost under the temporal passes.
/// </remarks>
public static class Igi2LevelModels
{
    /// <summary>The weapons lying in the world.</summary>
    /// <param name="lying">The level's pickups, or <see langword="null"/>.</param>
    /// <param name="tick">The tick the models are for.</param>
    /// <returns>One model a weapon.</returns>
    public static IEnumerable<TitleWorldModel> Lying(Igi2PickupList? lying, long tick) =>
        lying is null
            ? []
            : lying.Items.Where(static p => p.IsInWorld)
                .Select(p => new TitleWorldModel(p.WeaponType.FixedModel, p.Position, Rows(p.Orientation), Instance: p, Tick: tick));

    /// <summary>The grenades, impact grenades and missiles in flight.</summary>
    /// <param name="grenades">The level's grenades, or <see langword="null"/>.</param>
    /// <param name="tick">The tick the models are for.</param>
    /// <returns>One model a round.</returns>
    public static IEnumerable<TitleWorldModel> InFlight(Igi2Grenades? grenades, long tick) =>
        grenades is null
            ? []
            : grenades.Flying.Select(g => new TitleWorldModel(g.Model, g.Position, Rows(g.Orientation), Instance: g, Tick: tick))
                .Concat(grenades.Dynamic.Tasks.OfType<Igi2ImpactGrenade>()
                    .Where(static g => g.Parameters.Model.Length != 0)
                    .Select(g => new TitleWorldModel(g.Parameters.Model, g.Position, Rows(g.Orientation), Instance: g, Tick: tick)))
                .Concat(grenades.Dynamic.Tasks.OfType<Igi2Missile>()
                    .Select(m => new TitleWorldModel(m.Model, m.Position, Rows(m.Body.Orientation), Instance: m, Tick: tick)));

    /// <summary>A frame given row by row, as a matrix.</summary>
    /// <param name="m">The frame's nine numbers, row by row.</param>
    /// <returns>The matrix.</returns>
    internal static Matrix3f Rows(IReadOnlyList<float> m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
}
