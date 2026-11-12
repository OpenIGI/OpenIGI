using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

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
public sealed class RotatingObjectRegistry : ILevelReset, Saves.ISavedStateSource
{
    private const string RadarDishTaskType = "TASKTYPE_RADARDISH";
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
    private Action<PlacedRotatingObject>? _ticked;
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

    /// <summary>Every turning object's loop, as it is made (<c>0x448B70</c>, <c>0x439620</c>), with its speed as its parameter.</summary>
    /// <returns>One per object that names a sound and stands in the world.</returns>
    public IEnumerable<VehicleSoundSource> SoundSources()
    {
        foreach (PlacedRotatingObject turning in _objects)
        {
            if (turning.Sound.Length == 0 || !ConditionalContainerGate.IsTaskActive(turning.Placement.Owner))
            {
                continue;
            }

            yield return new VehicleSoundSource(
                turning.Placement.Owner,
                turning.Sound,
                turning.Placement.Position * WorldScene.UnitsPerMetre,
                turning.SoundLevel,
                0f,
                0f,
                SoundOn: true,
                Key: turning);
        }
    }

    /// <summary>Checks whether a model instance name is a known retail radar dish model.</summary>
    public static bool IsKnownRadarDishModel(string modelName) =>
        !string.IsNullOrEmpty(modelName) && KnownRadarDishes.ContainsKey(modelName);

    /// <summary>Builds the rotating object registry from a loaded level and placements.</summary>
    public static RotatingObjectRegistry Build(
        GameTask root,
        IEnumerable<WorldPlacement> placements,
        ScriptVarRegistry? variables = null,
        MagicObjectRegistry? magic = null,
        LevelExpressionRuntime? expressions = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(placements);

        expressions ??= variables is not null ? new LevelExpressionRuntime(variables) : null;
        RotatingObjectRegistry registry = new(expressions);

        foreach (WorldPlacement placement in placements)
        {
            // A task-authored RotatingObject owns its behavior even when its model also has a
            // magic-object definition (Level 5's standalone 436_02_1 is exactly that case).
            if (!placement.IsAttached && placement.Owner is RotatingObjectTask declared)
            {
                registry.Add(new PlacedRotatingObject(
                    placement,
                    declared.DegreesPerSecondX,
                    declared.DegreesPerSecondY,
                    declared.DegreesPerSecondZ,
                    declared.AccelerationX,
                    declared.AccelerationY,
                    declared.AccelerationZ,
                    declared.OnExpression,
                    isActive: true,
                    sound: declared.RotateSound)
                {
                    IsRotatingObjectTask = true,
                    IsDestructable = declared.IsDestructable,
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
        obj.Owner.PlacementsChanged();
    }

    /// <summary>Whether a placement, or its owner, turns (see <see cref="OpenIGI.Engine.Tasks.TaskTree.PlacementEpoch"/>).</summary>
    public bool Claims(WorldPlacement placement) =>
        _byPlacement.ContainsKey(placement) || _byTask.ContainsKey(placement.Owner);

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

            if (Level.LevelTaskClass.TryAs(obj.Owner, walker, out RotatingObjectTask? task))
            {
                _walked.Add(obj);
                task.Rotators = this;
                task.Rotator = obj;
            }
        }

        _ticked = ticked;
    }

    /// <summary>A rotating object's own update, where the level's walk reaches its task (<c>0x439090</c>).</summary>
    /// <param name="obj">The object.</param>
    internal void RunFromWalk(PlacedRotatingObject obj)
    {
        StepRotatingObject(obj);
        _ticked?.Invoke(obj);
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

        // Every placement in the level asks this every frame and nearly all of them own nothing that
        // turns, so the owner is tested first: the owner-and-model key hashes a string, and on the web
        // its tuple lookup ran in the interpreter. Every owner registered is in _byTask.
        if (_byPlacement.TryGetValue(placement, out PlacedRotatingObject? obj)
            || (_byTask.ContainsKey(placement.Owner)
                && _byOwnerAndModel.TryGetValue((placement.Owner, placement.Instance.Name), out obj)))
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

    /// <inheritdoc/>
    public void MarkLevelStart()
    {
        foreach (PlacedRotatingObject obj in _objects)
        {
            obj.MarkLevelStart();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Each fan and dish turning, or stopped, as the match began; its solid turned back with it.</remarks>
    public void ResetToLevelStart(ObjectCollisionIndex? solids)
    {
        foreach (PlacedRotatingObject obj in _objects)
        {
            obj.ResetToLevelStart();
        }

        if (solids is not null)
        {
            ApplyCollision(solids);
        }
    }

    /// <inheritdoc/>
    /// <remarks>An object read back turns its collision with it at once, as a level reset does.</remarks>
    public IEnumerable<KeyValuePair<GameTask, Saves.ISavedState>> SavedStates(ObjectCollisionIndex? solids) =>
        _byTask.Select(pair => new KeyValuePair<GameTask, Saves.ISavedState>(
            pair.Key,
            solids is null ? pair.Value : new Saves.SavedStateThen(pair.Value, () => ApplyCollision(solids, pair.Value))));

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
