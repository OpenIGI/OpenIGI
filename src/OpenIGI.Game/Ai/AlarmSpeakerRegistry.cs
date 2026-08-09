using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>One placed siren — an <c>AlarmLight</c> on a wall or a beeping <c>SCamera</c> on its pole.</summary>
/// <param name="Task">The placed task.</param>
/// <param name="PositionUnits">Where it hangs, in world units.</param>
/// <param name="AlarmExpression">The level expression that turns it on — usually a control's <c>isAlarm</c>.</param>
/// <param name="SirenSound">The sound it loops while on — the first of its two sound parameters.</param>
/// <param name="DestructionSound">The report it dies with — the second, which <c>0x43A5F0</c> hands its explosion.</param>
/// <param name="FollowsAnyAlarm">Whether it sounds while any alarm control is on, instead of by expression.</param>
public sealed class AlarmSpeaker(
    DeclaredTask Task,
    Vector3d PositionUnits,
    string AlarmExpression,
    string SirenSound,
    string DestructionSound,
    bool FollowsAnyAlarm = false)
{
    /// <summary>Gets the placed task.</summary>
    public DeclaredTask Task { get; } = Task;

    /// <summary>Gets where it hangs, in world units.</summary>
    public Vector3d PositionUnits { get; } = PositionUnits;

    /// <summary>Gets the level expression that turns it on.</summary>
    public string AlarmExpression { get; } = AlarmExpression;

    /// <summary>Gets the sound it loops while on.</summary>
    public string SirenSound { get; } = SirenSound;

    /// <summary>Gets the report it dies with.</summary>
    public string DestructionSound { get; } = DestructionSound;

    /// <summary>Gets whether it sounds while any alarm control is on, instead of by its own expression.</summary>
    /// <remarks>
    /// The security camera's alarm loop (<c>0x441AE0</c>) follows its control's alarm byte rather than a
    /// declared expression — the port stands the mission-wide alarm state in for that byte.
    /// </remarks>
    public bool FollowsAnyAlarm { get; } = FollowsAnyAlarm;

    /// <summary>Gets or sets whether the speaker has been shot out — retail's <c>+417</c>.</summary>
    /// <remarks>Permanent: a destroyed speaker never sounds again, whatever its expression says.</remarks>
    public bool IsDestroyed { get; set; }
}

/// <summary>All placed sirens in one mission — <c>AlarmLight</c> speakers and beeping <c>SCamera</c>s — and whether each is sounding.</summary>
/// <remarks>
/// <para>
/// The retail task (<c>0x4397C0</c>, 504 bytes) is a speaker with a lamp: its update
/// (<c>0x43A270</c>) evaluates its own level expression each tick and, while it holds,
/// <b>spawns a looping sound child at its own position</b> — destroying that child the moment the
/// expression clears. That is why every siren in a level starts on the same tick and stops
/// together, and why each one is heard from where it hangs. A destroyed speaker (<c>+417</c>)
/// silences itself for good — <see cref="TryDestroyAt"/> models that, standing proximity in for the
/// per-object damage identity the port does not carry yet.
/// </para>
/// <para>
/// The expression machinery is the same one <see cref="AiAlarmControlRegistry"/> evaluates the
/// controls through, over the same live level variables — so a speaker whose expression names
/// <c>AlarmControl_5.isAlarm</c> follows that control exactly as retail's does.
/// </para>
/// </remarks>
public sealed class AlarmSpeakerRegistry
{
    private readonly List<AlarmSpeaker> _speakers = [];
    private readonly LevelExpressionRuntime _expressions;
    private readonly Func<bool>? _anyAlarmOn;

    private AlarmSpeakerRegistry(LevelExpressionRuntime expressions, Func<bool>? anyAlarmOn)
    {
        _expressions = expressions;
        _anyAlarmOn = anyAlarmOn;
    }

    /// <summary>Gets the speakers found under the level root.</summary>
    public IReadOnlyList<AlarmSpeaker> Speakers => _speakers;

    /// <summary>Builds the registry from every <c>AlarmLight</c> and <c>SCamera</c> below <paramref name="root"/>.</summary>
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
    /// the port resolves hits against the world without naming the struck task, so nearness to the
    /// placement stands in. The lamp is a ~30 cm box — this is forgiving without being magnetic.
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
            && _expressions.EvaluateCondition(speaker.AlarmExpression, emptyValue: false);
    }

    /// <summary>Destroys whatever speaker an impact landed on, if it landed on one.</summary>
    /// <param name="impactUnits">Where the round or blast hit, in world units.</param>
    /// <param name="radiusUnits">How far the destruction reaches — a bullet's touch, a blast's radius.</param>
    /// <returns>The speaker destroyed, or <see langword="null"/> when the hit struck none.</returns>
    /// <remarks>The nearest one inside the radius takes it; one impact fells at most one speaker.</remarks>
    public AlarmSpeaker? TryDestroyAt(Vector3d impactUnits, float radiusUnits = HitRadiusUnits)
    {
        AlarmSpeaker? nearest = null;
        double nearestDistance = radiusUnits;

        foreach (AlarmSpeaker speaker in _speakers)
        {
            if (speaker.IsDestroyed)
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

    private void Collect(GameTask task)
    {
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, "AlarmLight", StringComparison.Ordinal))
        {
            // The parameter names are the level data's own declarations, so they are matched by
            // what they say rather than by a hardcoded position: the expression parameter names
            // itself, and of the two sound parameters the FIRST is the siren -- the second is the
            // report of the speaker being destroyed, which the destruction path will want one day.
            string expression = FirstString(declared, "expression");
            (string siren, string destruction) = SoundPair(declared);

            _speakers.Add(new AlarmSpeaker(declared, FirstPosition(declared), expression, siren, destruction));
        }
        else if (task is DeclaredTask camera
            && string.Equals(task.Type.Name, "SCamera", StringComparison.Ordinal))
        {
            // A security camera is the other siren the levels hang up -- the early missions have no
            // AlarmLight at all and their alarm IS the cameras' beeping. While the alarm holds, each
            // live camera loops "camera_beep_al" from where it sits (0x441AE0), and a shot camera
            // dies with "explo_camera" and a small fireball (0x441770's destruction branch). The
            // sound names are the retail update's own literals, not level parameters.
            _speakers.Add(new AlarmSpeaker(
                camera,
                FirstPosition(camera),
                AlarmExpression: string.Empty,
                SirenSound: "camera_beep_al",
                DestructionSound: "explo_camera",
                FollowsAnyAlarm: true));
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
