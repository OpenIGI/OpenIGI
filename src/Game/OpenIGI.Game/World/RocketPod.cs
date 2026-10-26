using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>A rocket fired from a <see cref="RocketPod"/>: where it leaves, how it faces, how fast and at what.</summary>
/// <param name="Origin">Where it leaves, world units: half a metre out along the pod's forward axis.</param>
/// <param name="Orientation">The pod's orientation, which the rocket is fired facing.</param>
/// <param name="SpeedMetresPerSecond">Its speed: the vehicle's own, in metres a second, and five more.</param>
/// <param name="Target">What it is fired at, world units.</param>
public readonly record struct RocketPodLaunch(Vector3d Origin, Matrix3f Orientation, float SpeedMetresPerSecond, Vector3d Target);

/// <summary>
/// A helicopter's rocket pod, <c>TASKTYPE_ROCKETPOD</c> in both games (Project I.G.I.'s <c>0x427A70</c>, I.G.I. 2's
/// <c>RocketPod_RunHandler</c>): when its gap has run out it fires one rocket at each enemy its vehicle sees ahead of it.
/// </summary>
/// <remarks>
/// <para>
/// It is made with its countdown drawn below its delay (<c>0x427940</c>), and made again whenever its vehicle's
/// parts are: so each draws one number from the game's stream when the level is made, and another whenever its
/// vehicle's flight starts over.
/// </para>
/// <para>
/// Each run, while the countdown is below nought, every enemy the vehicle's detector sees is tested: it must lie on
/// the pod's forward side, within 1.3 radians of its forward axis across, and in sight of the pod. Each that is gets a
/// rocket, and puts the countdown back to the delay and a draw below it. The countdown falls by one every run.
/// </para>
/// </remarks>
public sealed class RocketPod
{
    /// <summary>The widest heading off its forward axis it fires at, radians.</summary>
    public const double Cone = 1.3;

    /// <summary>How far ahead of the pod a rocket leaves, world units.</summary>
    public const float Reach = 2048f;

    /// <summary>What the vehicle's speed, world units a tick, is scaled by into metres a second: 30 / 4096.</summary>
    public const float SpeedScale = 0.00732421875f;

    /// <summary>The speed a rocket has over its vehicle's, metres a second.</summary>
    public const float SpeedOverVehicle = 5f;

    /// <summary>Initializes a new instance of the <see cref="RocketPod"/> class.</summary>
    /// <param name="offset">Where it is attached, in its vehicle's frame, world units.</param>
    /// <param name="orientation">How it is attached, in its vehicle's frame.</param>
    /// <param name="delayTicks">The least gap between its salvoes, ticks.</param>
    public RocketPod(Vector3d offset, Matrix3f orientation, int delayTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayTicks);
        Offset = offset;
        Orientation = orientation;
        DelayTicks = delayTicks;
    }

    /// <summary>Gets where it is attached, in its vehicle's frame, world units.</summary>
    public Vector3d Offset { get; }

    /// <summary>Gets how it is attached, in its vehicle's frame.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the least gap between its salvoes, ticks.</summary>
    public int DelayTicks { get; }

    /// <summary>Gets or sets its countdown: it fires while this is below nought.</summary>
    public int Countdown { get; set; }

    /// <summary>Makes it, drawing its first countdown below its delay.</summary>
    /// <param name="random">The game's stream.</param>
    public void Create(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Countdown = (int)random.NextBounded((uint)DelayTicks);
    }

    /// <summary>Where it is and how it faces, on its vehicle.</summary>
    /// <param name="vehiclePosition">The vehicle's position, world units.</param>
    /// <param name="vehicleOrientation">The vehicle's orientation.</param>
    /// <returns>Its pose.</returns>
    public (Vector3d Position, Matrix3f Orientation) Pose(Vector3d vehiclePosition, Matrix3f vehicleOrientation) =>
        (vehiclePosition + vehicleOrientation.Transposed().Transform(Offset), vehicleOrientation * Orientation);

    /// <summary>Runs it a tick, as its vehicle runs its parts.</summary>
    /// <param name="random">The game's stream, which a salvo draws its next gap from.</param>
    /// <param name="position">Where it is, world units, as its vehicle's last move left it.</param>
    /// <param name="orientation">How it faces.</param>
    /// <param name="vehicleSpeed">Its vehicle's speed, world units a tick.</param>
    /// <param name="enemies">Where each enemy its vehicle's detector sees is, world units.</param>
    /// <param name="inSight">Whether the pod sees a point from where it is.</param>
    /// <param name="fire">Receives each rocket it fires.</param>
    /// <returns>How many it fired.</returns>
    public int Run(
        IgiRandom random,
        Vector3d position,
        Matrix3f orientation,
        float vehicleSpeed,
        IEnumerable<Vector3d> enemies,
        Func<Vector3d, Vector3d, bool> inSight,
        Action<RocketPodLaunch> fire)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(inSight);
        ArgumentNullException.ThrowIfNull(fire);
        int fired = 0;
        if (Countdown < 0)
        {
            Vector3f forward = orientation.Transposed().Transform(Vector3f.UnitY);
            foreach (Vector3d enemy in enemies)
            {
                Vector3d local = orientation.Transform(enemy - position);
                if (local.Y < 0d || Math.Abs(VehicleGunTurret.Heading(local)) >= Cone || !inSight(position, enemy))
                {
                    continue;
                }

                Vector3d origin = new(
                    position.X + (forward.X * Reach),
                    position.Y + (float)(forward.Y * Reach),
                    position.Z + (float)(forward.Z * Reach));
                fire(new RocketPodLaunch(origin, orientation, (vehicleSpeed * SpeedScale) + SpeedOverVehicle, enemy));
                Countdown = (int)random.NextBounded((uint)DelayTicks) + DelayTicks;
                fired++;
            }
        }

        Countdown--;
        return fired;
    }
}

/// <summary>
/// A helicopter's sliding door, <c>TASKTYPE_HELIDOOR</c> (Project I.G.I.'s <c>0x4273B0</c>): it slides along an axis of
/// its vehicle's frame toward its open stop while told open, and back to nought while not, a step a run.
/// </summary>
public sealed class SlidingDoor
{
    /// <summary>Initializes a new instance of the <see cref="SlidingDoor"/> class.</summary>
    /// <param name="door">Its record.</param>
    public SlidingDoor(MagicHeliDoor door) => Door = door;

    /// <summary>Gets its record.</summary>
    public MagicHeliDoor Door { get; }

    /// <summary>Gets or sets whether it is told open (<c>0x426D80</c>).</summary>
    public bool IsOpen { get; set; }

    /// <summary>Gets or sets how far it has slid, world units.</summary>
    public float Slide { get; set; }

    /// <summary>Makes it, shut.</summary>
    public void Create()
    {
        IsOpen = false;
        Slide = 0f;
    }

    /// <summary>Slides it a step toward where it is told to be.</summary>
    public void Run()
    {
        float stop = IsOpen ? Door.OpenUnits : 0f;
        if (Slide < stop)
        {
            Slide = Math.Min(Slide + Door.SpeedUnitsPerTick, stop);
        }
        else if (Slide > stop)
        {
            Slide = Math.Max(Slide - Door.SpeedUnitsPerTick, stop);
        }
    }

    /// <summary>Gets how far it is slid along its vehicle's axes, world units.</summary>
    public Vector3d Displacement => Door.Axis switch
    {
        0 => new Vector3d(Slide, 0d, 0d),
        1 => new Vector3d(0d, Slide, 0d),
        2 => new Vector3d(0d, 0d, Slide),
        _ => Vector3d.Zero,
    };
}
