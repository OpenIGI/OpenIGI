using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The world a <see cref="RetailCarBody"/> steps in during play.</summary>
/// <remarks>
/// A batch of rays goes first to the level's placed models and spline segments and to the other vehicles at their
/// live poses (<see cref="RetailWorldObjects"/>), skipping the casting car and anything in a dormant container,
/// then to the terrain (<see cref="RetailTerrainRay"/>), which replaces an object hit only when strictly nearer --
/// the order <c>0x4CC2A0</c> runs them in. Not ported: the upward burial probe (<c>0x4CA010</c>, cast only by a car
/// with non-wheel collision points), rigid spline segments, water, and the struck object's contact handlers.
/// </remarks>
/// <param name="terrain">The level's exact terrain query.</param>
/// <param name="objects">The level's placed models and spline segments, or <see langword="null"/>.</param>
/// <param name="vehicles">The vehicles other rays can strike, at their live poses, or <see langword="null"/>.</param>
/// <param name="present">Whether an object's owner is in the world now, or <see langword="null"/> for always.</param>
/// <param name="touchGlass">Breaks a pane a body strikes from where it is, answering whether it broke.</param>
public sealed class RetailCarWorld(
    RetailTerrainRay terrain,
    RetailWorldObjects? objects = null,
    Func<IReadOnlyList<RetailRayModel>>? vehicles = null,
    Func<object, bool>? present = null,
    Func<RetailGlassPane, Vector3d, bool>? touchGlass = null) : IRetailCarWorld
{
    private readonly RetailTerrainRay _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));

    /// <inheritdoc/>
    public void CastRays(RetailRigidBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
    {
        for (int k = 0; k < results.Length; k++)
        {
            results[k] = new RetailRayResult { Point = ends[k] };
        }

        objects?.Cast(starts, ends, results, car?.Owner, vehicles?.Invoke(), present);
        _terrain.Cast(starts, ends, results);
    }

    /// <inheritdoc/>
    public void ProbeUp(RetailRigidBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results)
    {
        for (int k = 0; k < results.Length; k++)
        {
            results[k] = default;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A pane of glass answers the physics object's hit (<c>0x449CB0</c>): it breaks, its shards thrown from where the body is,
    /// and the contact is handled -- the body goes on through. Nothing else a body strikes answers.
    /// </remarks>
    public bool VetoContact(RetailRigidBody car, in RetailRayResult result) =>
        result.Struck is RetailGlassPane pane
        && !pane.IsBroken
        && touchGlass?.Invoke(pane, car is null ? result.Point : new Vector3d(car.Position[0], car.Position[1], car.Position[2])) == true;
}
