using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>
/// A locomotive or a carriage, I.G.I. 2's <c>Train</c> (train.c): a <c>Vehicle</c> that rides the spline its <c>RailroadQTaskID</c>
/// names, at a distance along it, its carriages its child <c>Train</c> tasks, each hung a coupling's length behind the one in front.
/// An <c>AnimTask</c> drives it; on its own it does not move.
/// </summary>
/// <remarks>
/// <para>
/// <c>Train_UpdateInternalDataHandler</c>: its length is the distance between its model's first two magic vertices, its couplings; a
/// locomotive is placed (<c>Train_Move</c>). <c>Train_UpdateAnimDataHandler</c>: channel nought, unless minus one, is the thrust it
/// heads for. At tick nought it is set back: at its authored distance, its thrust and speed its original thrust's, and placed. At any
/// other tick its thrust closes on the one it heads for by its acceleration, its speed is its thrust of its top speed in km/h, it moves
/// that far along the spline, backwards when flipped, and is placed.
/// </para>
/// <para>
/// <c>Train_RunHandler</c>, once a tick: while its sound expression holds, it plays <c>train_loop_1</c>, or <c>train_loop_2</c> with no
/// carriages; its sound's two channels are its thrust (its locomotive's, for a carriage) five times over, held to between 0.8 and 1, and
/// whether that thrust is over a thousandth; its children run.
/// </para>
/// </remarks>
public sealed partial class Igi2TrainTask : Igi2Task, IIgi2Mover, IIgi2AnimTarget, IAttachParent
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = Igi2Train.TrainType;

    /// <summary>The thrust channel's value that leaves the thrust headed for as it is.</summary>
    public const float HoldThrust = -1f;

    /// <summary>What a thrust times a top speed in km/h is multiplied by to give a speed in world units a tick.</summary>
    public const double KilometresPerHourToTicks = 37.925926208496094;

    private float _length;
    private float _realThrust;
    private float _idealThrust;
    private float _speed;
    private float _position;
    private bool _isValidPosition;
    private int _carriages;
    private Igi2SplinePath? _path;
    private Igi2TrainTask[] _rake = [];
    private Vector3d _at;
    private Vector3d _lastAt;
    private float[] _orientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private float[] _lastOrientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private Igi2Pose? _built;
    private int _movedTick = int.MinValue;
    private int _runTick = -1;
    private LevelExpression? _playSound;
    private Igi2Sound? _sound;
    private IReadOnlyList<Igi2RigidObject> _solids = [];
    private IReadOnlyList<Igi2DeathZone> _deathZones = [];

    /// <summary>Gets or sets its distance along the spline as the level starts, <c>vOriginalPos</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Position")]
    public float OriginalPosition { get; set; }

    /// <summary>Gets or sets its thrust as the level starts, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Original thrust")]
    public float OriginalThrust { get; set; }

    /// <summary>Gets or sets the level id of the spline it rides.</summary>
    [ScriptParam(ScriptParamType.Int32, "RailroadQTaskID")]
    public int RailroadId { get; set; } = -1;

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String256, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the share of the gap to the thrust it heads for that its thrust closes by a tick.</summary>
    [ScriptParam(ScriptParamType.Real32, "Acceleration")]
    public float Acceleration { get; set; } = 0.05f;

    /// <summary>Gets or sets its top speed, in km/h.</summary>
    [ScriptParam(ScriptParamType.Real32, "MaxSpeed")]
    public float MaxSpeed { get; set; } = 200f;

    /// <summary>Gets or sets a value indicating whether it runs the other way along its spline.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Flip Direction")]
    public bool FlipDirection { get; set; }

    /// <summary>Gets or sets how far it is set off the rail across, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Displacement X")]
    public float DisplacementX { get; set; }

    /// <summary>Gets or sets how far it is set off the rail up, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Displacement Z")]
    public float DisplacementZ { get; set; }

    /// <summary>Gets or sets the expression that says whether it sounds.</summary>
    [ScriptParam(ScriptParamType.VarString, "Play Sound")]
    public string PlaySound { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it is a carriage: a <c>Train</c> whose parent is one.</summary>
    public bool IsCarriage => Parent is Igi2TrainTask;

    /// <summary>Gets its coupling-to-coupling length, <c>vLength</c>.</summary>
    public float Length => _length;

    /// <summary>Gets its thrust, <c>vRealThrust</c>.</summary>
    public float Thrust => _realThrust;

    /// <summary>Gets the thrust it heads for, <c>vIdealThrust</c>.</summary>
    public float IdealThrust => _idealThrust;

    /// <summary>Gets its speed, in world units a tick, <c>vSpeed</c>.</summary>
    public float Speed => _speed;

    /// <summary>Gets its distance along the spline, <c>vPos</c>.</summary>
    public float Distance => _position;

    /// <summary>Gets its sound's two channels: its thrust's level, and whether it is under way.</summary>
    public IReadOnlyList<float> SoundChannels { get; } = new float[2];

    /// <summary>Gets the sound it plays, or <see langword="null"/>.</summary>
    public Igi2Sound? Sound => _sound;

    /// <summary>Gets or sets the level's sounds.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its attach points are looked up in.</summary>
    public Igi2MagicObjTypes? Magic { get; set; }

    /// <inheritdoc/>
    public Igi2Pose Built => _built ?? Igi2Pose.FromMatrix(_at, Matrix3f.Identity);

    /// <inheritdoc/>
    public Vector3d CurrentPosition => _at;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _orientation;

    /// <inheritdoc/>
    /// <remarks>A train is a model object, which answers the attach event with where it stands (<c>ModelObj_AttachHandler</c>).</remarks>
    public Vector3d AttachPosition => _at;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _orientation;

    /// <inheritdoc/>
    public Vector3d LastPosition => _lastAt;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <summary>
    /// Measures it and the carriages behind it and puts them on their spline, as the level loads: <c>Train_UpdateInternalDataHandler</c>
    /// and, for a locomotive, <c>Train_UpdateInternalDataAfterLoadHandler</c>.
    /// </summary>
    /// <param name="models">The level's models.</param>
    /// <param name="path">The spline it rides, or <see langword="null"/> for a carriage, or a locomotive whose spline is not there.</param>
    public void Prepare(Func<string, MefModel?> models, Igi2SplinePath? path)
    {
        ArgumentNullException.ThrowIfNull(models);
        Models = models;
        _length = Igi2Train.TryMeasure(Model, models, out float length) ? length : 0f;
        _rake = [this, .. Children.OfType<Igi2TrainTask>()];
        _carriages = _rake.Length - 1;
        _position = OriginalPosition;
        _realThrust = 0f;
        _idealThrust = 0f;
        _isValidPosition = false;
        foreach (Igi2TrainTask carriage in _rake.Skip(1))
        {
            carriage.Prepare(models, null);
        }

        _path = IsCarriage ? null : path;
        if (_path is not null)
        {
            Move(int.MinValue);
            foreach (Igi2TrainTask car in _rake)
            {
                car._built = Igi2Pose.FromMatrix(car._at, Rows(car._orientation));
                car._lastAt = car._at;
                car._lastOrientation = [.. car._orientation];
            }
        }
    }

    /// <summary>Gives a carriage the solids it was built as, which then move with it.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids)
    {
        _solids = solids ?? throw new ArgumentNullException(nameof(solids));
        _deathZones = Models is { } models && Magic is { } magic && models(Model) is { } model
            ? Igi2DeathZone.Find(model, magic, models, solids)
            : [];
    }

    /// <summary>Gets or sets the humans its kill boxes are tested against.</summary>
    public IReadOnlyList<Humans.Igi2Human> Humans { get; set; } = [];

    /// <summary>Gets its kill boxes, once bound.</summary>
    public IReadOnlyList<Igi2DeathZone> DeathZones => _deathZones;

    /// <summary>Gets the train and its carriages, the locomotive first.</summary>
    public IReadOnlyList<Igi2TrainTask> Rake => _rake;

    /// <inheritdoc/>
    public void UpdateAnimData(Igi2AnimUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update.Program);
        float thrust = update.Program.Channels[0];
        if (thrust != HoldThrust)
        {
            _idealThrust = thrust;
        }

        if (update.Time != 0)
        {
            if (_length == 0f)
            {
                _length = 1f;
            }

            _realThrust = ((((Fpu)_idealThrust - _realThrust) * Acceleration) + _realThrust).F;
            _speed = ((Fpu)_realThrust * MaxSpeed * 4096000.0 * 9.259259059035685e-06).F;
            _position = ((Fpu)(FlipDirection ? -_speed : _speed) + _position).F;
            Move(update.FlowTick);
            return;
        }

        update.Program.WriteChannel(1, -2f);
        _position = OriginalPosition;
        _realThrust = 0f;
        _idealThrust = 0f;
        _isValidPosition = false;
        Move(update.FlowTick);
        _speed = ((Fpu)MaxSpeed * OriginalThrust * KilometresPerHourToTicks).F;
        _idealThrust = OriginalThrust;
        _realThrust = OriginalThrust;
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        int tick = level.LevelFlow?.Tick ?? 0;
        if (tick == _runTick)
        {
            return;
        }

        _runTick = tick;
        _playSound ??= Igi2LevelExpressions.Create(PlaySound, Name, level.Natives);
        if (!_playSound.IsEmpty && LevelExpression.Truncate(level.Evaluate(_playSound)) != 0)
        {
            // Train_SoundUpdateParameterListHandler: its sounds read its channels.
            _sound ??= Sounds?.New(_carriages != 0 ? "train_loop_1" : "train_loop_2", _at, () => _at, parameters: () => SoundChannels);
        }
        else
        {
            _sound?.Delete();
            _sound = null;
        }

        float thrust = Parent is Igi2TrainTask locomotive ? locomotive._realThrust : _realThrust;
        float level5 = ((Fpu)thrust * 5.0).F;
        float[] channels = (float[])SoundChannels;
        channels[0] = level5 >= 0.800000011920929f ? Math.Min(level5, 1f) : 0.800000011920929f;
        channels[1] = thrust > 0.0010000000474974513f ? 1f : 0f;
        level.RunChildren(this);

        // DeathZone_RunHandler: a carriage's kill boxes take its locomotive's speed, which moves them while it is not nought.
        float speed = Parent is Igi2TrainTask engine ? engine._speed : _speed;
        if (_deathZones.Count > 0 && speed != 0f)
        {
            Igi2Pose pose = Igi2Pose.FromMatrix(_at, Rows(_orientation));
            foreach (Igi2DeathZone zone in _deathZones)
            {
                zone.HitTest(pose, new Vector3f(speed, 0f, 0f), Humans);
            }
        }
    }

    /// <summary>Takes it out of the world, its sound with it, <c>Train_RemoveHandler</c>.</summary>
    public void Remove()
    {
        _sound?.Delete();
        _sound = null;
    }

    // Train_Move: the couplings walked along the spline, every step the locomotive's way, then each carriage set between its two; a
    // carriage whose locomotive was not yet in place is not either.
    private void Move(int flowTick)
    {
        if (_path is null || _rake.Length == 0)
        {
            return;
        }

        float[] steps = new float[_rake.Length];
        for (int i = 0; i < _rake.Length; i++)
        {
            steps[i] = FlipDirection ? _rake[i]._length : -_rake[i]._length;
        }

        if (!_path.TryWalk(_position, steps, out Vector3d[] points))
        {
            return;
        }

        bool wasValid = _isValidPosition;
        _rake[0].Place(points[0], points[1], flowTick);
        for (int i = 1; i < _rake.Length; i++)
        {
            _rake[i]._isValidPosition = _rake[i]._isValidPosition && wasValid;
            _rake[i].Place(points[i], points[i + 1], flowTick);
        }
    }

    // Train_MoveCarriage: turned to span its couplings, standing at the forward one and set off it across and up by its displacement;
    // where it stood at the tick's start kept for drawing between.
    private void Place(Vector3d front, Vector3d back, int flowTick)
    {
        if (flowTick != _movedTick)
        {
            _movedTick = flowTick;
            _lastAt = _at;
            _lastOrientation = [.. _orientation];
        }

        Matrix3f facing = Igi2Train.Facing(front, back);
        _at = Igi2Train.Offset(front, facing, DisplacementX, DisplacementZ);
        _orientation = [facing.M11, facing.M12, facing.M13, facing.M21, facing.M22, facing.M23, facing.M31, facing.M32, facing.M33];
        _isValidPosition = true;
        if (_solids.Count > 0 && Models is { } models)
        {
            Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(_at, facing), Magic);
        }
    }

    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
}
