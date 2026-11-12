using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Saves;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.World;

/// <summary>
/// What blasts at a task of the level while its script holds: the engine's <c>MineField</c>.
/// </summary>
/// <remarks>
/// <para>
/// One type in both games, declared under <c>EditorRigidObj</c> with the same twelve parameters and the same run handler
/// (Project I.G.I. <c>MineField_Run</c>, <c>0x43ABC0</c>; I.G.I. 2 <c>MineField_RunHandler</c>): while it waits it counts
/// the wait down; once it has waited, and its <c>Explode</c> slot is above or below nought, it sets off an explosion at
/// its target and waits its seconds again. A wait of -1 is for ever: it explodes once.
/// </para>
/// <para>
/// The explosion is each title's own -- its explosion world, how the target is found and put on the ground -- which is
/// <see cref="Explode"/>.
/// </para>
/// </remarks>
public abstract partial class MineFieldTask : TickedTask, IExchangedState
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MineField";

    /// <summary>The model of the fragments its explosion throws.</summary>
    public const string FragmentModel = "1009_01_1";

    private readonly MineFieldPause _pause = new();
    private LevelExpression? _explode;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the script that sets it off.</summary>
    [ScriptParam(ScriptParamType.VarString, "Explode")]
    public string ExplodeExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the explosion's radius, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius (meter)")]
    public float Radius { get; set; } = 3f;

    /// <summary>Gets or sets the radius the explosion's damage falls off over, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion falloff radius (meter)")]
    public float FalloffRadius { get; set; } = 1.5f;

    /// <summary>Gets or sets the explosion's damage.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion damage scale")]
    public float DamageScale { get; set; } = 3f;

    /// <summary>Gets or sets how long the explosion waits before it goes off, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay (seconds)")]
    public float Delay { get; set; }

    /// <summary>Gets or sets how many fragments the explosion throws.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explosion fragments")]
    public int Fragments { get; set; } = 3;

    /// <summary>Gets or sets how many fireballs the explosion makes.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explosion fireballs")]
    public int Fireballs { get; set; } = 3;

    /// <summary>Gets or sets the explosion's sound.</summary>
    [ScriptParam(ScriptParamType.String16, "Explosion sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets the id of the task it blasts at.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explode close to task ID")]
    public int TargetId { get; set; }

    /// <summary>Gets or sets the seconds between two explosions; -1 explodes once.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay between explosions (seconds) -1: explode once")]
    public float DelayBetween { get; set; } = 5f;

    /// <summary>Gets or sets a value indicating whether the explosion is put on the ground beneath its target.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Snap explosion to ground")]
    public bool SnapToGround { get; set; } = true;

    /// <summary>Gets or sets the ticks before it may explode again: nought when ready, -1 for never.</summary>
    public int Pause { get => _pause.Ticks; set => _pause.Ticks = value; }

    /// <summary>Gets the wait between explosions in ticks, as the title's build rounds it.</summary>
    protected virtual int DelayBetweenTicks => (int)(DelayBetween * LevelRun.Frequency);

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!level.EvaluatesExpressions)
        {
            return;
        }

        if (_pause.Due(() =>
            {
                _explode ??= level.Compile(ExplodeExpression, this);
                if (_explode.IsEmpty)
                {
                    return false;
                }

                // Above or below nought: a value that is not a number sets nothing off.
                double value = level.Evaluate(_explode);
                return value < 0.0 || value > 0.0;
            }))
        {
            Explode();
            Pause = DelayBetweenTicks;
        }
    }

    /// <summary>Sets off the explosion at the target (<c>MineField_Explode</c>, <c>0x43AC40</c>).</summary>
    public abstract void Explode();

    /// <inheritdoc/>
    /// <remarks>What a save keeps of it is the wait (I.G.I. 2 <c>MineField_ptSerializeParameterList</c>).</remarks>
    public void Exchange(StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Pause = exchange.Int(Pause);
    }
}
