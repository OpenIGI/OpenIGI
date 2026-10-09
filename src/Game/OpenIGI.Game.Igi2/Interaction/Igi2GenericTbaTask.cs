using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Something a human finishes by holding the activate key at it for a while, playing its own clip: a code pad, a padlock.</summary>
public interface IIgi2TimedAction : IIgi2WorkedActivatable
{
    /// <summary>Gets the clip the human plays at it.</summary>
    string ActivateAnimation { get; }
}

/// <summary>
/// A timed action, I.G.I. 2's <c>GenericTBA</c>: an <c>ExplodeObject</c> the player works at, exporting <c>isInUse</c>,
/// <c>isLastInUse</c>, <c>isActive</c>, <c>isFinished</c>, <c>isFinishedThisTick</c> and <c>nClientID</c> beside
/// <c>isExploded</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its tick (<c>GenericTBA_RunHandler</c>, <c>igi2 0x4C2200</c>): a finish set last tick is made, and stands for one tick as
/// finished this tick. One that is explodable then ticks as an <c>ExplodeObject</c> does, its script saying when it blows; one
/// that is not, once finished, wears its destroyed model and runs its children, once. Exploded, it is no longer active;
/// otherwise its script, where it has one, says whether it is. In use is kept while it was used in the last two ticks.
/// </para>
/// <para>
/// It answers bullets and blasts as any <c>ExplodeObject</c> does; one that is not explodable only counts them.
/// </para>
/// </remarks>
public sealed partial class Igi2GenericTbaTask
    : GameTask, IIgi2RunTask, IIgi2PlacedTask, IIgi2ExplosiveParameters, IIgi2WeaponHitTarget, IIgi2ExplodingTask, IIgi2TimedAction
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GenericTBA";

    /// <summary>The magic vertex a human works it from, <c>1</c>.</summary>
    public const int ActivateVertex = 1;

    /// <summary>How far to either side of its vertex a human may stand and still be offered it, <c>3072</c> (0.75 m).</summary>
    public const double ActivateHalfWidth = 3072.0;

    /// <summary>The ticks after its last use that it is still in use, <c>2</c>.</summary>
    public const int InUseTicks = 2;

    private byte _isInUse;
    private byte _isLastInUse;
    private int _lastInUseTick;
    private bool _isSetFinished;
    private byte _isActive = 1;
    private byte _isFinished;
    private byte _isFinishedThisTick;
    private int _finishedThisTickTick;
    private int _clientId;
    private Igi2LevelExpression? _activeExpression;
    private Igi2LevelFlowTask? _flow;

    /// <summary>Initializes a new instance of the <see cref="Igi2GenericTbaTask"/> class.</summary>
    public Igi2GenericTbaTask() => Explosive = new Igi2Explosive(this, this);

    /// <summary>Gets what it does when it explodes.</summary>
    public Igi2Explosive Explosive { get; }

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
    /// <remarks><c>GenericTBA_CreateHandler</c> (<c>igi2 0x4C20F0</c>) sets this and the rest of the blast's defaults.</remarks>
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
    /// <remarks>Four, which the create handler then sets to none.</remarks>
    [ScriptParam(ScriptParamType.Int32, "Explosion fireballs")]
    public int ExplosionFireballs { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.VarString, "Explosion expression")]
    public string ExplosionScript { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Explosion sound")]
    public string ExplosionSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the offset of where it is worked from, from its vertex, in metres along its frame.</summary>
    [ScriptParam(ScriptParamType.Real64, "Activate Offset X")]
    public double ActivateOffsetX { get; set; }

    /// <summary>Gets or sets the offset's Y.</summary>
    [ScriptParam(ScriptParamType.Real64, "Activate Offset Y")]
    public double ActivateOffsetY { get; set; }

    /// <summary>Gets or sets the offset's Z.</summary>
    [ScriptParam(ScriptParamType.Real64, "Activate Offset Z")]
    public double ActivateOffsetZ { get; set; }

    /// <summary>Gets or sets a value indicating whether it ticks as an <c>ExplodeObject</c>, its script blowing it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Explodable")]
    public bool IsExplodable { get; set; }

    /// <summary>Gets or sets the script that says whether it is active.</summary>
    [ScriptParam(ScriptParamType.VarString, "Active")]
    public string ActiveScript { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String32, "Activate Anim")]
    public string ActivateAnimation { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Activate Time (s)")]
    public float ActivateTime { get; set; } = 4f;

    /// <summary>Gets or sets the status screen's icon for it, which the status screen shows (not ported).</summary>
    [ScriptParam(ScriptParamType.DropDownCombo, "Click to select sprite")]
    public string Sprite { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String32, "Activate Sound")]
    public string ActivateSound { get; set; } = "tba_codepad";

    /// <summary>Gets a value indicating whether it is in use, <c>isInUse</c>.</summary>
    public bool IsInUse => _isInUse != 0;

    /// <summary>Gets a value indicating whether it may be worked, <c>isActive</c>.</summary>
    public bool IsActive => _isActive != 0;

    /// <summary>Gets a value indicating whether it is finished, <c>isFinished</c>.</summary>
    public bool IsFinished => _isFinished != 0;

    /// <summary>Gets a value indicating whether it was finished this tick, <c>isFinishedThisTick</c>.</summary>
    public bool IsFinishedThisTick => _isFinishedThisTick != 0;

    /// <summary>Gets the level tick it was finished on, <c>nFinishedThisFrameTick</c>.</summary>
    public int FinishedTick => _finishedThisTickTick;

    /// <inheritdoc/>
    public bool IsDestroyed => Explosive.IsExploded;

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    /// <remarks>The model it wears, with its largest scale of one.</remarks>
    public float Radius => Explosive.Models?.Invoke(Explosive.Model)?.NativeMarkerBound ?? 1f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.GenericTba;

    /// <inheritdoc/>
    public Vector3d MagicVertex =>
        Igi2MagicVertex.Position(Explosive.Models?.Invoke(Explosive.Model), ActivateVertex, Explosive.Frame, Position);

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => Explosive.Frame;

    /// <inheritdoc/>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _flow = level.LevelFlow;
        if (_isFinishedThisTick != 0)
        {
            _isFinishedThisTick = 0;
        }

        if (_isSetFinished)
        {
            _isFinished = 1;
            _isFinishedThisTick = 1;
            _isSetFinished = false;
            _finishedThisTickTick = _flow?.Tick ?? 0;
        }

        if (IsExplodable)
        {
            level.RunChildren(this);
            Explosive.Run(level);
        }
        else if (!Explosive.IsExploded && _isFinished != 0)
        {
            Explosive.SetExploded();
            level.RunChildren(this);
        }

        if (Explosive.IsExploded)
        {
            _isActive = 0;
        }
        else
        {
            _activeExpression ??= new Igi2LevelExpression(ActiveScript, Name, level.Natives);
            if (!_activeExpression.IsEmpty)
            {
                _isActive = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_activeExpression));
            }
        }

        if ((_flow?.Tick ?? 0) <= _lastInUseTick + InUseTicks)
        {
            _isLastInUse = _isInUse;
        }
        else
        {
            _isInUse = 0;
            _isLastInUse = 0;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GenericTBA_EvaluateActivateHandler</c> (<c>igi2 0x4C27B0</c>): only while active, from its second magic vertex moved
    /// by its offset along its frame, and only to a view in front of it and within three quarters of a metre to either side.
    /// </remarks>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_isActive == 0)
        {
            return;
        }

        IReadOnlyList<float> m = Explosive.Frame;
        Vector3d vertex = MagicVertex;
        Fpu ox = (Fpu)ActivateOffsetX * 4096.0, oy = (Fpu)ActivateOffsetY * 4096.0, oz = (Fpu)ActivateOffsetZ * 4096.0;
        Fpu rx = (((Fpu)m[2] * oz) + ((Fpu)m[0] * ox)) + ((Fpu)m[1] * oy);
        Fpu ry = (((Fpu)m[5] * oz) + ((Fpu)m[3] * ox)) + ((Fpu)m[4] * oy);
        Fpu rz = (((Fpu)m[8] * oz) + ((Fpu)m[6] * ox)) + ((Fpu)m[7] * oy);
        Vector3d at = new(((Fpu)vertex.X + rx).Value, ((Fpu)vertex.Y + ry).Value, ((Fpu)vertex.Z + rz).Value);
        context.OfferFromFront(at, m, ActivateHalfWidth, this);
    }

    /// <inheritdoc/>
    /// <remarks><c>GenericTBA_ActivateHandler</c> (<c>igi2 0x4C2950</c>): in use, as of this tick.</remarks>
    public void Activate(int activateTime)
    {
        _isInUse = 1;
        _lastInUseTick = _flow?.Tick ?? 0;
    }

    /// <inheritdoc/>
    /// <remarks>What <c>HumanPlayer_RunStateActivateGenericTBA</c> (<c>igi2 0x4AFEE9</c>) sets once the key has been held long enough.</remarks>
    public void Finish()
    {
        _isSetFinished = true;
        _clientId = 0;
    }

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
    /// <remarks><c>GenericTBA_RegisterScriptSymbolsHandler</c> (<c>igi2 0x4C2510</c>), after the <c>ExplodeObject</c>'s own.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        Explosive.ExportVars(variables);
        variables.BindInt(this, "isInUse", () => _isInUse, value => _isInUse = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastInUse", () => _isLastInUse, value => _isLastInUse = (byte)value, sizeof(byte));
        variables.BindInt(this, "isActive", () => _isActive, value => _isActive = (byte)value, sizeof(byte));
        variables.BindInt(this, "isFinished", () => _isFinished, value => _isFinished = (byte)value, sizeof(byte));
        variables.BindInt(this, "isFinishedThisTick", () => _isFinishedThisTick, value => _isFinishedThisTick = (byte)value, sizeof(byte));
        variables.BindInt(this, "nClientID", () => _clientId, value => _clientId = value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GenericTBA_UpdateInternalDataHandler</c> (<c>igi2 0x4C2450</c>): the <c>ExplodeObject</c>'s, then its icon looked up among
    /// the status screen's (not ported) and its script compiled.
    /// </remarks>
    protected override void OnParsed() => Explosive.Reset();
}
