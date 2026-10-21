using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// Tracks the retail <c>ExplodeObject</c>, glass, and explosive magic-object state shared by rendering,
/// collision, script variables, and impact handling.
/// </summary>
public sealed class DestructibleObjectRegistry
{
    private const string DestroyedModelParameter = "Destroyed model";
    private const string ExplosionDamageScaleParameter = "Damage scale";
    private const string GlassType = "TASKTYPE_GLASS";
    private const string CarType = "Car";
    private const string ExplodeObjectType = "ExplodeObject";
    private const string GeneratorType = "Generator";
    private const string TerminalType = "Terminal";
    private const string RadioType = "Radio";
    private const string SecurityCameraControlType = "SCameraControl";
    private const string AlarmControlType = "AlarmControl";
    private const string ExplodeMagicType = "TASKTYPE_EXPLODEMAGICOBJ";

    private readonly List<DestructibleObject> _objects = [];
    private readonly Dictionary<GameTask, List<DestructibleObject>> _byOwner =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, DestructibleObject> _byPlacement =
        new(ReferenceEqualityComparer.Instance);
    private readonly LevelExpressionRuntime _expressions;
    private readonly List<DestructibleObject> _readyExplosions = [];
    private readonly IgiRandom _random;
    private readonly DestructibleVehicleBlasts _vehicleBlasts;

    private DestructibleObjectRegistry(LevelExpressionRuntime expressions, IgiRandom random)
    {
        _expressions = expressions;
        _random = random;
        _vehicleBlasts = new DestructibleVehicleBlasts(_objects, random);
    }

    /// <summary>The weapon id a car's explosion handler (<c>0x42FB40</c>) answers to: 12, the LAW 80.</summary>
    /// <remarks>
    /// The handler reads the first field of the blast's source record (<c>Explosion0+0x84</c>) and does
    /// nothing unless it is 12. A missile hands over the weapon record that fired it, whose first field
    /// is the weapon id; a grenade, an M203 round, a barrel, a wreck and a vehicle's own gun all hand
    /// over none — so in the original only a LAW rocket ever blows up a car.
    /// </remarks>
    public const int CarDestroyingWeaponId = 12;

    /// <summary>A car with no explosion points: its countdown after the single blast (<c>0x42E76A</c>).</summary>
    public const int CarSingleBlastTicks = 10;

    /// <summary>A car with explosion points: its countdown after the per-point blasts (<c>0x42E6FF</c>).</summary>
    public const int CarPointBlastTicks = 30;

    /// <summary>The base of a pane's break threshold, the <c>0.75</c> <c>0x4498F1</c> stores.</summary>
    public const float GlassThresholdBase = 0.75f;

    /// <summary>The random span added to it: <c>0x449919</c> adds <c>rand * 0.75</c>.</summary>
    public const float GlassThresholdSpan = 0.75f;

    /// <summary>Gets all destructible objects found in the current level.</summary>
    public IReadOnlyList<DestructibleObject> Objects => _objects;

    /// <summary>Builds destructible state from declared tasks and expanded magic-object placements.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="placements">Every expanded world placement.</param>
    /// <param name="variables">The script variables the objects export.</param>
    /// <param name="vehiclePhysics">The CAR definitions, for a car's explosion points and wreck.</param>
    /// <param name="random">
    /// The engine's global stream: each pane's break threshold and each car explosion point's delay.
    /// </param>
    public static DestructibleObjectRegistry Build(
        GameTask root,
        IEnumerable<WorldPlacement> placements,
        ScriptVarRegistry variables,
        VehiclePhysicsRegistry? vehiclePhysics = null,
        IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(variables);

        DestructibleObjectRegistry registry = new(new LevelExpressionRuntime(variables), random ?? new IgiRandom());
        WorldPlacement[] resolved = [.. placements];

        foreach (DeclaredTask task in root.DescendantsAndSelf().OfType<DeclaredTask>())
        {
            // OBJECTS.QVM uses the same live destruction contract for ExplodeObject, Terminal,
            // AlarmControl, and SCameraControl: the first model is replaced by Destroyed model once
            // a weapon hit destroys the task.  A Switch also has a field of that name, but has no
            // explosion parameters and is governed by its separate destructable flag.
            //
            // ExplodeObject and every type derived from it export isExploded whatever the model
            // (0x481F90), and the blast (0x481A60) only swaps in a Destroyed model that
            // resolves -- with none the object stays as it is. Mission 7's Generator_99 is one: its
            // own On expression is !Generator_99.isExploded, so it must still be able to blow up.
            bool hasDestroyedModel = task.TryGetValue(DestroyedModelParameter, out object? rawDestroyed)
                && rawDestroyed is string { Length: > 0 };
            if (!task.TryGetValue(ExplosionDamageScaleParameter, out _)
                || (!hasDestroyedModel && !ExplodesWithoutDestroyedModel(task)))
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
            item.KeepsModelWhenDestroyed = !hasDestroyedModel;
            item.ExplodesOnCountdown = ExplodesWithoutDestroyedModel(task);

            variables.BindBool(task, "isExploded", () => item.IsDestroyed, value =>
            {
                if (value)
                {
                    item.IsDestroyed = true;
                }
            });
            variables.BindBool(task, "isLastExploded", () => item.IsLastDestroyed, _ => { });
        }

        // Every Car exports isExploded, whether or not its CAR record names a wreck: TASKTYPE_CAR
        // (0x42DCF0) binds "%s.isExploded" to the byte at Car+11989 on activation (0x42FCE0) with
        // no condition. Mission 1's gates read it off the escape truck (Car_1099, 622_01_1), whose
        // record has explosion points and no wreck model. There is no isLastExploded on a car.
        foreach (DeclaredTask task in root.DescendantsAndSelf().OfType<DeclaredTask>())
        {
            if (!string.Equals(task.Type.Name, CarType, StringComparison.Ordinal))
            {
                continue;
            }

            string model = task.TryGetValue(WorldScene.ModelParameter, out object? rawModel) && rawModel is string name
                ? name
                : string.Empty;
            CarPhysicsDefinition car = default;
            bool hasCar = model.Length > 0 && vehiclePhysics?.TryGetCar(model, out car) == true;

            // The car's own model (Car+108) carries its bound and its explosion points; the wheels
            // and anything else it owns are its children, which it deletes when it blows up (0x401780).
            WorldPlacement[] carPlacements = [.. resolved.Where(candidate => IsOwnedBy(candidate.Owner, task))];
            WorldPlacement? body = carPlacements.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Owner, task)
                    && string.Equals(candidate.Instance.Name, model, StringComparison.OrdinalIgnoreCase))
                ?? carPlacements.FirstOrDefault(candidate => ReferenceEquals(candidate.Owner, task));
            DestructibleObject item = registry.Add(new DestructibleObject(
                DestructibleObjectKind.Vehicle,
                task,
                body,
                CarType,
                PositionOf(task, body))
            {
                DestroyedModelName = hasCar ? car.DestroyedModel : string.Empty,
                ExplosionPoints = hasCar ? car.ExplosionPoints : [],
                Orientation = body?.Orientation ?? Matrix3f.Identity,
            });
            foreach (WorldPlacement carPlacement in carPlacements)
            {
                registry.RegisterPlacement(item, carPlacement);
            }

            // sub_4B7EE0 binds the byte itself, so a script can write it either way.
            variables.BindBool(task, "isExploded", () => item.IsDestroyed, value => item.IsDestroyed = value);
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

            if (kind == DestructibleObjectKind.Glass)
            {
                // Glass init (0x4497F0): +0x13C = 0, +0x140 = 0.75 + rand * 0.75 from the engine's
                // global stream (0x416D20 -> 0x4B4770).
                item.ExplosionDamageThreshold = GlassThresholdBase + (registry._random.NextSingle() * GlassThresholdSpan);
            }

            if (kind == DestructibleObjectKind.Explosive)
            {
                item.ExplosionRadiusUnits = placement.MagicExplosionRadiusMetres > 0f
                    ? ExplosionDamageDefaults.ToUnits(placement.MagicExplosionRadiusMetres)
                    : ExplosionDamageDefaults.RadiusUnits;
                item.ExplosionFalloffUnits = ExplosionDamageDefaults.ToUnits(placement.MagicExplosionFalloffMetres);
                // The authored damage, zero included: Explosion0 (0x47F200) sends its message to nobody when
                // its damage is 0, so an object authored that way breaks without hurting anyone.
                item.ExplosionDamageFactor = Math.Max(0f, placement.MagicExplosionDamageFactor);
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
    /// <remarks>
    /// An object that keeps its model when it blows up (<see cref="DestructibleObject.KeepsModelWhenDestroyed"/>)
    /// is never destroyed here: the placement is still drawn and still collides, only its task state
    /// has changed.
    /// </remarks>
    public bool IsDestroyed(WorldPlacement placement) =>
        placement is not null
        && _byPlacement.TryGetValue(placement, out DestructibleObject? item)
        && item.IsDestroyed
        && !item.KeepsModelWhenDestroyed;

    /// <summary>Gets whether a task has any destroyed object registered under it.</summary>
    public bool IsDestroyed(GameTask task) =>
        task is not null
        && _byOwner.TryGetValue(task, out List<DestructibleObject>? items)
        && items.Any(item => item.IsDestroyed);

    /// <summary>Moves every destructible identity owned by a live task to its current world position.</summary>
    /// <param name="owner">The exact moving task.</param>
    /// <param name="positionUnits">Its current origin in engine units.</param>
    /// <param name="orientation">Its current orientation, when it turns as well as moves.</param>
    /// <returns><see langword="true"/> when the task owns at least one destructible identity.</returns>
    /// <remarks>
    /// Vehicle destruction is dispatched after the missile has collided with the live world mesh.
    /// The blast envelope must therefore use the same moving origin instead of the authored spawn;
    /// otherwise an APC that has followed its alarm route can be visibly struck while its explosion
    /// callback remains kilometres of engine units behind it. The orientation places a dying car's
    /// explosion points the way it is facing now.
    /// </remarks>
    public bool UpdatePosition(GameTask owner, Vector3d positionUnits, Matrix3f? orientation = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_byOwner.TryGetValue(owner, out List<DestructibleObject>? items))
        {
            return false;
        }

        foreach (DestructibleObject item in items)
        {
            item.PositionUnits = positionUnits;
            if (orientation is { } turned && item.Kind == DestructibleObjectKind.Vehicle)
            {
                item.Orientation = turned;
            }
        }

        return true;
    }

    /// <summary>Returns the blasts dying cars set off that are due this tick.</summary>
    /// <remarks>
    /// Each is an <c>Explosion0</c> of its own: the caller applies it to the world exactly as it does any
    /// other, handing it no source record — so no car blast can set another car off.
    /// </remarks>
    public IReadOnlyList<VehicleBlast> ConsumeVehicleBlasts() => _vehicleBlasts.ConsumeVehicleBlasts();

    /// <summary>Returns the cars whose countdown ran out this tick and left the world.</summary>
    public IReadOnlyList<DestructibleObject> ConsumeExplodedVehicles() => _vehicleBlasts.ConsumeExplodedVehicles();

    /// <summary>Gets every placement a destructible answers for — a car's body, wheels and attachments.</summary>
    public IEnumerable<WorldPlacement> PlacementsOf(DestructibleObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach ((WorldPlacement placement, DestructibleObject owner) in _byPlacement)
        {
            if (ReferenceEquals(owner, item)
                || (item.Kind == DestructibleObjectKind.Vehicle && IsOwnedBy(placement.Owner, item.Owner)))
            {
                yield return placement;
            }
        }
    }

    /// <summary>Advances the script-visible previous-state flags by one simulation tick.</summary>
    /// <remarks>
    /// Also runs every dying car's destruction (<c>0x42E5A0</c>, from the car's tick <c>0x42E320</c>) and
    /// the fuses of the blasts it set off (<c>Explosion0</c>'s <c>0x480F40</c>).
    /// </remarks>
    public void Tick()
    {
        foreach (DestructibleObject item in _objects)
        {
            item.IsLastDestroyed = item.IsDestroyed;
            if (item.Kind == DestructibleObjectKind.Vehicle)
            {
                _vehicleBlasts.TickDyingVehicle(item);
                continue;
            }

            if (item.ExplodesOnCountdown)
            {
                TickCountdown(item);
                continue;
            }

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

        _vehicleBlasts.TickVehicleBlasts();
    }

    /// <summary>One ExplodeObject update, <c>0x481970</c>.</summary>
    /// <remarks>
    /// The object explodes when its Explosion expression holds or its countdown stands at 0, and counts
    /// down otherwise. The explosion (<c>0x481A60</c>) raises <c>isExploded</c> at once, but the
    /// Explosion0 it creates only goes off on its own first update, a tick later -- so the blast is
    /// handed to the host on the next tick.
    /// </remarks>
    private void TickCountdown(DestructibleObject item)
    {
        if (item.BlastDue)
        {
            item.BlastDue = false;
            _readyExplosions.Add(item);
            return;
        }

        if (item.IsDestroyed)
        {
            return;
        }

        bool triggered = item.ExplosionExpression.Length > 0 && ExplosionEnabled(item);
        if (triggered || item.Countdown == 0)
        {
            item.IsDestroyed = true;
            item.BlastDue = true;
        }
        else if (item.Countdown > 0)
        {
            item.Countdown--;
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
    /// <remarks>
    /// Retail has no search here: the round's trace (<c>0x47B750</c>) hands its damage message to the
    /// struck task's own type handler and nothing else, so a hit that names no destructible destroys
    /// none. The damage is the firing weapon's <c>Damage Factor</c> (the round's <c>+56</c>, copied from
    /// weapon <c>+1620</c>; the knife sends the same field), and a pane only breaks once the sum reaches
    /// its drawn threshold (<c>0x449B00</c>) -- a Glock round at 0.25 takes three to six.
    /// A placement is the stronger identity -- a wall of the Building that owns a pane names the
    /// Building, and must not fall back to that pane through the owner.
    /// </remarks>
    public DestructibleObject? TryDestroy(
        GameTask? owner,
        WorldPlacement? placement = null,
        float damage = 1f)
    {
        if (placement is not null)
        {
            return _byPlacement.TryGetValue(placement, out DestructibleObject? exactPlacement)
                && (owner is null || ReferenceEquals(exactPlacement.Owner, owner))
                ? DestructibleDamage.ApplyDamage(exactPlacement, damage, explosion: false)
                : null;
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
            return DestructibleDamage.ApplyDamage(exactOwner, damage, explosion: false);
        }

        return null;
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
    /// <param name="sourceWeaponId">
    /// The id of the weapon whose record the blast carries as its source (<c>Explosion0+0x84</c>), or 0
    /// for a blast that carries none.
    /// </param>
    /// <returns>The exact glass and explosive objects whose native hit callbacks should run.</returns>
    /// <remarks>
    /// <para>
    /// Retail does not destroy only the nearest object: <c>Glass_ExplosionHitHandler</c> and
    /// <c>ExplodeMagicObj_ExplosionHitHandler</c> are both registered as Explosion0 collision
    /// callbacks. The fireball therefore visits every authored object in its contact envelope. Marking
    /// the state before returning is important: a chain reaction must not enqueue the same object twice.
    /// </para>
    /// <para>
    /// A car is not returned. Its handler (<c>0x42FB40</c>) only starts it dying — and only for a blast
    /// the LAW 80 fired (<see cref="CarDestroyingWeaponId"/>) that reaches it at all — and
    /// <see cref="Tick"/> takes it from there.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DestructibleObject> ApplyExplosionHits(
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits = 0f,
        float damageFactor = 1f,
        Func<DestructibleObject, bool>? canContact = null,
        int sourceWeaponId = 0)
    {
        List<DestructibleObject> affected = [];
        foreach (DestructibleObject item in _objects)
        {
            if (ExplosionHit(item, source, originUnits, radiusUnits, falloffUnits, damageFactor, canContact, sourceWeaponId) is { } destroyed)
            {
                affected.Add(destroyed);
            }
        }

        return affected;
    }

    /// <inheritdoc cref="DestructibleDamage.ExplosionHit"/>
    public static DestructibleObject? ExplosionHit(
        DestructibleObject item,
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits = 0f,
        float damageFactor = 1f,
        Func<DestructibleObject, bool>? canContact = null,
        int sourceWeaponId = 0) =>
        DestructibleDamage.ExplosionHit(
            item,
            source,
            originUnits,
            radiusUnits,
            falloffUnits,
            damageFactor,
            canContact,
            sourceWeaponId);

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

    // The ExplodeObject family, traced in IGI.exe through each type's parent: Generator (0x43B6E0),
    // Terminal (0x443680) and Radio (0x43AD80, through sub_481340) call the ExplodeObject base
    // (word_54097C), and SCameraControl (0x440C20) and AlarmControl (0x440710) are registered under
    // Terminal (word_53C680). Siren and MineField declare the same parameters but their bases are not
    // traced, so they keep the older rule.
    private static readonly HashSet<string> ExplodeObjectFamily = new(StringComparer.Ordinal)
    {
        ExplodeObjectType,
        GeneratorType,
        TerminalType,
        RadioType,
        SecurityCameraControlType,
        AlarmControlType,
    };

    private static bool ExplodesWithoutDestroyedModel(DeclaredTask task) =>
        ExplodeObjectFamily.Contains(task.Type.Name);

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
        return item.Kind == DestructibleObjectKind.Explosive && ExplosionEnabled(item);
    }

    /// <summary>Starts an authored explosion immediately or arms its retail delay.</summary>
    /// <returns><see langword="true"/> when the caller should detonate now.</returns>
    public bool ScheduleExplosion(DestructibleObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ExplodesOnCountdown)
        {
            return ScheduleCountdownExplosion(item);
        }

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

    // The countdown object already exploded in TickCountdown; its blast is due once it is ready.
    private static bool ScheduleCountdownExplosion(DestructibleObject item)
    {
        if (!item.ExplosionReady || item.ExplosionStarted)
        {
            return false;
        }

        item.ExplosionReady = false;
        item.ExplosionStarted = true;
        return true;
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

    /// <summary>Gets whether a task is the ancestor itself or lies anywhere beneath it.</summary>
    internal static bool IsOwnedBy(GameTask? task, GameTask ancestor)
    {
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
