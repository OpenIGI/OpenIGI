using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player.Aim;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Modern aim's lean with the weapon up, and its brace on tops and corners, against boxes laid out in code.</summary>
public sealed class WeaponMountTests
{
    private const double M = WorldScene.UnitsPerMetre;

    // The eye of a crouched player at the origin facing +Y: a metre up.
    private static readonly Vector3d Eye = new(0.0, 0.0, 1.0 * M);

    [Fact]
    public void ACrateInFrontIsATopAndTheEyeRisesToClearIt()
    {
        // A crate 0.5 m ahead, a metre deep, its top at 1.05 m: a crouched player's eye is just under it.
        WorldRayCast crate = Boxes(new Box(-1, 0.5, 0, 1, 1.5, 1.05));
        MountSurface surface = Assert.IsType<MountSurface>(MountProbe.Find(Eye, 0f, crate));
        Assert.Equal(MountKind.Top, surface.Kind);
        Assert.Equal(1.05 * M, surface.SurfaceZ, 0);
        Assert.Equal((float)((1.05 + MountProbe.SightClearanceMetres - 1.0) * M), surface.Raise, 0);
    }

    [Fact]
    public void ALowTableBendsTheEyeDownToIt()
    {
        Vector3d standing = new(0.0, 0.0, 1.6 * M);
        WorldRayCast table = Boxes(new Box(-0.6, 0.35, 0, 0.6, 1.2, 1.15));
        MountSurface surface = Assert.IsType<MountSurface>(MountProbe.Find(standing, 0f, table));
        Assert.Equal(MountKind.Top, surface.Kind);
        Assert.True(surface.Raise < 0f, "A top below the eye should bend the eye down to it.");
    }

    [Fact]
    public void AWallTallerThanTheReachIsNoTop()
    {
        WorldRayCast wall = Boxes(new Box(-1, 0.5, 0, 1, 0.7, 3.0));
        Assert.NotEqual(MountKind.Top, MountProbe.Find(Eye, 0f, wall)?.Kind);
    }

    [Fact]
    public void OpenFloorIsNothingToBraceOn()
    {
        Assert.Null(MountProbe.Find(Eye, 0f, Boxes()));
    }

    [Fact]
    public void AWallWithSpaceToTheLeftIsACornerToLeanOutOf()
    {
        // A wall square in front that ends 0.2 m to the left of the eye.
        WorldRayCast wall = Boxes(new Box(-0.2, 0.4, 0, 3, 0.6, 3.0));
        MountSurface surface = Assert.IsType<MountSurface>(MountProbe.Find(Eye, 0f, wall));
        Assert.Equal(MountKind.CornerLeft, surface.Kind);
    }

    [Fact]
    public void TheBraceHoldsTheLookToItsSectorAndTheEyeSettlesOnIt()
    {
        WeaponMount mount = new();
        AdsProfile profile = new() { ScriptId = "W" };
        mount.Mount(new MountSurface(MountKind.Top, 1.05 * M, (float)(0.14 * M)), profile, yaw: 0f);
        for (int i = 0; i < 20; i++)
        {
            mount.Tick();
        }

        Assert.Equal((float)(0.14 * M), mount.Raise, 0);
        (float yaw, float pitch) = mount.Limit.Clamp(2f, -1.2f);
        Assert.Equal(WeaponMount.YawHalfWidthDegrees * MathF.PI / 180f, yaw, 4);
        Assert.Equal(-30f * MathF.PI / 180f, pitch, 4);

        mount.Dismount();
        for (int i = 0; i < 20; i++)
        {
            mount.Tick();
        }

        Assert.Equal(0f, mount.Raise);
        Assert.False(mount.IsMounted);
    }

    [Fact]
    public void TheLeanTakesItsTimeEitherWayAndHoldingBothStandsUpright()
    {
        CombatLean lean = new();
        int ticks = 0;
        while (lean.Amount < 1f)
        {
            lean.Tick(CombatLean.Target(left: false, right: true));
            ticks++;
        }

        Assert.Equal((int)MathF.Ceiling(CombatLean.Seconds / AimDownSights.TickSeconds), ticks);
        EyeLean eye = CombatLean.Eye(lean.Amount);
        Assert.Equal((float)(CombatLean.SideMetres * M), eye.Across, 1);
        Assert.True(eye.Up < 0f && eye.Roll > 0f);
        Assert.Equal(0f, CombatLean.Target(left: true, right: true));
        Assert.True(CombatLean.Eye(-1f).Roll < 0f);
    }

    // Axis-aligned boxes in metres, (x0, y0, z0) to (x1, y1, z1), as a world the rays are cast against.
    private readonly record struct Box(double X0, double Y0, double Z0, double X1, double Y1, double Z1);

    private static WorldRayCast Boxes(params Box[] boxes) => (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
    {
        hit = default;
        double best = double.PositiveInfinity;
        Vector3d direction = end - origin;
        foreach (Box box in boxes)
        {
            if (Slab(origin, direction, box, out double t, out Vector3f normal) && t < best)
            {
                best = t;
                hit = new WorldRayHit(origin + (direction * t), normal, t * direction.Length);
            }
        }

        return !double.IsPositiveInfinity(best);
    };

    // The ray's first entry into a box, as a share of its length, and the face it enters through.
    private static bool Slab(Vector3d origin, Vector3d direction, Box box, out double entry, out Vector3f normal)
    {
        double[] o = [origin.X, origin.Y, origin.Z];
        double[] d = [direction.X, direction.Y, direction.Z];
        double[] lo = [box.X0 * M, box.Y0 * M, box.Z0 * M];
        double[] hi = [box.X1 * M, box.Y1 * M, box.Z1 * M];
        entry = 0.0;
        double exit = 1.0;
        int axis = -1;
        double sign = 0.0;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(d[i]) < 1e-12)
            {
                if (o[i] < lo[i] || o[i] > hi[i])
                {
                    normal = default;
                    return false;
                }

                continue;
            }

            double a = (lo[i] - o[i]) / d[i];
            double b = (hi[i] - o[i]) / d[i];
            double near = Math.Min(a, b);
            if (near > entry)
            {
                entry = near;
                axis = i;
                sign = d[i] > 0 ? -1.0 : 1.0;
            }

            exit = Math.Min(exit, Math.Max(a, b));
        }

        normal = axis switch
        {
            0 => new Vector3f((float)sign, 0f, 0f),
            1 => new Vector3f(0f, (float)sign, 0f),
            2 => new Vector3f(0f, 0f, (float)sign),
            _ => default,
        };
        return axis >= 0 && entry <= exit && entry <= 1.0;
    }
}
