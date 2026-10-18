using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's conveyor belts (<c>ConveyorBelt_PushObjectHandler</c>, <c>igi2 0x53A7D0</c>): what they push a human on them
/// with, their speeds set by script (<c>ConveyorBelt_SetSpeed</c>, <c>0x53A5F0</c>), and the push the player's footing asks for.
/// </summary>
public sealed class RetailIgi2ConveyorBeltTests
{
    /// <summary>The belt that runs at ten metres a second along its X, <c>ConveyorBelt_3456</c>.</summary>
    private const int Running = 3456;

    /// <summary>The sloping belt of the lower building, <c>ConveyorBelt_3758</c>, still until a script starts it.</summary>
    private const int Sloping = 3758;

    /// <summary>
    /// A belt pushes by its speeds made units a tick and turned by its frame, but only where the footing is of its material;
    /// <c>ConveyorBelt_SetSpeed</c> holds a speed within twenty metres a second either way.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_belt_pushes_on_its_own_material_at_its_speeds()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2ConveyorBeltTask[] belts = [.. m.Tree.OfType<Igi2ConveyorBeltTask>()];
        Assert.Equal(2, belts.Length);
        Igi2ConveyorBeltTask running = belts.Single(static b => b.Id == Running);
        Assert.Equal((10f, 0f), (running.XSpeed, running.YSpeed));

        float[] f = Igi2Pose.Rotation(running.Orientation.X, running.Orientation.Y, running.Orientation.Z);
        float x = ((Fpu)10f * 136.53334f).F;
        Vector3f expected = new(
            ((((Fpu)f[1] * 0f) + ((Fpu)f[0] * x)) + ((Fpu)f[2] * 0.0)).F,
            ((((Fpu)f[4] * 0f) + ((Fpu)f[3] * x)) + ((Fpu)f[5] * 0.0)).F,
            ((((Fpu)f[7] * 0f) + ((Fpu)f[6] * x)) + ((Fpu)f[8] * 0.0)).F);
        Assert.Equal(expected, running.Push(Igi2ConveyorBeltTask.MaterialName));
        Assert.Equal(default, running.Push("Metal"));
        Assert.Equal(default, running.Push(null));

        Igi2ConveyorBeltTask sloping = belts.Single(static b => b.Id == Sloping);
        Assert.Equal(0f, sloping.XSpeed);
        m.Run.Evaluate(Igi2LevelExpressions.Create("ConveyorBelt_SetSpeed(3758, -30, 0)", "test", m.Run.Natives));
        Assert.Equal((-20f, 0f), (sloping.XSpeed, sloping.YSpeed));
        m.Run.Evaluate(Igi2LevelExpressions.Create("ConveyorBelt_SetSpeed(3758, 7.5, 30)", "test", m.Run.Natives));
        Assert.Equal((7.5f, 20f), (sloping.XSpeed, sloping.YSpeed));
    }

    /// <summary>
    /// What the player stands on answers the push (<c>Human_Move</c>, <c>igi2 0x501685</c>): the belt's own solid where the footing
    /// is of the belt's material, and nothing anywhere else. Mission 1's belts walk on faces of <c>HumanCollision</c> under their
    /// running surface, so a body standing on one stands below the faces that push.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_belt_underfoot_answers_the_push()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2ConveyorBeltTask belt = m.Tree.OfType<Igi2ConveyorBeltTask>().Single(static b => b.Id == Running);
        Igi2GameMaterials materials = m.Materials;
        int row = Enumerable.Range(0, Igi2GameMaterials.Rows).First(r => materials[r]?.Name == Igi2ConveyorBeltTask.MaterialName);
        Vector3d top = Surface(m.Collision!, belt, materials);

        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, null, top, 0f);
        player.Conveyors[belt.Id] = belt;
        player.Materials = materials;
        Igi2RigidObject solid = m.Collision!.SolidsOf(Igi2ConveyorBeltTask.TypeName, belt.Id)[0];
        Igi2Intersection onBelt = new() { EnterMaterial = row };
        Assert.Equal(belt.Push(Igi2ConveyorBeltTask.MaterialName), player.Human.Surroundings.PushOnConveyor(solid, onBelt));

        Igi2RigidObject other = m.Collision!.Objects.First(o => o.Owner is Igi2CollisionOwner { TypeName: not Igi2ConveyorBeltTask.TypeName });
        Assert.Equal(default(Vector3f), player.Human.Surroundings.PushOnConveyor(other, onBelt));
    }

    // The highest point of the belt's own material under its position, found by a line straight down onto its finest mesh.
    private static Vector3d Surface(Igi2CollisionWorld world, Igi2ConveyorBeltTask belt, Igi2GameMaterials materials)
    {
        Igi2RigidObject solid = world.SolidsOf(Igi2ConveyorBeltTask.TypeName, belt.Id)[0];
        float[] f = Igi2Pose.Rotation(belt.Orientation.X, belt.Orientation.Y, belt.Orientation.Z);
        for (double x = -8192.0; x <= 8192.0; x += 512.0)
        {
            for (double y = -8192.0; y <= 8192.0; y += 512.0)
            {
                Vector3d at = belt.Position + new Vector3d((f[0] * x) + (f[1] * y), (f[3] * x) + (f[4] * y), 16384.0);
                Igi2LineTrace trace = new(1) { Mesh = 0 };
                trace.Starts[0] = at;
                world.NextZ(trace, [-32768.0], ~Igi2CollisionWorld.TerrainGroup);
                Igi2Intersection hit = trace.Intersections[0];
                if (hit.IsHit && ReferenceEquals(hit.HitObject, solid) && hit.EnterMaterial is int row
                    && materials[row]?.Name == Igi2ConveyorBeltTask.MaterialName)
                {
                    return hit.Position;
                }
            }
        }

        throw new InvalidOperationException($"No line down onto ConveyorBelt_{belt.Id} meets its own material.");
    }
}
