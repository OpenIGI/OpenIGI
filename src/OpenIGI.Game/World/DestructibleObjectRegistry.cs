using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>The authored destructible object families present in retail IGI1.</summary>
public enum DestructibleObjectKind
{
    /// <summary>A glass magic-object attachment.</summary>
    Glass,

    /// <summary>An authored ExplodeObject task or explosive magic-object attachment.</summary>
    Explosive,

    /// <summary>A physics-object vehicle whose CAR definition authors a wreck model.</summary>
    Vehicle,
}

/// <summary>One exact destructible object, including an attached magic-object placement when present.</summary>
public sealed class DestructibleObject
{
    internal DestructibleObject(
        DestructibleObjectKind kind,
        GameTask owner,
        WorldPlacement? placement,
        string typeName,
        Vector3d positionUnits)
    {
        Kind = kind;
        Owner = owner;
        Placement = placement;
        TypeName = typeName;
        PositionUnits = positionUnits;
        // ExplodeMagicObj_ExplosionHitHandler (0x43c2d0) passes the target's native model bound to
        // the shared contact-dose routine (0x416c90).  FUN_004d0950 reads that value from MESH+0x78;
        // keep the raw engine-unit value on the identity rather than substituting a generic radius.
        // A loaded world placement always has geometry, but preserve the default contact radius for
        // model-less synthetic/modded task declarations just as we do for a model-less task.
        ExplosionContactRadiusUnits = placement?.Model?.NativeMarkerBound ?? 1f;
    }

    /// <summary>Gets whether this is a glass pane or an explosive object.</summary>
    public DestructibleObjectKind Kind { get; }

    /// <summary>Gets the exact task that owns the object.</summary>
    public GameTask Owner { get; }

    /// <summary>Gets the exact rendered placement, or <see langword="null"/> for a model-less task.</summary>
    public WorldPlacement? Placement { get; }

    /// <summary>Gets the native task or magic-object type name.</summary>
    public string TypeName { get; }

    /// <summary>Gets the object's world position in engine units.</summary>
    public Vector3d PositionUnits { get; internal set; }

    /// <summary>
    /// Gets the target's native contact half-extent used by <c>ExplodeMagicObj_ExplosionHitHandler</c>.
    /// </summary>
    /// <remarks>
    /// Retail reads <c>MESH+0x78</c> through <c>FUN_004d0950</c> and subtracts it in
    /// <c>FUN_00416c90</c> before evaluating blast falloff.  The value is already in raw IGI engine
    /// units, matching <see cref="PositionUnits"/>.
    /// </remarks>
    public float ExplosionContactRadiusUnits { get; }

    /// <summary>Gets the authored replacement model name, when the task provides one.</summary>
    public string DestroyedModelName { get; internal set; } = string.Empty;

    /// <summary>Gets the authored explosion radius in engine units.</summary>
    public float ExplosionRadiusUnits { get; internal set; }

    /// <summary>Gets the authored explosion falloff radius in engine units.</summary>
    public float ExplosionFalloffUnits { get; internal set; }

    /// <summary>Gets the authored explosion damage factor.</summary>
    public float ExplosionDamageFactor { get; internal set; }

    /// <summary>Gets the accumulated damage required before this explosive object detonates.</summary>
    /// <remarks>
    /// Retail <c>ExplodeObject</c> compares <c>+0x1e8</c> with <c>+0x1ec</c>; attached
    /// <c>ExplodeMagicObj</c> uses the equivalent <c>+0x13c</c>/<c>+0x140</c> pair.
    /// </remarks>
    public float ExplosionDamageThreshold { get; internal set; } = 1f;

    /// <summary>Gets the damage accumulated by this object since it was created.</summary>
    public float ExplosionDamageAccumulated { get; internal set; }

    /// <summary>Gets the authored explosion sound definition.</summary>
    public string ExplosionSound { get; internal set; } = string.Empty;

    /// <summary>Gets the authored fireball count.</summary>
    /// <remarks>Retail stores this as a floating-point fireball scale, not an integer count.</remarks>
    public float ExplosionFireballs { get; internal set; }

    /// <summary>Gets the authored fragment count for the retail <c>Fragment</c> effect.</summary>
    public int ExplosionFragments { get; internal set; }

    /// <summary>Gets whether the retail explosion owns an <c>ExplosionSmoke</c> effect.</summary>
    /// <remarks>
    /// This is true only for an <c>ExplodeObject</c> authored by the level's <c>OBJECTS.QVM</c>.
    /// Attached magic-object families keep their own authored FireBall/Fragment recipe, but do not
    /// inherit an <c>ExplosionSmoke</c> child merely because they are explosive.
    /// </remarks>
    public bool HasExplosionSmoke { get; internal set; }

    /// <summary>Gets the authored explosion expression, when one gates the blast.</summary>
    public string ExplosionExpression { get; internal set; } = string.Empty;

    /// <summary>Gets the authored delay before the blast, in simulation ticks.</summary>
    public int ExplosionDelayTicks { get; internal set; }

    /// <summary>Gets whether the retail object has already been destroyed.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>Gets the previous tick's destroyed state exposed as <c>isLastDestroyed</c>.</summary>
    public bool IsLastDestroyed { get; internal set; }

    internal int PendingExplosionTicks { get; set; }

    internal bool ExplosionStarted { get; set; }

    internal bool ExplosionReady { get; set; }
}

/// <summary>
/// Tracks the retail <c>ExplodeObject</c>, glass, and explosive magic-object state shared by rendering,
/// collision, script variables, and impact handling.
/// </summary>
public sealed class DestructibleObjectRegistry
{
    /// <summary>The default contact radius used when a legacy impact has no exact placement identity.</summary>
    public const float HitRadiusUnits = 3276.8f;

    private const string DestroyedModelParameter = "Destroyed model";
    private const string ExplosionDamageScaleParameter = "Damage scale";
    private const string GlassType = "TASKTYPE_GLASS";
    private const string ExplodeMagicType = "TASKTYPE_EXPLODEMAGICOBJ";

    private readonly List<DestructibleObject> _objects = [];
    private readonly Dictionary<GameTask, List<DestructibleObject>> _byOwner =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, DestructibleObject> _byPlacement =
        new(ReferenceEqualityComparer.Instance);
    private readonly LevelExpressionRuntime _expressions;
    private readonly List<DestructibleObject> _readyExplosions = [];

    private DestructibleObjectRegistry(LevelExpressionRuntime expressions) => _expressions = expressions;

    /// <summary>Gets all destructible objects found in the current level.</summary>
    public IReadOnlyList<DestructibleObject> Objects => _objects;

    /// <summary>Builds destructible state from declared tasks and expanded magic-object placements.</summary>
    public static DestructibleObjectRegistry Build(
        GameTask root,
        IEnumerable<WorldPlacement> placements,
        ScriptVarRegistry variables,
        VehiclePhysicsRegistry? vehiclePhysics = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(variables);

        DestructibleObjectRegistry registry = new(new LevelExpressionRuntime(variables));
        WorldPlacement[] resolved = [.. placements];

        foreach (DeclaredTask task in Walk(root).OfType<DeclaredTask>())
        {
            // OBJECTS.QVM uses the same live destruction contract for ExplodeObject, Terminal,
            // AlarmControl, and SCameraControl: the first model is replaced by Destroyed model once
            // a weapon hit destroys the task.  A Switch also has a field of that name, but has no
            // explosion parameters and is governed by its separate destructable flag.
            if (!task.TryGetValue(DestroyedModelParameter, out object? rawDestroyed)
                || rawDestroyed is not string { Length: > 0 }
                || !task.TryGetValue(ExplosionDamageScaleParameter, out _))
            {
                continue;
            }

            WorldPlacement[] taskPlacements = [.. resolved.Where(candidate =>
                ReferenceEquals(candidate.Owner, task))];
            WorldPlacement? placement = taskPlacements.FirstOrDefault();
            DestructibleObject item = registry.Add(
                new DestructibleObject(
                    DestructibleObjectKind.Explosive,
                    task,
                    placement,
                    task.Type.Name,
                    PositionOf(task, placement)));
            foreach (WorldPlacement taskPlacement in taskPlacements)
            {
                registry.RegisterPlacement(item, taskPlacement);
            }
            ReadExplodeParameters(task, item);

            variables.BindBool(task, "isExploded", () => item.IsDestroyed, value =>
            {
                if (value)
                {
                    item.IsDestroyed = true;
                }
            });
            variables.BindBool(task, "isLastExploded", () => item.IsLastDestroyed, _ => { });
        }

        // Vehicle damage is not an ExplodeObject task.  The CAR physics definition owns the
        // replacement model (e.g. the retail Level 6 APC's 601_08_1), while the vehicle's native
        // explosion callback exports the same isExploded/isLastExploded state that level gates use.
        // Register only models that actually publish a wreck binding; ordinary cars remain intact.
        if (vehiclePhysics is not null)
        {
            foreach (DeclaredTask task in Walk(root).OfType<DeclaredTask>())
            {
                if (!string.Equals(task.Type.Name, "Car", StringComparison.Ordinal)
                    || !task.TryGetValue(WorldScene.ModelParameter, out object? rawModel)
                    || rawModel is not string model
                    || !vehiclePhysics.TryGetCar(model, out CarPhysicsDefinition car)
                    || car.DestroyedModel.Length == 0)
                {
                    continue;
                }

                WorldPlacement[] taskPlacements = [.. resolved.Where(candidate =>
                    ReferenceEquals(candidate.Owner, task))];
                WorldPlacement? placement = taskPlacements.FirstOrDefault();
                DestructibleObject item = registry.Add(new DestructibleObject(
                    DestructibleObjectKind.Vehicle,
                    task,
                    placement,
                    "Car",
                    PositionOf(task, placement))
                {
                    DestroyedModelName = car.DestroyedModel,
                });
                foreach (WorldPlacement taskPlacement in taskPlacements)
                {
                    registry.RegisterPlacement(item, taskPlacement);
                }

                variables.BindBool(task, "isExploded", () => item.IsDestroyed, value =>
                {
                    if (value)
                    {
                        item.IsDestroyed = true;
                    }
                });
                variables.BindBool(task, "isLastExploded", () => item.IsLastDestroyed, _ => { });
            }
        }

        foreach (WorldPlacement placement in resolved)
        {
            if (placement.MagicTaskTypeName.Length == 0
                || !TryMagicKind(placement.MagicTaskTypeName, out DestructibleObjectKind kind))
            {
                continue;
            }

            DestructibleObject item = registry.Add(
                new DestructibleObject(
                    kind,
                    placement.Owner,
                    placement,
                    placement.MagicTaskTypeName,
                    placement.Position * WorldScene.UnitsPerMetre));

            if (kind == DestructibleObjectKind.Explosive)
            {
                item.ExplosionRadiusUnits = placement.MagicExplosionRadiusMetres > 0f
                    ? ExplosionDamageDefaults.ToUnits(placement.MagicExplosionRadiusMetres)
                    : ExplosionDamageDefaults.RadiusUnits;
                item.ExplosionFalloffUnits = ExplosionDamageDefaults.ToUnits(placement.MagicExplosionFalloffMetres);
                item.ExplosionDamageFactor = placement.MagicExplosionDamageFactor > 0f
                    ? placement.MagicExplosionDamageFactor
                    : ExplosionDamageDefaults.DamageFactor;
                item.ExplosionDamageThreshold = placement.MagicExplosionDamageThreshold > 0f
                    ? placement.MagicExplosionDamageThreshold
                    : ExplosionDamageDefaults.DamageThreshold;
                item.ExplosionDelayTicks = Math.Max(0, placement.MagicExplosionDelayTicks);
                item.ExplosionFireballs = placement.MagicExplosionFireballs;
                item.ExplosionFragments = placement.MagicExplosionFragments;
                item.ExplosionSound = placement.MagicExplosionSound;
                item.DestroyedModelName = placement.MagicDestroyedModel;
            }
        }

        return registry;
    }

    /// <summary>Gets whether one exact placement is already destroyed.</summary>
    public bool IsDestroyed(WorldPlacement placement) =>
        placement is not null
        && _byPlacement.TryGetValue(placement, out DestructibleObject? item)
        && item.IsDestroyed;

    /// <summary>Gets whether a task has any destroyed object registered under it.</summary>
    public bool IsDestroyed(GameTask task) =>
        task is not null
        && _byOwner.TryGetValue(task, out List<DestructibleObject>? items)
        && items.Any(item => item.IsDestroyed);

    /// <summary>Moves every destructible identity owned by a live task to its current world position.</summary>
    /// <param name="owner">The exact moving task.</param>
    /// <param name="positionUnits">Its current origin in engine units.</param>
    /// <returns><see langword="true"/> when the task owns at least one destructible identity.</returns>
    /// <remarks>
    /// Vehicle destruction is dispatched after the missile has collided with the live world mesh.
    /// The blast envelope must therefore use the same moving origin instead of the authored spawn;
    /// otherwise an APC that has followed its alarm route can be visibly struck while its explosion
    /// callback remains kilometres of engine units behind it.
    /// </remarks>
    public bool UpdatePosition(GameTask owner, Vector3d positionUnits)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_byOwner.TryGetValue(owner, out List<DestructibleObject>? items))
        {
            return false;
        }

        foreach (DestructibleObject item in items)
        {
            item.PositionUnits = positionUnits;
        }

        return true;
    }

    /// <summary>Advances the script-visible previous-state flags by one simulation tick.</summary>
    public void Tick()
    {
        foreach (DestructibleObject item in _objects)
        {
            item.IsLastDestroyed = item.IsDestroyed;
            if (!item.IsDestroyed
                && item.ExplosionExpression.Length > 0
                && ExplosionEnabled(item))
            {
                // ExplodeObject::Update (IGI1 0x481970) evaluates the authored expression on every
                // tick and enters the explosion path immediately when it becomes true. This is the
                // Level 6 C4 chain: LevelTimer_1604 reaches tick 600, then tanks 1600/1601 explode.
                item.IsDestroyed = true;
                _readyExplosions.Add(item);
                continue;
            }

            if (item.PendingExplosionTicks > 0 && --item.PendingExplosionTicks == 0)
            {
                _readyExplosions.Add(item);
            }
        }
    }

    /// <summary>Returns delayed blasts whose authored fuse expired on this tick.</summary>
    public IReadOnlyList<DestructibleObject> ConsumeReadyExplosions()
    {
        if (_readyExplosions.Count == 0)
        {
            return [];
        }

        DestructibleObject[] ready = [.. _readyExplosions];
        _readyExplosions.Clear();
        foreach (DestructibleObject item in ready)
        {
            item.ExplosionReady = true;
        }

        return ready;
    }

    /// <summary>Applies one weapon hit to the exact placement or owner.</summary>
    public DestructibleObject? TryDestroy(
        GameTask? owner,
        WorldPlacement? placement = null,
        float damage = 1f)
    {
        if (placement is not null
            && _byPlacement.TryGetValue(placement, out DestructibleObject? exactPlacement))
        {
            return ApplyDamage(exactPlacement, damage);
        }

        if (owner is null || !_byOwner.TryGetValue(owner, out List<DestructibleObject>? items))
        {
            return null;
        }

        // A normal OBJECTS.QVM task produces one state object even when it places Model, Model 2,
        // and further instances.  Attached magic objects deliberately produce several state objects
        // beneath one owner, so an owner-only hit must retain the old "unplaced" fallback rather than
        // accidentally choosing an arbitrary attachment.
        DestructibleObject? exactOwner = items.Count == 1
            ? items[0]
            : items.FirstOrDefault(item => item.Placement is null && !item.IsDestroyed);
        if (exactOwner is not null)
        {
            return ApplyDamage(exactOwner, damage);
        }

        return null;
    }

    /// <summary>Applies retail's immediate direct-weapon-hit transition to one exact vehicle.</summary>
    public DestructibleObject? TryDestroyDirectVehicle(GameTask? owner, WorldPlacement? placement = null)
    {
        DestructibleObject? item = null;
        if (placement is not null)
        {
            _byPlacement.TryGetValue(placement, out item);
        }

        if (item is null
            && owner is not null
            && _byOwner.TryGetValue(owner, out List<DestructibleObject>? items)
            && items.Count == 1)
        {
            item = items[0];
        }

        return item?.Kind == DestructibleObjectKind.Vehicle
            ? ApplyDamage(item, 1f)
            : null;
    }

    /// <summary>Applies one weapon hit to the nearest live destructible within the supplied radius.</summary>
    public DestructibleObject? TryDestroyAt(
        Vector3d impactUnits,
        float radiusUnits = HitRadiusUnits,
        GameTask? owner = null,
        WorldPlacement? placement = null,
        float damage = 1f)
    {
        if (placement is not null && _byPlacement.TryGetValue(placement, out DestructibleObject? exactPlacement))
        {
            return ApplyDamage(exactPlacement, damage);
        }

        if (owner is not null
            && _byOwner.TryGetValue(owner, out List<DestructibleObject>? owned)
            && (owned.Count == 1
                ? owned[0]
                : owned.FirstOrDefault(item => item.Placement is null && !item.IsDestroyed)) is { } exactOwner)
        {
            return ApplyDamage(exactOwner, damage);
        }

        DestructibleObject? nearest = null;
        double nearestDistance = radiusUnits;
        foreach (DestructibleObject item in _objects)
        {
            if (item.IsDestroyed)
            {
                continue;
            }

            double distance = (item.PositionUnits - impactUnits).Length;
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = item;
            }
        }

        return nearest is null ? null : ApplyDamage(nearest, damage);
    }

    /// <summary>
    /// Applies one native <c>Explosion0</c> contact to every authored destructible in its blast.
    /// </summary>
    /// <param name="source">The object that owns the blast, which must not be hit by its own fireball.</param>
    /// <param name="originUnits">The explosion centre in engine units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The outer contact range after the full-damage radius.</param>
    /// <param name="damageFactor">The damage delivered by a full-strength contact.</param>
    /// <param name="canContact">Optional world-visibility gate for the exact target.</param>
    /// <returns>The exact glass and explosive objects whose native hit callbacks should run.</returns>
    /// <remarks>
    /// Retail does not destroy only the nearest object: <c>Glass_ExplosionHitHandler</c> and
    /// <c>ExplodeMagicObj_ExplosionHitHandler</c> are both registered as Explosion0 collision
    /// callbacks. The fireball therefore visits every authored object in its contact envelope. Marking
    /// the state before returning is important: a chain reaction must not enqueue the same object twice.
    /// </remarks>
    public IReadOnlyList<DestructibleObject> ApplyExplosionHits(
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits = 0f,
        float damageFactor = 1f,
        Func<DestructibleObject, bool>? canContact = null)
    {
        float contactRadius = Math.Max(0f, radiusUnits) + Math.Max(0f, falloffUnits);
        List<DestructibleObject> affected = [];

        foreach (DestructibleObject item in _objects)
        {
            if (item.IsDestroyed
                || ReferenceEquals(item, source)
                || (item.PositionUnits - originUnits).Length
                    > contactRadius + Math.Max(0f, item.ExplosionContactRadiusUnits))
            {
                continue;
            }

            if (canContact is not null && !canContact(item))
            {
                continue;
            }

            float distance = (float)(item.PositionUnits - originUnits).Length;
            float dose = ContactDose(
                distance,
                item.ExplosionContactRadiusUnits,
                radiusUnits,
                falloffUnits,
                damageFactor);
            if (ApplyDamage(item, dose) is { } destroyed)
            {
                affected.Add(destroyed);
            }
        }

        return affected;
    }

    private static float ContactDose(
        float distance,
        float targetRadiusUnits,
        float radiusUnits,
        float falloffUnits,
        float damageFactor)
    {
        // This is the same order as FUN_00416c90: d = distance - targetBound - blastRadius;
        // d < 0 returns full damage, d >= falloff returns zero, and the remaining interval is linear.
        float d = distance
            - Math.Max(0f, targetRadiusUnits)
            - Math.Max(0f, radiusUnits);
        if (d < 0f)
        {
            return damageFactor > 0f ? damageFactor : 0f;
        }

        if (!(falloffUnits > 0f) || d >= falloffUnits || !(damageFactor > 0f))
        {
            return 0f;
        }

        return (falloffUnits - d) * damageFactor / falloffUnits;
    }

    private static DestructibleObject? ApplyDamage(DestructibleObject item, float damage)
    {
        if (item.IsDestroyed || !(damage > 0f))
        {
            return null;
        }

        // Vehicle and glass callbacks are binary state transitions. ExplodeObject and the attached
        // ExplodeMagicObj family instead accumulate the incoming hit value and compare it with their
        // authored damage scale (IGI1 0x481e60: +0x1e8 against +0x1ec).
        if (item.Kind != DestructibleObjectKind.Explosive)
        {
            item.IsDestroyed = true;
            return item;
        }

        item.ExplosionDamageAccumulated += damage;
        float threshold = item.ExplosionDamageThreshold > 0f
            ? item.ExplosionDamageThreshold
            : ExplosionDamageDefaults.DamageThreshold;
        if (item.ExplosionDamageAccumulated < threshold)
        {
            return null;
        }

        item.IsDestroyed = true;
        return item;
    }

    private DestructibleObject Add(DestructibleObject item)
    {
        _objects.Add(item);
        if (!_byOwner.TryGetValue(item.Owner, out List<DestructibleObject>? owned))
        {
            owned = [];
            _byOwner.Add(item.Owner, owned);
        }

        owned.Add(item);
        if (item.Placement is not null)
        {
            _byPlacement[item.Placement] = item;
        }

        return item;
    }

    private void RegisterPlacement(DestructibleObject item, WorldPlacement placement) =>
        _byPlacement[placement] = item;

    private static bool TryMagicKind(string typeName, out DestructibleObjectKind kind)
    {
        if (string.Equals(typeName, GlassType, StringComparison.Ordinal))
        {
            kind = DestructibleObjectKind.Glass;
            return true;
        }

        if (string.Equals(typeName, ExplodeMagicType, StringComparison.Ordinal))
        {
            kind = DestructibleObjectKind.Explosive;
            return true;
        }

        kind = default;
        return false;
    }

    private static Vector3d PositionOf(DeclaredTask task, WorldPlacement? placement)
    {
        if (placement is not null)
        {
            return placement.Position * WorldScene.UnitsPerMetre;
        }

        return task.TryGetValue("Position", out object? raw) && raw is Vector3d position
            ? position
            : default;
    }

    private static void ReadExplodeParameters(DeclaredTask task, DestructibleObject item)
    {
        item.DestroyedModelName = ReadString(task, "Destroyed model");
        item.ExplosionRadiusUnits = ExplosionDamageDefaults.ToUnits(ReadFloat(task, "Explosion radius"));
        item.ExplosionFalloffUnits = ExplosionDamageDefaults.ToUnits(ReadFloat(task, "Explosion falloff radius"));
        item.ExplosionDamageFactor = ReadFloat(task, "Explosion damage scale", ExplosionDamageDefaults.DamageFactor);
        item.ExplosionDamageThreshold = ReadFloat(
            task,
            "Damage scale",
            ReadFloat(
                task,
                "Max damage before explosion",
                ExplosionDamageDefaults.DamageThreshold));
        item.ExplosionDelayTicks = Math.Max(0, (int)(ReadFloat(task, "Explosion delay") * 30f));
        item.ExplosionSound = ReadString(task, "Explosion sound");
        item.ExplosionFireballs = ReadFloat(task, "Explosion fireballs");
        item.ExplosionFragments = ReadInt(task, "Explosion fragments");
        item.HasExplosionSmoke = true;
        item.ExplosionExpression = ReadString(task, "Explosion expression");
    }

    private bool ExplosionEnabled(DestructibleObject item) =>
        item.ExplosionExpression.Length == 0
        || _expressions.EvaluateCondition(item.ExplosionExpression, emptyValue: false, item.Owner);

    /// <summary>Gets whether an explosive object should produce its authored blast.</summary>
    public bool ShouldExplode(DestructibleObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.Kind is DestructibleObjectKind.Explosive or DestructibleObjectKind.Vehicle)
            && ExplosionEnabled(item);
    }

    /// <summary>Starts an authored explosion immediately or arms its retail delay.</summary>
    /// <returns><see langword="true"/> when the caller should detonate now.</returns>
    public bool ScheduleExplosion(DestructibleObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ShouldExplode(item) || item.ExplosionStarted || item.PendingExplosionTicks > 0)
        {
            return false;
        }

        if (item.ExplosionReady)
        {
            item.ExplosionReady = false;
            item.ExplosionStarted = true;
            return true;
        }

        if (item.ExplosionDelayTicks <= 0)
        {
            item.ExplosionStarted = true;
            return true;
        }

        item.PendingExplosionTicks = item.ExplosionDelayTicks;
        return false;
    }

    private static float ReadFloat(DeclaredTask task, string name, float fallback = 0f)
    {
        if (!task.TryGetValue(name, out object? raw))
        {
            return fallback;
        }

        // OBJECTS.QVM declares Explosion fireballs as Int32, while the other explosion tuning
        // values are Real32.  The native parameter reader accepts either numeric stack type; do the
        // same here so the authored fireball count is not silently replaced by zero.
        return raw switch
        {
            float value => value,
            double value => (float)value,
            int value => value,
            short value => value,
            _ => fallback,
        };
    }

    private static int ReadInt(DeclaredTask task, string name, int fallback = 0) =>
        task.TryGetValue(name, out object? raw) && raw is int value ? value : fallback;

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is string value
            ? value.TrimEnd('\r', '\n')
            : string.Empty;

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static class ExplosionDamageDefaults
    {
        public const float UnitsPerMetre = 4096f;
        public const float DamageFactor = 1f;
        public const float RadiusUnits = 5f * UnitsPerMetre;
        public const float FireballScale = 1f;
        public const int FragmentCount = 6;
        public const float DamageThreshold = 1f;

        public static float ToUnits(float metres) => metres * UnitsPerMetre;
    }
}
