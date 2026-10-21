using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>One rotating world placement (radar dish or generic rotating object).</summary>
public sealed class PlacedRotatingObject
{
    private const float DegreesToRadians = MathF.PI / 180f;
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

    /// <summary>Gets whether this is a level <c>RotatingObject</c> task, which updates by <c>0x439090</c>, rather than a radar dish.</summary>
    public bool IsRotatingObjectTask { get; init; }

    /// <summary>Gets a value indicating whether a round breaks this <c>RotatingObject</c>, its <c>Destructable</c> flag.</summary>
    public bool IsDestructable { get; init; }

    /// <summary>Gets a value indicating whether a round has broken it (<c>+424</c>), which switches it off for good.</summary>
    public bool IsBroken { get; private set; }

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

    private Matrix3f ComputeOrientation(float x, float y, float z)
    {
        Matrix3f rot = (x == 0f && y == 0f)
            ? Matrix3f.RotationZ(z * DegreesToRadians)
            : Matrix3f.FromEngineEulerAngles(x * DegreesToRadians, y * DegreesToRadians, z * DegreesToRadians);

        return BaseOrientation * rot;
    }

    // One axis's speed toward its target (0x4390DF and its two siblings).
    private static float StepSpeed(float current, float target, float acceleration)
    {
        if (current == target)
        {
            return current;
        }

        if (current < target)
        {
            X87 next = ((X87)acceleration * DeltaTime) + current;
            return next.Value > target ? target : next.F;
        }

        X87 down = (X87)current - ((X87)acceleration * DeltaTime);
        return down.Value < target ? target : down.F;
    }

    // One axis's angle (0x4392D4): the sum is stored, then brought under 360 by whole turns.
    private static float StepAngle(float angle, float speed)
    {
        float next = (((X87)speed * DeltaTime) + angle).F;
        if (next >= 360f)
        {
            X87 wrapped;
            do
            {
                wrapped = (X87)next - 360f;
                next = wrapped.F;
            }
            while (wrapped.Value >= 360.0);
        }

        return next;
    }

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

/// <summary>
/// Collects and advances continuously rotating world objects (radar dishes and RotatingObject tasks).
/// </summary>
/// <remarks>
/// <para>
/// Retail Project IGI implements continuous object rotation in two engine classes:
/// <list type="bullet">
/// <item><description><c>TASKTYPE_RADARDISH</c> (<c>0x448d30</c>): class for radar towers and radar dish attachments
/// (e.g. model <c>436_02_1</c> on radar tower <c>436_01_1</c> in Level 5). Rotates around the vertical Z axis
/// at authored speed and acceleration defined in <c>magicobj.qvm</c>.</description></item>
/// <item><description><c>RotatingObject</c> (<c>0x438df0</c>): declared level task class supporting 3-axis rotation,
/// acceleration, and on-expression activation (e.g. Level 3, Level 5, Level 8).</description></item>
/// </list>
/// Both integrate rotation at the 30 Hz simulation tick and compose their live orientation with their base transform.
/// </para>
/// </remarks>
public sealed class RotatingObjectRegistry
{
    private const string RadarDishTaskType = "TASKTYPE_RADARDISH";
    private const string RotatingObjectTaskType = "RotatingObject";

    private static readonly Dictionary<string, (float Speed, float Accel)> KnownRadarDishes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["436_02_1"] = (90f, 15f),
        ["241_02_1"] = (35f, 15f),
        ["433_03_1"] = (320f, 15f),
        ["451_03_1"] = (520f, 12f),
        ["243_02_1"] = (999f, 900f),
    };

    private readonly List<PlacedRotatingObject> _objects = [];
    private readonly HashSet<PlacedRotatingObject> _walked = [];
    private readonly Dictionary<WorldPlacement, PlacedRotatingObject> _byPlacement = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(GameTask Owner, string Model), PlacedRotatingObject> _byOwnerAndModel = [];
    private readonly Dictionary<GameTask, PlacedRotatingObject> _byTask = [];
    private readonly LevelExpressionRuntime? _expressions;

    /// <summary>Initializes a new instance of the <see cref="RotatingObjectRegistry"/> class.</summary>
    /// <param name="expressions">Optional expression runtime for evaluating on-expressions.</param>
    public RotatingObjectRegistry(LevelExpressionRuntime? expressions = null)
    {
        _expressions = expressions;
    }

    /// <summary>Gets the number of registered rotating objects.</summary>
    public int Count => _objects.Count;

    /// <summary>Gets all registered rotating objects.</summary>
    public IReadOnlyList<PlacedRotatingObject> Objects => _objects;

    /// <summary>Checks whether a model instance name is a known retail radar dish model.</summary>
    public static bool IsKnownRadarDishModel(string modelName) =>
        !string.IsNullOrEmpty(modelName) && KnownRadarDishes.ContainsKey(modelName);

    /// <summary>Builds the rotating object registry from a loaded level and placements.</summary>
    public static RotatingObjectRegistry Build(
        GameTask root,
        IEnumerable<WorldPlacement> placements,
        ScriptVarRegistry? variables = null,
        MagicObjectRegistry? magic = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(placements);

        LevelExpressionRuntime? expressions = variables is not null ? new LevelExpressionRuntime(variables) : null;
        RotatingObjectRegistry registry = new(expressions);

        foreach (WorldPlacement placement in placements)
        {
            // A task-authored RotatingObject owns its behavior even when its model also has a
            // magic-object definition (Level 5's standalone 436_02_1 is exactly that case).
            if (!placement.IsAttached
                && string.Equals(placement.Owner.Type.Name, RotatingObjectTaskType, StringComparison.Ordinal)
                && placement.Owner is DeclaredTask declared)
            {
                float speedX = RealFromNumberOrShort(declared, "Degrees per second X-axis");
                float speedY = RealFromNumberOrShort(declared, "Degrees per second Y-axis");
                float speedZ = RealFromNumberOrShort(declared, "Degrees per second Z-axis");

                float accelX = RealFromNumberOrShort(declared, "Acceleration, degrees per second X-axis");
                float accelY = RealFromNumberOrShort(declared, "Acceleration, degrees per second Y-axis");
                float accelZ = RealFromNumberOrShort(declared, "Acceleration, degrees per second Z-axis");

                string onExpr = TextTrimmed(declared, "On expression");
                string sound = TextTrimmed(declared, "Rotate sound");

                registry.Add(new PlacedRotatingObject(
                    placement,
                    speedX,
                    speedY,
                    speedZ,
                    accelX,
                    accelY,
                    accelZ,
                    onExpr,
                    isActive: true,
                    sound: sound)
                {
                    IsRotatingObjectTask = true,
                    IsDestructable = declared.TryGetValue("Destructable", out object? destructable) && destructable is true,
                });
                continue;
            }

            // TASKTYPE_RADARDISH (or known radar dish model attachment)
            bool isRadarDish = string.Equals(placement.MagicTaskTypeName, RadarDishTaskType, StringComparison.Ordinal);
            if (!isRadarDish && magic is not null && magic.TryGet(placement.Instance.Name, out MagicObjectDefinition childDef))
            {
                isRadarDish = string.Equals(magic.TaskTypeName(childDef.TaskType), RadarDishTaskType, StringComparison.Ordinal);
            }
            if (!isRadarDish && IsKnownRadarDishModel(placement.Instance.Name))
            {
                isRadarDish = true;
            }

            if (isRadarDish)
            {
                float speed = 90f;
                float accel = 15f;
                string sound = "radardishsounds";

                if (magic is not null && magic.TryGet(placement.Instance.Name, out MagicObjectDefinition def))
                {
                    if (def.Speed > 0f)
                    {
                        speed = def.Speed;
                    }
                    if (def.Acceleration > 0f)
                    {
                        accel = def.Acceleration;
                    }
                    if (!string.IsNullOrEmpty(def.Sound))
                    {
                        sound = def.Sound;
                    }
                }
                else if (KnownRadarDishes.TryGetValue(placement.Instance.Name, out (float Speed, float Accel) fallback))
                {
                    speed = fallback.Speed;
                    accel = fallback.Accel;
                }

                PlacedRotatingObject obj = new(
                    placement,
                    speedX: 0f,
                    speedY: 0f,
                    speedZ: speed,
                    accelX: 0f,
                    accelY: 0f,
                    accelZ: accel,
                    onExpression: string.Empty,
                    isActive: true,
                    sound: sound);

                registry.Add(obj);
            }
        }

        return registry;
    }

    /// <summary>Adds a rotating object to the registry.</summary>
    public void Add(PlacedRotatingObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        _objects.Add(obj);
        _byPlacement[obj.Placement] = obj;
        _byOwnerAndModel[(obj.Owner, obj.Placement.Instance.Name)] = obj;
        _byTask.TryAdd(obj.Owner, obj);
    }

    /// <summary>Hands a round's <c>WeaponHit</c> to the rotating object it struck, if it struck one.</summary>
    /// <param name="owner">The task the round struck.</param>
    /// <param name="placement">The placement it struck: a radar dish is an attachment of its tower's task.</param>
    public void WeaponHit(GameTask? owner, WorldPlacement? placement)
    {
        if (placement is not null && _byPlacement.TryGetValue(placement, out PlacedRotatingObject? struck))
        {
            struck.WeaponHit();
        }
        else if (owner is not null && _byTask.TryGetValue(owner, out PlacedRotatingObject? task) && task.IsRotatingObjectTask)
        {
            task.WeaponHit();
        }
    }

    /// <summary>Puts every level <c>RotatingObject</c> into the level walk, updating where the walk reaches it.</summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="ticked">Called after each object's update, to carry its new orientation.</param>
    /// <remarks>
    /// Its update (<c>0x439090</c>) reads its On expression as the walk finds it and runs no children. Radar dishes are not
    /// level tasks of their own, and keep updating after the walk.
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker, Action<PlacedRotatingObject>? ticked = null)
    {
        ArgumentNullException.ThrowIfNull(walker);
        foreach (PlacedRotatingObject obj in _objects)
        {
            if (!obj.IsRotatingObjectTask)
            {
                continue;
            }

            _walked.Add(obj);
            walker.Bind(obj.Owner, _ =>
            {
                StepRotatingObject(obj);
                ticked?.Invoke(obj);
            });
        }
    }

    /// <summary>Advances every rotating object the level walk does not reach, for one 30 Hz simulation tick.</summary>
    public void Tick()
    {
        foreach (PlacedRotatingObject obj in _objects)
        {
            if (_walked.Contains(obj))
            {
                continue;
            }

            if (obj.IsRotatingObjectTask)
            {
                StepRotatingObject(obj);
                continue;
            }

            bool powered = true;
            if (!string.IsNullOrEmpty(obj.OnExpression) && _expressions is not null)
            {
                powered = _expressions.EvaluateCondition(obj.OnExpression, emptyValue: true, obj.Owner);
            }
            else
            {
                powered = obj.IsActive;
            }

            obj.Tick(powered);
        }
    }

    /// <summary>Resolves the current draw transform for a rendered placement.</summary>
    public bool TryGetDrawTransform(
        WorldPlacement placement,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (_byPlacement.TryGetValue(placement, out PlacedRotatingObject? obj)
            || _byOwnerAndModel.TryGetValue((placement.Owner, placement.Instance.Name), out obj))
        {
            position = obj.Position;
            orientation = obj.PresentedOrientation(tickFraction, smooth);
            return true;
        }

        position = default;
        orientation = default;
        return false;
    }

    /// <summary>Resolves the current draw transform for a task that owns a rotating object.</summary>
    public bool TryGetDrawTransform(
        GameTask task,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_byTask.TryGetValue(task, out PlacedRotatingObject? obj))
        {
            position = obj.Position;
            orientation = obj.PresentedOrientation(tickFraction, smooth);
            return true;
        }

        position = default;
        orientation = default;
        return false;
    }

    // 0x439090: switched off when it has an On expression (sub_4F16C0) and that is false.
    private void StepRotatingObject(PlacedRotatingObject obj)
    {
        bool switchedOff = _expressions is not null
            && !string.IsNullOrWhiteSpace(obj.OnExpression)
            && !_expressions.EvaluateCondition(obj.OnExpression, emptyValue: true, obj.Owner);
        obj.TickRotatingObject(switchedOff);
    }

    /// <summary>Updates one rotating object's collision solid, if its orientation changed.</summary>
    /// <param name="solids">The level's collision index.</param>
    /// <param name="obj">The object.</param>
    /// <returns>Whether its solid moved.</returns>
    public static bool ApplyCollision(ObjectCollisionIndex solids, PlacedRotatingObject obj)
    {
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(obj);
        return solids.UpdateTransform(obj.Placement, obj.Position, obj.CurrentOrientation);
    }

    /// <summary>Updates collision solids for all rotating objects whose orientation changed.</summary>
    public int ApplyCollision(ObjectCollisionIndex solids)
    {
        ArgumentNullException.ThrowIfNull(solids);
        int updated = 0;

        foreach (PlacedRotatingObject obj in _objects)
        {
            if (solids.UpdateTransform(obj.Placement, obj.Position, obj.CurrentOrientation))
            {
                updated++;
            }
        }

        return updated;
    }
}
