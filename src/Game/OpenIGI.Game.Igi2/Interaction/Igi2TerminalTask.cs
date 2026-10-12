using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A computer a human hacks, I.G.I. 2's <c>Terminal</c> (terminal.c): an <c>ExplodeObject</c> that is also a terminal
/// (<see cref="Igi2Terminal"/>), exporting <c>isInUse</c>, <c>isLastInUse</c>, <c>isActive</c>, <c>isHacked</c>,
/// <c>isHackedThisTick</c> and <c>nClientID</c> beside the explodable's <c>isExploded</c>.
/// </summary>
/// <remarks>
/// <c>Terminal_CreateHandler</c> gives its blast the defaults of a terminal: a damage scale of one, half a metre's radius and
/// falloff, no delay, five fragments and four fireballs, and <c>explo_02_m</c>; four seconds' hack to <c>tba_hackcomp</c>.
/// </remarks>
public sealed partial class Igi2TerminalTask
    : Igi2Task, IIgi2PlacedTask, IIgi2ExplosiveParameters, IIgi2WeaponHitTarget, IIgi2ExplodingTask, IIgi2WorkedActivatable, IIgi2TerminalOwner
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Terminal";

    /// <summary>Initializes a new instance of the <see cref="Igi2TerminalTask"/> class.</summary>
    public Igi2TerminalTask()
    {
        Explosive = new Igi2Explosive(this, this);
        Terminal = new Igi2Terminal(this);
    }

    /// <summary>Gets what it does when it explodes.</summary>
    public Igi2Explosive Explosive { get; }

    /// <summary>Gets the terminal it is.</summary>
    public Igi2Terminal Terminal { get; }

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

    /// <inheritdoc/>
    public bool IsDestroyed => Explosive.IsExploded;

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Terminal.Radius;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Terminal;

    /// <inheritdoc/>
    /// <remarks><c>Terminal_GetActivateTime</c>: its hack time.</remarks>
    public float ActivateTime => HackTime;

    /// <inheritdoc/>
    public Vector3d MagicVertex => Terminal.MagicVertex;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => Explosive.Frame;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level) => Terminal.Run(level);

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context) => Terminal.EvaluateActivate(context, this);

    /// <inheritdoc/>
    public void Activate(int activateTime) => Terminal.Activate();

    /// <inheritdoc/>
    public void Finish() => Terminal.Hacked();

    /// <inheritdoc/>
    public void WeaponHit(in Igi2WeaponHit hit) => Explosive.WeaponHit(hit.Damage);

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
    protected override void OnExportVars(ScriptVarRegistry variables) => Terminal.ExportVars(variables, this);

    /// <inheritdoc/>
    protected override void OnParsed() => Explosive.Reset();
}
