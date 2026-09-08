using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The march a round makes through level one's own collision, and the entry/exit classification it
/// turns on.
/// </summary>
/// <remarks>
/// <para>
/// The original resolves a shot by casting repeatedly while range is left, and every crossing is an
/// entry or an exit and never both — <c>0x4CD300</c> reports the pair as the two halves of one
/// condition (<c>*a7 = c0; *a8 = !c0</c>). An entry sounds and marks; an exit leaves only the
/// far-side decal and arms the latch that stops the round on its next entry.
/// </para>
/// <para>
/// Which side of a face is which was <b>measured, not assumed</b>: see
/// <see cref="TheFirstCrossingFromOutsideIsAlwaysAnEntry"/>.
/// </para>
/// </remarks>
public sealed class ShotMarchTests
{
    private const double U = 4096.0;

    /// <summary>The player's feet at the level-one doorway, the anchor DoorwayDiagnosticTests uses.</summary>
    private static readonly Vector3d Player = new(24501169, -55966720, 174413291);

    /// <summary>
    /// A ray arriving from outside the geometry crosses an entry face, by definition. Fanning
    /// sixty-four of them in from every compass direction says which sign of the face's own facing
    /// that is — and it is unanimous, which is what makes it safe to key behaviour off.
    /// </summary>
    [RequiresGameDataFact]
    public void TheFirstCrossingFromOutsideIsAlwaysAnEntry()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);
        int rays = 0;
        int entries = 0;

        for (int a = 0; a < 64; a++)
        {
            double angle = a * (2.0 * Math.PI / 64.0);
            Vector3d dir = new(Math.Cos(angle), Math.Sin(angle), -0.35);
            Vector3d origin = new(
                Player.X - (dir.X * 30.0 * U),
                Player.Y - (dir.Y * 30.0 * U),
                Player.Z + (12.0 * U));

            if (!index.TryCast(origin, origin + (dir * (80.0 * U)), out WorldRayHit hit))
            {
                continue;
            }

            rays++;
            if (!hit.IsExit)
            {
                entries++;
            }
        }

        Assert.True(rays >= 32, $"only {rays} of 64 rays met anything");
        Assert.Equal(rays, entries);
    }

    /// <summary>
    /// Marching the way the shot resolution does — cast, step five millimetres past the face, cast
    /// again — walks a solid wall as an entry and then an exit, and reports the same material for
    /// both sides of it. That pairing is what the exit decal and the stop-on-next-entry latch rest on.
    /// </summary>
    [RequiresGameDataFact]
    public void AWallIsEnteredAndThenLeftAgain()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

        // Head height, a metre and a half right of the doorway: solid wall, walking into it (+Y).
        Vector3d at = new(Player.X + (1.5 * U), Player.Y, Player.Z + (1.6 * U));
        Vector3d dir = new(0, 1, 0);
        float remaining = (float)(8.0 * U);

        List<(bool IsExit, int Material, double Metres)> crossings = [];
        for (int pass = 0; pass < 8 && remaining > 0f; pass++)
        {
            if (!index.TryCast(at, at + (dir * remaining), out WorldRayHit hit))
            {
                break;
            }

            crossings.Add((hit.IsExit, hit.Material, hit.Distance / U));
            remaining -= (float)hit.Distance;
            at = new Vector3d(
                hit.Position.X + (dir.X * GunShotBallistics.PenetrationStepOffUnits),
                hit.Position.Y + (dir.Y * GunShotBallistics.PenetrationStepOffUnits),
                hit.Position.Z + (dir.Z * GunShotBallistics.PenetrationStepOffUnits));
        }

        // The march reaches the far side of what it enters rather than stopping at the first face.
        Assert.True(crossings.Count >= 4, $"the march made only {crossings.Count} crossings");

        // They alternate: nothing is entered twice without being left, or left without being entered.
        for (int i = 0; i < crossings.Count; i++)
        {
            Assert.Equal(i % 2 == 1, crossings[i].IsExit);
        }

        // And each pair is the same surface going in and coming out.
        Assert.Equal(crossings[0].Material, crossings[1].Material);
        Assert.Equal(crossings[2].Material, crossings[3].Material);

        // The second wall is a real thickness apart, not a coincident pair — the exit is measurably
        // beyond the entry, which is what gives an exit decal somewhere to sit.
        Assert.InRange(crossings[3].Metres, 0.05, 3.0);
    }
}
