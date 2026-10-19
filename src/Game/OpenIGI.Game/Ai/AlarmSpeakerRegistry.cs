using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>One placed alarm emitter: a <c>Siren</c>, <c>AlarmLight</c>, <c>SCamera</c>, or spatial <c>ConditionalSound</c>.</summary>
/// <param name="Task">The placed task.</param>
/// <param name="PositionUnits">Where it hangs, in world units.</param>
/// <param name="AlarmExpression">The level expression that turns it on — usually a control's <c>isAlarm</c>.</param>
/// <param name="SirenSound">The sound it loops while on — the first of its two sound parameters.</param>
/// <param name="DestructionSound">The report it dies with — the second, which <c>0x43A5F0</c> hands its explosion.</param>
/// <param name="FollowsAnyAlarm">Whether it sounds while any alarm control is on, instead of by expression.</param>
/// <param name="CanBeDestroyed">Whether world damage can permanently silence this emitter.</param>
/// <param name="FaultySound">The loop a shot-out siren sounds instead while its expression holds, or empty.</param>
public sealed class AlarmSpeaker(
    DeclaredTask Task,
    Vector3d PositionUnits,
    string AlarmExpression,
    string SirenSound,
    string DestructionSound,
    bool FollowsAnyAlarm = false,
    bool CanBeDestroyed = true,
    string FaultySound = "")
{
    /// <summary>Gets the placed task.</summary>
    public DeclaredTask Task { get; } = Task;

    /// <summary>Gets where it hangs, in world units.</summary>
    public Vector3d PositionUnits { get; internal set; } = PositionUnits;

    /// <summary>Gets the level expression that turns it on.</summary>
    public string AlarmExpression { get; } = AlarmExpression;

    /// <summary>Gets the sound it loops while on.</summary>
    public string SirenSound { get; } = SirenSound;

    /// <summary>Gets the report it dies with.</summary>
    public string DestructionSound { get; } = DestructionSound;

    /// <summary>Gets the loop a shot-out siren sounds instead of its working one while its expression holds.</summary>
    /// <remarks>
    /// Siren's update (<c>0x4494D0</c>) keeps evaluating its On expression after the ExplodeObject it derives from has
    /// exploded (<c>+400</c>): it stops the working sound (<c>+568</c>) and starts the faulty one (<c>+600</c>) instead.
    /// The shipped sirens name <c>alarmsystem_working</c> for both, so a siren that is shot out still wails.
    /// </remarks>
    public string FaultySound { get; } = FaultySound;

    /// <summary>Gets whether it sounds while any alarm control is on, instead of by its own expression.</summary>
    /// <remarks>
    /// The security camera's alarm loop (<c>0x441AE0</c>) follows its control's alarm byte rather than a
    /// declared expression — the port stands the mission-wide alarm state in for that byte.
    /// </remarks>
    public bool FollowsAnyAlarm { get; } = FollowsAnyAlarm;

    /// <summary>Gets whether world damage can permanently silence this emitter.</summary>
    public bool CanBeDestroyed { get; } = CanBeDestroyed;

    /// <summary>Gets or sets whether the speaker has been shot out — retail's <c>+417</c>.</summary>
    /// <remarks>Permanent: a destroyed speaker never sounds its working loop again; only a siren's faulty loop is left.</remarks>
    public bool IsDestroyed { get; set; }

    /// <summary>Gets whether damage adds up before it breaks, as a camera lens's does.</summary>
    /// <remarks>
    /// A security camera's lens sums every round's and blast's damage (<c>+260</c>) and explodes only once
    /// the sum reaches <see cref="AlarmSpeakerRegistry.CameraDestroyThreshold"/>; the other emitters break on
    /// their first hit.
    /// </remarks>
    public bool AccumulatesDamage =>
        string.Equals(Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal);

    /// <summary>Gets the damage taken so far — the lens's <c>+260</c>.</summary>
    public float Damage { get; internal set; }

    /// <summary>Gets the radius a blast's falloff is measured from, in engine units.</summary>
    /// <remarks>
    /// <c>0x4422F0</c> hands <c>0x416C90</c> the lens's largest extent (<c>1</c>) times its head model's bound
    /// (<c>0x4D0950</c>, <c>MESH+0x78</c>) — 2863.59 units for the intact <c>313_02_1</c>, as the live game
    /// reports.
    /// </remarks>
    public float ExplosionContactRadiusUnits { get; internal set; } = 1f;
}

/// <summary>All placed sirens in one mission and whether each is sounding.</summary>
/// <remarks>
/// <para>
/// The retail task (<c>0x4397C0</c>, 504 bytes) is a speaker with a lamp: its update
/// (<c>0x43A270</c>) evaluates its own level expression each tick and, while it holds,
/// <b>spawns a looping sound child at its own position</b> — destroying that child the moment the
/// expression clears. That is why every siren in a level starts on the same tick and stops
/// together, and why each one is heard from where it hangs. A destroyed speaker (<c>+417</c>)
/// silences itself for good. Rich world contacts use <see cref="TryDestroy"/> to preserve the struck
/// task's identity; radius damage and legacy contacts use <see cref="TryDestroyAt"/>.
/// </para>
/// <para>
/// The expression machinery is the same one <c>AiAlarmControlRegistry</c> evaluates the
/// controls through, over the same live level variables — so a speaker whose expression names
/// <c>AlarmControl_5.isAlarm</c> follows that control exactly as retail's does.
/// </para>
/// </remarks>
public sealed class AlarmSpeakerRegistry
{
    private readonly List<AlarmSpeaker> _speakers = [];
    private readonly Dictionary<GameTask, AlarmSpeaker> _byTask = [];
    private readonly LevelExpressionRuntime _expressions;
    private readonly Func<bool>? _anyAlarmOn;

    private AlarmSpeakerRegistry(LevelExpressionRuntime expressions, Func<bool>? anyAlarmOn)
    {
        _expressions = expressions;
        _anyAlarmOn = anyAlarmOn;
    }

    /// <summary>Gets the speakers found under the level root.</summary>
    public IReadOnlyList<AlarmSpeaker> Speakers => _speakers;

    /// <summary>Builds the registry from every spatial alarm emitter below <paramref name="root"/>.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="variables">The live level variables the expressions read.</param>
    /// <param name="anyAlarmOn">Whether any alarm control is sounding — what the cameras' beeps follow.</param>
    /// <returns>The registry.</returns>
    public static AlarmSpeakerRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        Func<bool>? anyAlarmOn = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        AlarmSpeakerRegistry registry = new(new LevelExpressionRuntime(variables), anyAlarmOn);
        registry.Collect(root);
        return registry;
    }

    /// <summary>How close a hit must land to count as striking the speaker, in world units.</summary>
    /// <remarks>
    /// 0.8 m. The retail speaker takes the ordinary damage message through its own collision mesh;
    /// legacy/ballistic contacts do not always retain the struck task, so nearness to the resolved
    /// head placement stands in there. The lamp is a ~30 cm box — this is forgiving without being magnetic.
    /// </remarks>
    public const float HitRadiusUnits = 3276.8f;

    /// <summary>Evaluates whether a speaker is sounding this tick.</summary>
    /// <param name="speaker">The speaker.</param>
    /// <returns><see langword="true"/> while its expression holds and it has not been shot out.</returns>
    public bool IsOn(AlarmSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (speaker.IsDestroyed)
        {
            return false;
        }

        if (speaker.FollowsAnyAlarm)
        {
            return _anyAlarmOn?.Invoke() ?? false;
        }

        return !string.IsNullOrWhiteSpace(speaker.AlarmExpression)
            && _expressions.EvaluateCondition(speaker.AlarmExpression, emptyValue: false, speaker.Task);
    }

    /// <summary>Gets what a speaker sounds this tick: its working loop, a shot-out siren's faulty loop, or nothing.</summary>
    /// <param name="speaker">The speaker.</param>
    /// <returns>The definition to loop, or empty while it is silent.</returns>
    public string SoundNow(AlarmSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (speaker.IsDestroyed)
        {
            return speaker.FaultySound.Length > 0
                && !string.IsNullOrWhiteSpace(speaker.AlarmExpression)
                && _expressions.EvaluateCondition(speaker.AlarmExpression, emptyValue: false, speaker.Task)
                    ? speaker.FaultySound
                    : string.Empty;
        }

        return IsOn(speaker) ? speaker.SirenSound : string.Empty;
    }

    /// <summary>Gets whether the placed destructible task has already been shot out.</summary>
    public bool IsDestroyed(GameTask task) =>
        _byTask.TryGetValue(task, out AlarmSpeaker? speaker) && speaker.IsDestroyed;

    /// <summary>Binds emitter positions to their rendered scene objects.</summary>
    /// <param name="placements">The mission's resolved placements.</param>
    /// <remarks>
    /// An SCamera's script position is the holder. Retail attaches its sound and damage-bearing lens at
    /// the camera-head child placed at that holder transform. Using the resolved head placement keeps
    /// the siren, bullet hit and explosion on the visible object if a level overrides its model layout.
    /// </remarks>
    public void BindScene(IEnumerable<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        foreach (WorldPlacement placement in placements)
        {
            if (!_byTask.TryGetValue(placement.Owner, out AlarmSpeaker? speaker))
            {
                continue;
            }

            if (string.Equals(speaker.Task.Type.Name, "AlarmLight", StringComparison.Ordinal))
            {
                // AlarmLight's ExplosionHit (0x43A660) weighs a blast against its own model's bound (0x4D0950).
                if (placement.Model is { NativeMarkerBound: > 0f } light && float.IsFinite(light.NativeMarkerBound))
                {
                    speaker.ExplosionContactRadiusUnits = light.NativeMarkerBound;
                }

                continue;
            }

            if (!string.Equals(speaker.Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal)
                || !string.Equals(placement.Instance.Name, CameraModelName(speaker.Task), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            speaker.PositionUnits = placement.Position * WorldScene.UnitsPerMetre;
            if (placement.Model is { NativeMarkerBound: > 0f } model && float.IsFinite(model.NativeMarkerBound))
            {
                speaker.ExplosionContactRadiusUnits = model.NativeMarkerBound;
            }
        }
    }

    /// <summary>The damage that breaks a security camera's lens.</summary>
    /// <remarks>
    /// <c>0x4415C0</c> writes <c>0.5</c> into the lens's <c>+264</c>; its hit handlers (<c>0x4422B0</c> for a
    /// round, <c>0x4422F0</c> for a blast) add to <c>+260</c> and flag the explosion once the sum reaches it.
    /// Retail's rounds carry 0.2 in Mission 1, so a camera takes three.
    /// </remarks>
    public const float CameraDestroyThreshold = 0.5f;

    /// <summary>Hands one round to the exact alarm object named by a rich world trace.</summary>
    /// <param name="owner">The placing task hit by the trace.</param>
    /// <param name="damage">
    /// The round's damage — the firing weapon's <c>Damage Factor</c>. A camera adds it up; every other
    /// emitter breaks on the first hit whatever it is.
    /// </param>
    /// <returns>The speaker this round destroyed, or <see langword="null"/>.</returns>
    public AlarmSpeaker? TryDestroy(GameTask? owner, float damage = float.PositiveInfinity)
    {
        if (owner is null
            || !_byTask.TryGetValue(owner, out AlarmSpeaker? speaker)
            || speaker.IsDestroyed
            || !speaker.CanBeDestroyed)
        {
            return null;
        }

        return Damage(speaker, damage) ? speaker : null;
    }

    /// <summary>Hands a blast to every speaker it reaches.</summary>
    /// <param name="impactUnits">Where the blast went off, in world units.</param>
    /// <param name="radiusUnits">The blast's radius.</param>
    /// <param name="falloffUnits">How far past the radius its damage takes to reach zero.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    /// <returns>The speakers this blast destroyed.</returns>
    /// <remarks>
    /// A camera takes the shared contact dose (<c>0x416C90</c>) measured from its head's bound and adds it
    /// to what it has already taken (<c>0x4422F0</c>), so a grenade in reach breaks it outright while a
    /// weak or distant one only wears it down. An alarm light (<c>0x43A660</c>) breaks on any dose, so a blast breaks
    /// every light it reaches.
    /// </remarks>
    public IReadOnlyList<AlarmSpeaker> ApplyExplosion(
        Vector3d impactUnits,
        float radiusUnits,
        float falloffUnits,
        float damageFactor)
    {
        // Every speaker weighs the blast in its own handler (0x43A660, 0x4422F0): every light it reaches breaks.
        List<AlarmSpeaker> destroyed = [];
        foreach (AlarmSpeaker speaker in _speakers)
        {
            if (ExplosionHit(speaker, impactUnits, radiusUnits, falloffUnits, damageFactor))
            {
                destroyed.Add(speaker);
            }
        }

        return destroyed;
    }

    /// <summary>One speaker's share of a blast: its own <c>ExplosionHit</c>, as the explosion list sends it.</summary>
    /// <param name="speaker">The speaker.</param>
    /// <param name="impactUnits">Where the blast went off.</param>
    /// <param name="radiusUnits">The blast's full-damage radius.</param>
    /// <param name="falloffUnits">How far past it its damage fades.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <returns><see langword="true"/> when the blast destroyed it.</returns>
    /// <remarks>
    /// Each weighs the blast by distance against its own model's bound (<c>0x416C90</c>), whatever stands between. An
    /// <c>AlarmLight</c> (<c>0x43A660</c>) explodes on any dose at all, so a blast breaks every light it reaches; a camera's
    /// lens (<c>0x4422F0</c>) adds the dose to its sum.
    /// </remarks>
    public static bool ExplosionHit(AlarmSpeaker speaker, Vector3d impactUnits, float radiusUnits, float falloffUnits, float damageFactor)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (speaker.IsDestroyed || !speaker.CanBeDestroyed)
        {
            return false;
        }

        float dose = ExplosionDamage.Dose(
            (float)(speaker.PositionUnits - impactUnits).Length,
            speaker.ExplosionContactRadiusUnits,
            radiusUnits,
            falloffUnits,
            damageFactor);
        return dose != 0f && Damage(speaker, dose);
    }

    /// <summary>Destroys whatever speaker an impact landed on, if it landed on one.</summary>
    /// <param name="impactUnits">Where the round or blast hit, in world units.</param>
    /// <param name="radiusUnits">How far the destruction reaches — a bullet's touch, a blast's radius.</param>
    /// <returns>The speaker destroyed, or <see langword="null"/> when the hit struck none.</returns>
    /// <remarks>
    /// The nearest one inside the radius takes it; one impact fells at most one speaker. Cameras are left
    /// to <see cref="ApplyExplosion"/> and <see cref="TryDestroy"/>, which add their damage up.
    /// </remarks>
    public AlarmSpeaker? TryDestroyAt(Vector3d impactUnits, float radiusUnits = HitRadiusUnits)
    {
        AlarmSpeaker? nearest = null;
        double nearestDistance = radiusUnits;

        foreach (AlarmSpeaker speaker in _speakers)
        {
            if (speaker.IsDestroyed || !speaker.CanBeDestroyed || speaker.AccumulatesDamage)
            {
                continue;
            }

            double distance = (speaker.PositionUnits - impactUnits).Length;
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = speaker;
            }
        }

        if (nearest is not null)
        {
            nearest.IsDestroyed = true;
        }

        return nearest;
    }

    // A camera adds the hit to its sum and breaks at the threshold; anything else breaks outright.
    private static bool Damage(AlarmSpeaker speaker, float damage)
    {
        if (speaker.AccumulatesDamage)
        {
            speaker.Damage += damage;
            if (speaker.Damage < CameraDestroyThreshold)
            {
                return false;
            }
        }

        speaker.IsDestroyed = true;
        return true;
    }

    private void Add(AlarmSpeaker speaker)
    {
        _speakers.Add(speaker);
        _byTask[speaker.Task] = speaker;
    }

    private void Collect(GameTask task)
    {
        if (task is DeclaredTask sirenTask
            && string.Equals(task.Type.Name, WorldScene.SirenType, StringComparison.Ordinal))
        {
            // Retail Siren's QVM declaration supplies these explicitly. The visual comes from Model;
            // its destruction path clears that model when Destroyed model is empty, plays Explosion
            // sound and spawns the fireballs; after that its faulty loop stands in for the working one.
            Add(new AlarmSpeaker(
                sirenTask,
                FirstPosition(sirenTask),
                FirstString(sirenTask, "on expression"),
                FirstString(sirenTask, "working sound"),
                FirstString(sirenTask, "explosion sound"),
                FaultySound: FirstString(sirenTask, "faulty sound")));
        }
        else if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, "AlarmLight", StringComparison.Ordinal))
        {
            // The parameter names are the level data's own declarations, so they are matched by
            // what they say rather than by a hardcoded position: the expression parameter names
            // itself, and of the two sound parameters the FIRST is the siren -- the second is the
            // report of the speaker being destroyed, which the destruction path will want one day.
            string expression = FirstString(declared, "expression");
            (string siren, string destruction) = SoundPair(declared);

            Add(new AlarmSpeaker(declared, FirstPosition(declared), expression, siren, destruction));
        }
        else if (task is DeclaredTask camera
            && string.Equals(task.Type.Name, "SCamera", StringComparison.Ordinal))
        {
            // Detection and movement loops belong to SecurityCameraRegistry's lens state machine.
            // This entry remains as the exact-task damage/destruction proxy used by weapon traces.
            Add(new AlarmSpeaker(
                camera,
                FirstPosition(camera),
                AlarmExpression: string.Empty,
                SirenSound: string.Empty,
                DestructionSound: "explo_camera",
                FollowsAnyAlarm: false));
        }
        else if (task is DeclaredTask conditionalSound
            && string.Equals(task.Type.Name, "ConditionalSound", StringComparison.Ordinal))
        {
            // ConditionalSound is an audio-only positional emitter. The live retail Mission 1 object
            // commit log contains no loudspeaker or AlarmLight object for these tasks, so there is no
            // collision object to shoot and no destruction effect to synthesize.
            string condition = FirstString(conditionalSound, "condition");
            string sound = FirstString(conditionalSound, "sound");
            if (condition.Length > 0
                && sound.Contains("alarm", StringComparison.OrdinalIgnoreCase))
            {
                Add(new AlarmSpeaker(
                    conditionalSound,
                    FirstPosition(conditionalSound),
                    condition,
                    sound,
                    DestructionSound: string.Empty,
                    CanBeDestroyed: false));
            }
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child);
        }
    }

    // The first vector parameter whose name says it is a position -- "Position" on an AlarmLight,
    // "Holder Position" on an SCamera.
    private static Vector3d FirstPosition(DeclaredTask task)
    {
        IReadOnlyList<TaskParameter> parameters = task.Type.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Name.Contains("position", StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is Vector3d placed)
            {
                return placed;
            }
        }

        return default;
    }

    private static string FirstString(DeclaredTask task, string nameFragment)
    {
        IReadOnlyList<TaskParameter> parameters = task.Type.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string CameraModelName(DeclaredTask task) =>
        task.TryGetValue(WorldScene.SecurityCameraModelParameters[1], out object? raw)
            && raw is string name
            && name.Length > 0
            ? name
            : WorldScene.SecurityCameraModels[1];

    // The two sound parameters, in declaration order: the siren first, the destruction report
    // second -- the order the retail ctor fills +352 and +384 in.
    private static (string Siren, string Destruction) SoundPair(DeclaredTask task)
    {
        string siren = string.Empty;
        string destruction = string.Empty;

        IReadOnlyList<TaskParameter> parameters = task.Type.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (!parameters[i].Name.Contains("sound", StringComparison.OrdinalIgnoreCase)
                || task.GetValue(i) is not string value
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (siren.Length == 0)
            {
                siren = value;
            }
            else
            {
                destruction = value;
                break;
            }
        }

        return (siren, destruction);
    }
}
