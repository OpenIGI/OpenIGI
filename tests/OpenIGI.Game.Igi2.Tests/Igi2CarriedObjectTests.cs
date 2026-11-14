using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// What a level's containers keep out of the world, and what an object that moves carries with it: the two things a helicopter
/// that flies off at a mission's start needs, and had neither of.
/// </summary>
public sealed class Igi2CarriedObjectTests
{
    private const double Tolerance = 0.01;

    /// <summary>An object held to a parent stays where it sat against it, however the parent then turns and moves.</summary>
    /// <remarks>
    /// <c>ModelObj_CalcRelativePosAndOrientation</c> keeps the object's place and frame in the parent's own, and
    /// <c>ModelObj_UpdateAttachment</c> turns them back out. Two units ahead of a parent that then faces a quarter turn round and
    /// stands somewhere else is two units ahead of it the new way.
    /// </remarks>
    [Fact]
    public void A_held_object_follows_its_parent()
    {
        Carrier parent = new() { AttachPosition = new Vector3d(100.0, 200.0, 300.0), AttachOrientation = EngineEuler.Rotation(0f, 0f, 0f) };
        Igi2Attachment attachment = new();
        attachment.Attach(parent, new Vector3d(102.0, 200.0, 301.0), EngineEuler.Rotation(0f, 0f, 0f));

        float[] turned = EngineEuler.Rotation(0f, 0f, MathF.PI / 2f);
        parent.AttachOrientation = turned;
        parent.AttachPosition = new Vector3d(1000.0, 2000.0, 3000.0);

        Assert.True(attachment.TryResolve(out Vector3d position, out float[] frame));

        // The parent's first column is where its own X now points.
        Assert.Equal(1000.0 + (2.0 * turned[0]), position.X, Tolerance);
        Assert.Equal(2000.0 + (2.0 * turned[3]), position.Y, Tolerance);
        Assert.Equal(3001.0, position.Z, Tolerance);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(turned[i], frame[i], 4);
        }
    }

    /// <summary>An object held to nothing is not moved.</summary>
    [Fact]
    public void An_object_held_to_nothing_stays()
    {
        Igi2Attachment attachment = new();
        attachment.Attach(null, new Vector3d(1.0, 2.0, 3.0), EngineEuler.Rotation(0f, 0f, 0f));

        Assert.False(attachment.TryResolve(out _));
        Assert.False(attachment.TryResolve(out _, out _));
    }

    /// <summary>
    /// The helicopter of mission 4's landing cutscene leaves the world with the container that holds it, and the one that flies off
    /// after it is in the world with its own.
    /// </summary>
    /// <remarks>
    /// <c>ConditionalContainer_RunHandler</c> removes a container's children from the world when its condition stops holding. The
    /// level keeps one helicopter under "Jones jumping out of chopper" for the cutscene and another under "Tasks disabled during
    /// cutscenes" for play, at the same place; with neither taken out, the first stood where the cutscene left it while the second
    /// flew away.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_stopped_containers_objects_leave_the_world()
    {
        Igi2Level level = Igi2Level.Find("location1", "level4");
        Igi2WorldPresence presence = Assert.IsType<Igi2WorldPresence>(Igi2WorldPresence.Build(level.Root, null));

        Igi2EditRigidObjTask[] helicopters =
        [
            .. Walk(level.Root).OfType<Igi2EditRigidObjTask>().Where(static prop => prop.Model == "710_01_1"),
        ];
        Assert.Equal(3, helicopters.Length);

        foreach (Igi2EditRigidObjTask helicopter in helicopters)
        {
            ConditionalContainerTask container = Containers(helicopter).First();
            container.IsActive = true;
            Assert.True(presence.IsInWorld(helicopter.Id));

            container.IsActive = false;
            Assert.False(presence.IsInWorld(helicopter.Id));
            container.IsActive = true;
        }
    }

    /// <summary>Mission 4's helicopters are each what a guide moves, and each carries its pilot and its exhausts' smoke.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_guided_helicopter_carries_what_is_beneath_it()
    {
        Igi2Level level = Igi2Level.Find("location1", "level4");
        Igi2EditRigidObjTask helicopter = Walk(level.Root).OfType<Igi2EditRigidObjTask>().First(static prop => prop.Id == 3321);
        Igi2EditBoneObjTask pilot = helicopter.Children.OfType<Igi2EditBoneObjTask>().Single();
        Igi2SmokeTask[] smoke = [.. helicopter.Children.OfType<Igi2SmokeTask>()];
        Assert.Equal(2, smoke.Length);

        Vector3d seat = Offset(pilot.Position, helicopter.CurrentPosition);
        Vector3d exhaust = Offset(smoke[0].Position, helicopter.CurrentPosition);
        Igi2Attachment.AttachChildren(helicopter, helicopter);

        // Lifted straight up without turning: everything beneath it rises as far.
        Vector3d raised = new(helicopter.CurrentPosition.X, helicopter.CurrentPosition.Y, helicopter.CurrentPosition.Z + 40960.0);
        helicopter.Ride(raised, [.. helicopter.CurrentOrientation], tick: 1);

        AssertNear(seat, Offset(pilot.Position, raised));
        AssertNear(exhaust, Offset(smoke[0].Position, raised));
    }

    private static Vector3d Offset(Vector3d of, Vector3d from) => new(of.X - from.X, of.Y - from.Y, of.Z - from.Z);

    private static void AssertNear(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, 1.0);
        Assert.Equal(expected.Y, actual.Y, 1.0);
        Assert.Equal(expected.Z, actual.Z, 1.0);
    }

    private static IEnumerable<ConditionalContainerTask> Containers(GameTask task)
    {
        for (GameTask? above = task.Parent; above is not null; above = above.Parent)
        {
            if (above is ConditionalContainerTask container)
            {
                yield return container;
            }
        }
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class Carrier : IAttachParent
    {
        public Vector3d AttachPosition { get; set; }

        public IReadOnlyList<float> AttachOrientation { get; set; } = EngineEuler.Rotation(0f, 0f, 0f);
    }
}
