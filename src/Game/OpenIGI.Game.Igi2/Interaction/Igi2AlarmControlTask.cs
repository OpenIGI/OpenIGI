using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A level's alarm, I.G.I. 2's <c>AlarmControl</c>: a <c>Terminal</c> (itself an <c>ExplodeObject</c>) whose scripts say when its
/// alarm is triggered and when it sounds, exporting <c>isTrigger</c>, <c>isAlarm</c> and <c>nTriggerLastTick</c> beside the
/// terminal's <c>isInUse</c>, <c>isLastInUse</c>, <c>isActive</c>, <c>isHacked</c>, <c>isHackedThisTick</c>, <c>nClientID</c>
/// and <c>nTeam</c>, and the explodable's <c>isExploded</c>.
/// </summary>
/// <remarks>
/// <para>
/// The terminal's tick (<c>Terminal_RunHandler</c>, <c>igi2 0x4CA9E0</c>): a hack finished last tick is made, and stands for one
/// tick as hacked this tick; it ticks as an <c>ExplodeObject</c>; exploded, it is no longer active, and otherwise its
/// <c>Active</c> script, where it has one, says whether it is; in use is kept while it was hacked at in the last two ticks.
/// </para>
/// <para>
/// Then the alarm's (<c>AlarmControl_RunHandler</c>, <c>0x4D0320</c>): last tick's alarm is kept; exploded or inactive, it is
/// neither triggered nor sounding; otherwise its trigger script says whether it is triggered, which stamps the level's tick,
/// and its alarm script whether it sounds. An alarm that starts raises <c>AIEVENT_ALARMON</c> and counts once on the player's
/// <c>nTimesAlarmTriggered</c>; one that stops raises <c>AIEVENT_ALARMOFF</c>. Both go to every team, from nowhere and no one.
/// </para>
/// <para>
/// A human hacks it as a timed action at its model's second vertex, kneeling 0.675 m out, for its hack time. The multiplayer
/// team a hack hands it over to (<c>Change Team When Hacked?</c>, <c>Start Team</c>, <c>Link To Terminal</c>) is not in the
/// sequel's single-player levels and not ported.
/// </para>
/// </remarks>
public sealed partial class Igi2AlarmControlTask
    : Igi2Task, IIgi2PlacedTask, IIgi2ExplosiveParameters, IIgi2WeaponHitTarget, IIgi2ExplodingTask, IIgi2WorkedActivatable, IIgi2TerminalOwner
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AlarmControl";

    /// <summary>The AI event an alarm that starts raises, <c>AIEVENT_ALARMON</c>.</summary>
    public const int AlarmOnEvent = 7;

    /// <summary>The AI event an alarm that stops raises, <c>AIEVENT_ALARMOFF</c>.</summary>
    public const int AlarmOffEvent = 8;

    private int _team;
    private int _isTrigger;
    private int _isAlarm;
    private int _isLastAlarm;
    private int _triggerLastTick = -10000;
    private Igi2LevelExpression? _triggerExpression;
    private Igi2LevelExpression? _alarmExpression;

    /// <summary>Initializes a new instance of the <see cref="Igi2AlarmControlTask"/> class.</summary>
    public Igi2AlarmControlTask()
    {
        Explosive = new Igi2Explosive(this, this);
        Terminal = new Igi2Terminal(this);
    }

    /// <summary>Gets what it does when it explodes.</summary>
    public Igi2Explosive Explosive { get; }

    /// <summary>Gets the terminal it is.</summary>
    public Igi2Terminal Terminal { get; }

    /// <summary>Gets or sets what an alarm that starts does beside its AI event: count on the player's <c>nTimesAlarmTriggered</c>.</summary>
    public Action? AlarmRaised { get; set; }

    /// <summary>Gets or sets where its AI events go, <c>HumanAI_SendEvent</c>, or <see langword="null"/> when no one hears them.</summary>
    public Action<Ai.Igi2AiEvent>? AiEvents { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Destroyed model")]
    public string DestroyedModel { get; set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks><c>Terminal_CreateHandler</c> (<c>igi2 0x4CA8F0</c>) sets this and the rest of the blast's defaults.</remarks>
    [ScriptParam(ScriptParamType.Real32, "Damage scale")]
    public float DamageScale { get; set; } = 1f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius")]
    public float ExplosionRadius { get; set; } = 0.5f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion falloff radius")]
    public float ExplosionFalloffRadius { get; set; } = 0.5f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion damage scale")]
    public float ExplosionDamageScale { get; set; } = 1f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay")]
    public float ExplosionDelay { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Int32, "Explosion fragments")]
    public int ExplosionFragments { get; set; } = 5;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Int32, "Explosion fireballs")]
    public int ExplosionFireballs { get; set; } = 4;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.VarString, "Explosion expression")]
    public string ExplosionScript { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Explosion sound")]
    public string ExplosionSound { get; set; } = "explo_02_m";

    /// <summary>Gets or sets the script that says whether it may be hacked and its alarm run.</summary>
    [ScriptParam(ScriptParamType.VarString, "Active")]
    public string ActiveScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the seconds hacking it takes.</summary>
    [ScriptParam(ScriptParamType.Real32, "Hack Time (s)")]
    public float HackTime { get; set; } = Igi2Terminal.DefaultHackTime;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String32, "Activate Sound")]
    public string ActivateSound { get; set; } = Igi2Terminal.DefaultActivateSound;

    /// <summary>Gets or sets the script that says whether its alarm is triggered.</summary>
    [ScriptParam(ScriptParamType.VarString, "Trigger Expression")]
    public string TriggerScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that says whether its alarm sounds.</summary>
    [ScriptParam(ScriptParamType.VarString, "Alarm Expression")]
    public string AlarmScript { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it may be hacked, <c>isActive</c>.</summary>
    public bool IsActive => Terminal.IsActive;

    /// <summary>Gets a value indicating whether it is hacked at, <c>isInUse</c>.</summary>
    public bool IsInUse => Terminal.IsInUse;

    /// <summary>Gets a value indicating whether it has been hacked, <c>isHacked</c>.</summary>
    public bool IsHacked => Terminal.IsHacked;

    /// <summary>Gets a value indicating whether it was hacked this tick, <c>isHackedThisTick</c>.</summary>
    public bool IsHackedThisTick => Terminal.IsHackedThisTick;

    /// <summary>Gets the level flow's tick it was hacked on, <c>nHackedThisFrameTick</c>.</summary>
    public int HackedTick => Terminal.HackedTick;

    /// <summary>Gets a value indicating whether its alarm is triggered, <c>isTrigger</c>.</summary>
    public bool IsTrigger => _isTrigger != 0;

    /// <summary>Gets a value indicating whether its alarm sounds, <c>isAlarm</c>.</summary>
    public bool IsAlarm => _isAlarm != 0;

    /// <summary>Gets a value indicating whether its alarm sounded last tick, <c>isLastAlarm</c>.</summary>
    public bool IsLastAlarm => _isLastAlarm != 0;

    /// <summary>Gets the level's tick it was last triggered on, <c>nTriggerLastTick</c>; -10000 until it is.</summary>
    public int TriggerLastTick => _triggerLastTick;

    /// <inheritdoc/>
    public bool IsDestroyed => Explosive.IsExploded;

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Terminal.Radius;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Terminal;

    /// <inheritdoc/>
    /// <remarks><c>Terminal_GetActivateTime</c> (<c>igi2 0x4CB0D0</c>): its hack time.</remarks>
    public float ActivateTime => HackTime;

    /// <inheritdoc/>
    public Vector3d MagicVertex => Terminal.MagicVertex;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => Explosive.Frame;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Terminal.Run(level);
        RunAlarm(level);
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context) => Terminal.EvaluateActivate(context, this);

    /// <inheritdoc/>
    public void Activate(int activateTime) => Terminal.Activate();

    /// <inheritdoc/>
    public void Finish() => Terminal.Hacked();

    /// <inheritdoc/>
    public void WeaponHit(in Igi2WeaponHit hit) => Explosive.WeaponHit(hit, this);

    /// <inheritdoc/>
    public void ExplosionHit(in Igi2ExplosionHit hit) => Explosive.ExplosionHit(hit);

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            "Destroyed model" => DestroyedModel,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Terminal_RegisterScriptSymbolsHandler</c> (<c>igi2 0x4CAC50</c>) after the <c>ExplodeObject</c>'s own, then
    /// <c>AlarmControl_RegisterScriptSymbolsHandler</c> (<c>0x4D0590</c>).
    /// </remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        Terminal.ExportVars(variables, this);
        variables.BindInt(this, "nTeam", () => _team, value => _team = value);
        variables.BindInt(this, "isTrigger", () => _isTrigger, value => _isTrigger = value);
        variables.BindInt(this, "isAlarm", () => _isAlarm, value => _isAlarm = value);
        variables.BindInt(this, "nTriggerLastTick", () => _triggerLastTick, value => _triggerLastTick = value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Terminal_UpdateInternalDataHandler</c> (<c>igi2 0x4CAC10</c>): the <c>ExplodeObject</c>'s, then its scripts compiled.
    /// </remarks>
    protected override void OnParsed() => Explosive.Reset();

    // AlarmControl_RunHandler (igi2 0x4D0320): each script's value chopped and taken as a signed byte.
    private void RunAlarm(Igi2LevelRun level)
    {
        int wasAlarm = _isAlarm;
        _isLastAlarm = wasAlarm;
        if (Explosive.IsExploded || !Terminal.IsActive)
        {
            _isTrigger = 0;
            _isAlarm = 0;
            return;
        }

        _triggerExpression ??= new Igi2LevelExpression(TriggerScript, Name, level.Natives);
        if (!_triggerExpression.IsEmpty)
        {
            _isTrigger = (sbyte)Igi2LevelExpression.Truncate(level.Evaluate(_triggerExpression));
        }

        if (_isTrigger != 0)
        {
            _triggerLastTick = level.Ticks;
        }

        _alarmExpression ??= new Igi2LevelExpression(AlarmScript, Name, level.Natives);
        if (!_alarmExpression.IsEmpty)
        {
            _isAlarm = (sbyte)Igi2LevelExpression.Truncate(level.Evaluate(_alarmExpression));
        }

        // HumanAI_SendEvent (igi2 0x460A90): AIEVENT_ALARMON (7) as it starts, AIEVENT_ALARMOFF (8) as it stops, to every team.
        if (wasAlarm == 0 && _isAlarm != 0)
        {
            AiEvents?.Invoke(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.AlarmOn, -1, 0, 0f, null, default, null));
            AlarmRaised?.Invoke();
        }
        else if (wasAlarm != 0 && _isAlarm == 0)
        {
            AiEvents?.Invoke(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.AlarmOff, -1, 0, 0f, null, default, null));
        }
    }
}
