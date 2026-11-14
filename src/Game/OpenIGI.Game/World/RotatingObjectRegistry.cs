using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One rotating world placement (radar dish or generic rotating object).</summary>
public sealed class PlacedRotatingObject : Saves.IExchangedState
{
    private const float DeltaTime = 1.0f / 30.0f;

    /// <summary>The world placement associated with this rotating object.</summary>
    public WorldPlacement Placement { get; }

    /// <summary>The owner game task of the placement.</summary>
    public GameTask Owner => Placement.Owner;

    /// <summary>The world position of the placement.</summary>
    public Vector3d Position => Placement.Position;

    /// <summary>The base (rest) orientation of the placement.</summary>
    public Matrix3f BaseOrientation { get; }

    /// <summary>Target angular speed in degrees per second along the X axis.</summary>
    public float TargetSpeedX { get; set; }

    /// <summary>Target angular speed in degrees per second along the Y axis.</summary>
    public float TargetSpeedY { get; set; }

    /// <summary>Target angular speed in degrees per second along the Z axis (vertical in IGI).</summary>
    public float TargetSpeedZ { get; set; }

    /// <summary>Current angular speed in degrees per second along the X axis.</summary>
    public float CurrentSpeedX { get; set; }

    /// <summary>Current angular speed in degrees per second along the Y axis.</summary>
    public float CurrentSpeedY { get; set; }

    /// <summary>Current angular speed in degrees per second along the Z axis.</summary>
    public float CurrentSpeedZ { get; set; }

    /// <summary>Angular acceleration in degrees per second squared along the X axis.</summary>
    public float AccelX { get; }

    /// <summary>Angular acceleration in degrees per second squared along the Y axis.</summary>
    public float AccelY { get; }

    /// <summary>Angular acceleration in degrees per second squared along the Z axis.</summary>
    public float AccelZ { get; }

    /// <summary>Current rotation angle in degrees along the X axis in [0, 360).</summary>
    public float AngleX { get; private set; }

    /// <summary>Current rotation angle in degrees along the Y axis in [0, 360).</summary>
    public float AngleY { get; private set; }

    /// <summary>Current rotation angle in degrees along the Z axis in [0, 360).</summary>
    public float AngleZ { get; private set; }

    /// <summary>Previous tick rotation angle in degrees along the X axis.</summary>
    public float PreviousAngleX { get; private set; }

    /// <summary>Previous tick rotation angle in degrees along the Y axis.</summary>
    public float PreviousAngleY { get; private set; }

    /// <summary>Previous tick rotation angle in degrees along the Z axis.</summary>
    public float PreviousAngleZ { get; private set; }

    /// <summary>Condition expression string for activation, or empty if unconditionally active.</summary>
    public string OnExpression { get; }

    /// <summary>Whether this object is active/powered.</summary>
    public bool IsActive { get; set; }

    /// <summary>Authored rotate / loop sound name, if any.</summary>
    public string Sound { get; }

    /// <summary>Current live orientation matrix.</summary>
    public Matrix3f CurrentOrientation { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="PlacedRotatingObject"/> class.</summary>
    /// <param name="placement">The world placement to animate.</param>
    /// <param name="speedX">Target speed around the X axis in degrees per second.</param>
    /// <param name="speedY">Target speed around the Y axis in degrees per second.</param>
    /// <param name="speedZ">Target speed around the Z axis in degrees per second.</param>
    /// <param name="accelX">Acceleration around the X axis in degrees per second squared.</param>
    /// <param name="accelY">Acceleration around the Y axis in degrees per second squared.</param>
    /// <param name="accelZ">Acceleration around the Z axis in degrees per second squared.</param>
    /// <param name="onExpression">Condition expression controlling rotation.</param>
    /// <param name="isActive">Initial active state.</param>
    /// <param name="sound">Authored rotation sound name.</param>
    public PlacedRotatingObject(
        WorldPlacement placement,
        float speedX,
        float speedY,
        float speedZ,
        float accelX = 0f,
        float accelY = 0f,
        float accelZ = 0f,
        string onExpression = "",
        bool isActive = true,
        string sound = "")
    {
        ArgumentNullException.ThrowIfNull(placement);
        Placement = placement;
        BaseOrientation = placement.Orientation;

        TargetSpeedX = speedX;
        TargetSpeedY = speedY;
        TargetSpeedZ = speedZ;

        MaxSpeedX = speedX;
        MaxSpeedY = speedY;
        MaxSpeedZ = speedZ;

        CurrentSpeedX = speedX;
        CurrentSpeedY = speedY;
        CurrentSpeedZ = speedZ;

        AccelX = accelX;
        AccelY = accelY;
        AccelZ = accelZ;

        OnExpression = onExpression ?? string.Empty;
        IsActive = isActive;
        Sound = sound ?? string.Empty;

        CurrentOrientation = BaseOrientation;
    }

    /// <summary>Gets its authored top speed about the X axis, degrees a second.</summary>
    public float MaxSpeedX { get; }

    /// <summary>Gets its authored top speed about the Y axis.</summary>
    public float MaxSpeedY { get; }

    /// <summary>Gets its authored top speed about the Z axis.</summary>
    public float MaxSpeedZ { get; }

    /// <summary>
    /// Gets the one parameter its loop is played with: a dish's speed over its top speed (<c>0x448E30</c>), a
    /// <c>RotatingObject</c>'s three axes' summed (<c>0x439090</c>), held to nought to one -- an axis with no top speed makes the
    /// sum no number at all, which reads as nought.
    /// </summary>
    public float SoundLevel
    {
        get
        {
            if (IsRotatingObjectTask)
            {
                return Objects.RotationRule.SoundLevel(CurrentSpeedX, CurrentSpeedY, CurrentSpeedZ, MaxSpeedX, MaxSpeedY, MaxSpeedZ);
            }

            float level = CurrentSpeedZ / MaxSpeedZ;
            return level < 0f || float.IsNaN(level) ? 0f : Math.Min(level, 1f);
        }
    }

    /// <summary>Gets whether this is a level <c>RotatingObject</c> task, which updates by <c>0x439090</c>, rather than a radar dish.</summary>
    public bool IsRotatingObjectTask { get; init; }

    /// <summary>Gets a value indicating whether a round breaks this <c>RotatingObject</c>, its <c>Destructable</c> flag.</summary>
    public bool IsDestructable { get; init; }

    /// <summary>Gets a value indicating whether a round has broken it (<c>+424</c>), which switches it off for good.</summary>
    public bool IsBroken { get; private set; }

    // As the match began: its speeds, its angles, whether it ran and whether it was shot to a stop.
    private (float Tx, float Ty, float Tz, float Cx, float Cy, float Cz, float Ax, float Ay, float Az, bool Active, bool Broken) _start;

    /// <summary>Marks the state the level starts in, to be put back by <see cref="ResetToLevelStart"/>.</summary>
    public void MarkLevelStart() =>
        _start = (TargetSpeedX, TargetSpeedY, TargetSpeedZ, CurrentSpeedX, CurrentSpeedY, CurrentSpeedZ, AngleX, AngleY, AngleZ, IsActive, IsBroken);

    /// <summary>Puts back the state the level started in.</summary>
    public void ResetToLevelStart()
    {
        (TargetSpeedX, TargetSpeedY, TargetSpeedZ, CurrentSpeedX, CurrentSpeedY, CurrentSpeedZ) =
            (_start.Tx, _start.Ty, _start.Tz, _start.Cx, _start.Cy, _start.Cz);
        (AngleX, AngleY, AngleZ) = (_start.Ax, _start.Ay, _start.Az);
        (PreviousAngleX, PreviousAngleY, PreviousAngleZ) = (_start.Ax, _start.Ay, _start.Az);
        IsActive = _start.Active;
        IsBroken = _start.Broken;
        CurrentOrientation = ComputeOrientation(AngleX, AngleY, AngleZ);
    }

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        var levelStart = _start;
        MarkLevelStart();
        _start = (
            exchange.Real(_start.Tx), exchange.Real(_start.Ty), exchange.Real(_start.Tz),
            exchange.Real(_start.Cx), exchange.Real(_start.Cy), exchange.Real(_start.Cz),
            exchange.Real(_start.Ax), exchange.Real(_start.Ay), exchange.Real(_start.Az),
            exchange.Bool(_start.Active), exchange.Bool(_start.Broken));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

    /// <summary>A round strikes it: its <c>WeaponHit</c> handler.</summary>
    /// <remarks>
    /// A radar dish's (<c>0x449260</c>, I.G.I. 2's <c>RadarDish_WeaponHitHandler</c> the same) wants no more speed, so it runs
    /// down at its acceleration and stays still. A <c>RotatingObject</c>'s (<c>0x439750</c>) breaks it when it is
    /// <c>Destructable</c>, and its update (<c>0x439090</c>) reads a broken object as switched off.
    /// </remarks>
    public void WeaponHit()
    {
        if (!IsRotatingObjectTask)
        {
            TargetSpeedZ = 0f;
        }
        else if (IsDestructable)
        {
            IsBroken = true;
        }
    }

    /// <summary>Advances one 30 Hz tick as a <c>RotatingObject</c>'s update (<c>0x439090</c>), with its x87 arithmetic.</summary>
    /// <param name="switchedOff">Whether the object has an On expression and it is false this tick.</param>
    /// <remarks>
    /// <para>
    /// Switching off zeroes the target speeds themselves (<c>+316..+324</c>), and nothing writes them again: the post-load
    /// handler (<c>0x4395D0</c>) is the only other writer. An object switched off, even for one tick (Mission 8's fan
    /// is on while <c>!Switch_260.isPressed</c>, a three-tick pulse), runs down to rest and stays there.
    /// </para>
    /// <para>
    /// Each speed steps toward its target by its acceleration (<c>+300..+308</c>) over a thirtieth of a second. The clamp
    /// compares the register's unrounded value, so an axis with no acceleration never changes speed. Each angle
    /// (<c>+340..+348</c>) gains its speed over a thirtieth, and is wrapped only from above.
    /// </para>
    /// </remarks>
    public void TickRotatingObject(bool switchedOff)
    {
        PreviousAngleX = AngleX;
        PreviousAngleY = AngleY;
        PreviousAngleZ = AngleZ;

        if (switchedOff || IsBroken)
        {
            TargetSpeedX = 0f;
            TargetSpeedY = 0f;
            TargetSpeedZ = 0f;
        }

        IsActive = TargetSpeedX != 0f || TargetSpeedY != 0f || TargetSpeedZ != 0f;
        CurrentSpeedX = StepSpeed(CurrentSpeedX, TargetSpeedX, AccelX);
        CurrentSpeedY = StepSpeed(CurrentSpeedY, TargetSpeedY, AccelY);
        CurrentSpeedZ = StepSpeed(CurrentSpeedZ, TargetSpeedZ, AccelZ);
        AngleX = StepAngle(AngleX, CurrentSpeedX);
        AngleY = StepAngle(AngleY, CurrentSpeedY);
        AngleZ = StepAngle(AngleZ, CurrentSpeedZ);
        CurrentOrientation = ComputeOrientation(AngleX, AngleY, AngleZ);
    }

    /// <summary>Advances rotation state for one 30 Hz simulation tick.</summary>
    public void Tick(bool powered)
    {
        PreviousAngleX = AngleX;
        PreviousAngleY = AngleY;
        PreviousAngleZ = AngleZ;

        IsActive = powered;

        float targetX = powered ? TargetSpeedX : 0f;
        float targetY = powered ? TargetSpeedY : 0f;
        float targetZ = powered ? TargetSpeedZ : 0f;

        CurrentSpeedX = MoveTowards(CurrentSpeedX, targetX, AccelX > 0f ? AccelX * DeltaTime : float.MaxValue);
        CurrentSpeedY = MoveTowards(CurrentSpeedY, targetY, AccelY > 0f ? AccelY * DeltaTime : float.MaxValue);
        CurrentSpeedZ = MoveTowards(CurrentSpeedZ, targetZ, AccelZ > 0f ? AccelZ * DeltaTime : float.MaxValue);

        AngleX = WrapDegrees(AngleX + (CurrentSpeedX * DeltaTime));
        AngleY = WrapDegrees(AngleY + (CurrentSpeedY * DeltaTime));
        AngleZ = WrapDegrees(AngleZ + (CurrentSpeedZ * DeltaTime));

        CurrentOrientation = ComputeOrientation(AngleX, AngleY, AngleZ);
    }

    /// <summary>Computes the live orientation interpolated between previous and current ticks.</summary>
    public Matrix3f PresentedOrientation(float tickFraction, bool smooth)
    {
        if (!smooth)
        {
            return CurrentOrientation;
        }

        float t = Math.Clamp(tickFraction, 0f, 1f);
        float x = LerpDegrees(PreviousAngleX, AngleX, t);
        float y = LerpDegrees(PreviousAngleY, AngleY, t);
        float z = LerpDegrees(PreviousAngleZ, AngleZ, t);

        return ComputeOrientation(x, y, z);
    }

    private Matrix3f ComputeOrientation(float x, float y, float z) => Objects.RotationRule.Orientation(BaseOrientation, x, y, z);

    // The step both games run (Objects.RotationRule).
    private static float StepSpeed(float current, float target, float acceleration) =>
        Objects.RotationRule.StepSpeed(current, target, acceleration);

    private static float StepAngle(float angle, float speed) => Objects.RotationRule.StepAngle(angle, speed);

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target)
        {
            return MathF.Min(current + maxDelta, target);
        }
        if (current > target)
        {
            return MathF.Max(current - maxDelta, target);
        }
        return target;
    }

    private static float WrapDegrees(float degrees)
    {
        float wrapped = degrees % 360f;
        if (wrapped < 0f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    private static float LerpDegrees(float from, float to, float t)
    {
        float diff = (to - from) % 360f;
        if (diff > 180f)
        {
            diff -= 360f;
        }
        else if (diff < -180f)
        {
            diff += 360f;
        }
        return WrapDegrees(from + (diff * t));
    }
}
