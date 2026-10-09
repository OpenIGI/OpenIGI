using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's twelve security cameras (<c>SCamera_SCameraLensRunHandler</c>, <c>igi2 0x48EF20</c>): their sweep, their sight of
/// the player through <c>AIDetection</c>, the detection clock, and the lens shot out.
/// </summary>
public sealed class RetailIgi2SecurityCameraTests
{
    /// <summary>The main gate's camera, <c>SCamera_106</c>: it turns only right, 120°, at 15° a second.</summary>
    private const int MainGate = 106;

    /// <summary>A camera that sweeps 45° either way at 45° a second and holds two seconds, <c>SCamera_107</c>.</summary>
    private const int Sweeper = 107;

    /// <summary>All twelve are typed with their parameters; an angle is brought within a half turn either way.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_cameras_are_read()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SecurityCameraTask[] cameras = [.. m.Tree.OfType<Igi2SecurityCameraTask>()];
        Assert.Equal(12, cameras.Length);
        Igi2SecurityCameraTask gate = cameras.Single(static c => c.Id == MainGate);
        Assert.Equal(("335_01_1", "335_02_1", "335_03_1", "1"), (gate.HolderModel, gate.CameraModel, gate.CameraDestroyedModel, gate.OnScript));
        Assert.Equal(((short)0, (short)120, (short)15, 9f), (gate.RotateLeft, gate.RotateRight, gate.RotateSpeed, gate.GammaDelay));
        Assert.Equal(((short)180, (short)45, 18f, 0.1f), (gate.ViewconeAlpha, gate.ViewconeGamma, gate.ViewconeLength, gate.MaxDamageFactor));

        Assert.Equal(-1.5707964f, Igi2SecurityCameraTask.LimitAngle(4.712389f), 5);
        Assert.Equal(-3.1415927f, Igi2SecurityCameraTask.LimitAngle(3.1415927f), 5);
        Assert.Equal(0.5f, Igi2SecurityCameraTask.LimitAngle(0.5f));
    }

    /// <summary>
    /// With nothing to see, a camera sweeps to its right limit at its speed, holds there for its delay, and sweeps back to its
    /// left; and its lens's solid turns with it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_camera_sweeps_and_holds()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2SecurityCameraTask camera = Arm(m, null).Single(static c => c.Id == Sweeper);
        float right = ((Fpu)(double)45 * Igi2SecurityCameraTask.DegreesToRadians).F;
        int reached = -1;
        for (int tick = 0; tick < 120 && reached < 0; tick++)
        {
            m.Run.Tick();
            if (camera.LensGamma == right)
            {
                reached = tick;
            }
        }

        // 45° at 45° a second is thirty ticks of turning.
        Assert.InRange(reached, 29, 32);
        for (int tick = 0; tick < 60; tick++)
        {
            m.Run.Tick();
            Assert.Equal(right, camera.LensGamma);
        }

        m.Run.Tick();
        Assert.True(camera.LensGamma < right);

        Igi2RigidObject lens = m.Collision!.SolidsOf(Igi2SecurityCameraTask.TypeName, Sweeper).Single(static s => s.Owner is Igi2CollisionOwner { Depth: 1 });
        float turn = (((Fpu)camera.LensGamma + camera.HolderGamma) + camera.CameraGamma).F;
        Assert.Equal(Igi2Pose.Rotation(camera.LensAlpha, 0f, turn), lens.Orientation);
    }

    /// <summary>
    /// The player standing in a camera's view, in plain sight, becomes its target; the camera beeps while its clock runs, three
    /// seconds at normal difficulty, and then detects the player and sounds its detection beep.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_camera_detects_the_player_after_its_clock()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2SecurityCameraTask gate = m.Tree.OfType<Igi2SecurityCameraTask>().Single(static c => c.Id == MainGate);
        gate.Models = m.Level.ModelFor;
        gate.Prepare();
        float[] f = gate.View.Orientation;
        Vector3d at = gate.View.Position + (new Vector3d(f[1], f[4], 0.0) * 16000.0);
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, null, new Vector3d(at.X, at.Y, gate.View.Position.Z - 8000.0), 0f, m.Sounds);
        Arm(m, player.Human);

        int targeted = -1;
        for (int tick = 0; tick < 40 && targeted < 0; tick++)
        {
            m.Run.Tick();
            if (ReferenceEquals(gate.Target, player.Human))
            {
                targeted = tick;
            }
        }

        Assert.True(targeted >= 0);
        Assert.Equal(90 - 1, gate.DetectionTime);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "camera_beep_s" && s.IsAlive);
        for (int tick = 0; tick < 88; tick++)
        {
            m.Run.Tick();
            Assert.False(gate.IsDetection);
        }

        m.Run.Tick();
        Assert.True(gate.IsDetection);
        Assert.Equal(1.0, m.Run.Evaluate(new Igi2LevelExpression($"SCamera_{MainGate}.isDetection", "test", m.Run.Natives)));
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "camera_beep_l");
        Assert.True(gate.Detection!.Find(player.Human)!.IsFullVisible);
    }

    /// <summary>
    /// Bullets add to the lens's damage; once it reaches the camera's most, the lens explodes on its next tick: the camera is
    /// destroyed and off, its lens wears its destroyed model, and a blast goes off.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_shot_lens_destroys_its_camera()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2DynamicTasks dynamic = new();
        Igi2ExplosionWorld blasts = new(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic);
        Igi2SecurityCameraTask gate = Arm(m, null).Single(static c => c.Id == MainGate);
        gate.Explosions = blasts;
        Igi2RigidObject lens = m.Collision!.SolidsOf(Igi2SecurityCameraTask.TypeName, MainGate).Single(static s => s.Owner is Igi2CollisionOwner { Depth: 1 });
        Assert.Same(gate, lens.Handler);
        m.Run.Tick();
        Assert.True(gate.IsOn);

        gate.BulletDamage(0.05f);
        m.Run.Tick();
        Assert.False(gate.IsDestroyed);
        gate.BulletDamage(0.05f);
        m.Run.Tick();
        Assert.Equal((true, false), (gate.IsDestroyed, gate.IsOn));
        Assert.Equal(1.0, m.Run.Evaluate(new Igi2LevelExpression($"SCamera_{MainGate}.isExploded", "test", m.Run.Natives)));
        Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());
        Assert.Equal(m.Level.ModelFor("335_03_1")!.NativeMarkerBound, lens.Radius);

        gate.BulletDamage(1f);
        Assert.Equal(0.1f, gate.Damage, 5);
    }

    // The level's cameras readied as gameplay readies them, seeing one another and, where given, the player.
    private static Igi2SecurityCameraTask[] Arm(RetailIgi2Mission1 m, IIgi2Detectable? player)
    {
        Igi2DetectionWorld world = new(m.Collision, m.Materials)
        {
            FlowTicks = () => m.Run.Ticks,
            LevelTick = () => m.Run.LevelFlow!.Tick,
        };
        Igi2SecurityCameraTask[] cameras = [.. m.Tree.OfType<Igi2SecurityCameraTask>()];
        foreach (Igi2SecurityCameraTask camera in cameras)
        {
            camera.Models = m.Level.ModelFor;
            camera.Sounds = m.Sounds;
            camera.Prepare();
            camera.Bind(world, m.Collision!.SolidsOf(Igi2SecurityCameraTask.TypeName, camera.Id));
            world.Detectables.Add(camera);
        }

        if (player is not null)
        {
            world.Detectables.Add(player);
        }

        return cameras;
    }
}
