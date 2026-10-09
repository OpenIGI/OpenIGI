using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Paths;

/// <summary>
/// What moves an object along a spline path, I.G.I. 2's <c>SplinePathGuideQTask</c>, exporting <c>vPosition</c> (segment and the
/// share of it run) and <c>vSpeed</c> (the speed's scale).
/// </summary>
/// <remarks>
/// <para>
/// Its tick (<c>SplinePathGuideQTask_RunHandler</c>, <c>igi2 0x5E67F0</c>): its speed is taken as the tick's scale, and its children
/// run with it as the guide <c>SplinePathGuide_SetRunning</c> starts and stops. Running, on a path of three nodes or more and with
/// a model to move, it steps the share of its segment by the unit length's share there times the speed between the segment's
/// nodes, in world units a tick, times the scale. Past the end of a segment it carries what is over into the next by the ratio of
/// their lengths; past the last, it starts again from the first when looping, else it stops at the end. The model is turned and
/// moved to where the curve then is, and the step it took is the guide's velocity.
/// </para>
/// <para>
/// Reading one (<c>SplinePathGuideQTask_UpdateInternalDataHandler</c>, <c>0x5E6650</c>) holds its start position to between nought
/// and one, puts it at that share of its path's whole length when it is past nought, sets it running if it runs from the start,
/// and takes its speed from its scale. It connects to its model once the level is loaded
/// (<c>SplinePathGuideQTask_UpdateInternalDataAfterLoadHandler</c>, <c>0x5E6700</c>).
/// </para>
/// </remarks>
public sealed partial class Igi2SplinePathGuideTask : GameTask, IIgi2RunTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SplinePathGuideQTask";

    /// <summary>The native that starts and stops the guide whose children are running.</summary>
    public const string SetRunningNative = "SplinePathGuide_SetRunning";

    /// <summary>The ticks a second paths are run at, <c>SplinePath_vFlowFrequency</c>, set by <c>Game_Open</c> (<c>igi2 0x409F1E</c>).</summary>
    public const float FlowFrequency = 30f;

    /// <summary>The flag that says it is running.</summary>
    public const int RunningFlag = 1;

    // The highest level id a guide looks a path or a model up by, exclusive.
    private const int LevelIds = 0x1000;

    private int _segment;
    private int _flags;
    private float _t;
    private float _speedScale = 1f;
    private double _speed = 1.0;
    private double _position;
    private Vector3f _velocity;

    /// <summary>Gets or sets the level id of the path it runs along.</summary>
    [ScriptParam(ScriptParamType.Int32, "Path ID")]
    public int PathId { get; set; } = -1;

    /// <summary>Gets or sets the level id of the model it moves.</summary>
    [ScriptParam(ScriptParamType.Int32, "RigidObject ID")]
    public int ModelId { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether it starts over from the path's start at its end.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Looping")]
    public bool IsLooping { get; set; }

    /// <summary>Gets or sets a value indicating whether it runs from the start.</summary>
    [ScriptParam(ScriptParamType.Bool8, "RunFromStart")]
    public bool IsRunFromStart { get; set; }

    /// <summary>Gets or sets the share of the path's whole length it starts at, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Startposition")]
    public float StartPosition { get; set; }

    /// <summary>Gets a value indicating whether it is running.</summary>
    public bool IsRunning => (_flags & RunningFlag) != 0;

    /// <summary>Gets the segment it is on, <c>nCurrentSegment</c>.</summary>
    public int Segment => _segment;

    /// <summary>Gets the share of its segment it has run, <c>vT</c>.</summary>
    public float T => _t;

    /// <summary>Gets the segment and the share of it run as one number, <c>vPosition</c>.</summary>
    public double PathPosition => _position;

    /// <summary>Gets the step its model took this tick, <c>tVelocity</c>; nought when it did not move.</summary>
    public Vector3f Velocity => _velocity;

    /// <summary>Gets or sets its speed's scale, <c>vSpeed</c>, which scripts may change.</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = value;
    }

    /// <summary>Registers <see cref="SetRunningNative"/>, which starts or stops the guide whose children are running.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    /// <remarks>
    /// <c>SplinePathGuideQTask_SetRunningScriptFunction</c> (<c>igi2 0x5E6570</c>): no argument, or one not nought, starts it; nought
    /// stops it. Outside a guide's children it does nothing.
    /// </remarks>
    public static void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        functions.Register(natives, SetRunningNative, static call =>
        {
            if (call.Context is Igi2SplinePathGuideTask guide)
            {
                guide.SetRunning(call.Count <= 0 || call.GetInt32(0) != 0);
            }

            return QvmValue.Zero;
        }, 1);
    }

    /// <summary>Starts or stops it.</summary>
    /// <param name="running">Whether it runs.</param>
    public void SetRunning(bool running) => _flags = running ? _flags | RunningFlag : _flags & ~RunningFlag;

    /// <summary>
    /// Tells its model it moves it, <c>SplinePathQuideQTask_ConnectObject</c> (<c>igi2 0x5E65E0</c>): a model id past nought that names
    /// something it can move is connected.
    /// </summary>
    /// <remarks>
    /// Retail forgets an id that names a task that is no <c>ModelObj</c>; here only the models a guide can move are known as such,
    /// so an id naming anything else is kept, and never moves anything.
    /// </remarks>
    public void Connect()
    {
        if (ModelId > 0 && ModelId < LevelIds && Tree.FromId(ModelId) is IIgi2PathRider rider)
        {
            rider.Connect(this);
        }
    }

    /// <inheritdoc/>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _speedScale = ((Fpu)_speed).F;
        level.Natives.SetSymbolContext(SetRunningNative, this);
        try
        {
            level.RunChildren(this);
        }
        finally
        {
            level.Natives.SetSymbolContext(SetRunningNative, null);
        }

        int flags = _flags;
        _velocity = default;
        if ((flags & RunningFlag) == 0
            || PathId < 0 || PathId >= LevelIds || Tree.FromId(PathId) is not Igi2SplinePathTask path
            || ModelId < 0 || Tree.FromId(ModelId) is not IIgi2PathRider rider)
        {
            return;
        }

        IReadOnlyList<Igi2SplinePathNodeTask> nodes = path.Nodes;
        int count = nodes.Count;
        if (_segment < 0 || _segment + 1 >= count || count < Igi2SplinePathTask.MinNodes)
        {
            return;
        }

        Advance(nodes, flags);
        float t = _t >= 1f || float.IsNaN(_t) ? 1f : _t;
        _position = ((Fpu)(double)_segment + t).Value;
        _t = t;

        IReadOnlyList<float> frame = path.Orientation(_segment, _t);
        Vector3d at = path.Position(_segment, _t);
        Vector3d from = rider.CurrentPosition;
        _velocity = new Vector3f(((Fpu)at.X - from.X).F, ((Fpu)at.Y - from.Y).F, ((Fpu)at.Z - from.Z).F);
        rider.Ride(at, frame, level.Ticks);
    }

    /// <inheritdoc/>
    /// <remarks><c>SplinePathGuideQTask_RegisterScriptSymbolsHandler</c> (<c>igi2 0x5E6C10</c>).</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindReal(this, "vPosition", () => _position, value => _position = value);
        variables.BindReal(this, "vSpeed", () => _speed, value => _speed = value);
    }

    /// <inheritdoc/>
    /// <remarks><c>SplinePathGuideQTask_UpdateInternalDataHandler</c> (<c>igi2 0x5E6650</c>).</remarks>
    protected override void OnParsed()
    {
        float start = StartPosition;
        StartPosition = start > 0f || start == 0f ? (start <= 1f ? start : 1f) : 0f;
        if (StartPosition > 0f)
        {
            PlaceAtStart();
        }

        SetRunning(IsRunFromStart);
        if (_speedScale <= 0f)
        {
            _speedScale = 0f;
            _speed = 0.0;
        }
        else
        {
            _speed = _speedScale;
        }
    }

    // The share of the segment run stepped on a tick, and carried into the next segment past its end.
    private void Advance(IReadOnlyList<Igi2SplinePathNodeTask> nodes, int flags)
    {
        Igi2SplinePathNodeTask node = nodes[_segment], next = nodes[_segment + 1];
        float speedFrom = node.Speed;
        Fpu perUnit = node.TPerUnit(_t);
        Fpu unitsPerTick = (Fpu)4096000.0 / ((Fpu)FlowFrequency * 3600.0);
        Fpu speed = (((Fpu)next.Speed - speedFrom) * _t) + speedFrom;
        Fpu t = (perUnit * ((unitsPerTick * speed) * _speedScale)) + _t;
        _t = t.F;
        if (t.Value <= 1.0 || double.IsNaN(t.Value))
        {
            return;
        }

        _t = (t - 1f).F;
        _segment++;
        _t = (((Fpu)node.DistanceToNext * _t) / next.DistanceToNext).F;
        if (_segment < nodes.Count - 1)
        {
            return;
        }

        if (IsLooping)
        {
            _t = 0f;
            _segment = 0;
        }
        else
        {
            _t = 1f;
            _segment = nodes.Count - 2;
            _flags = flags & ~RunningFlag;
        }
    }

    // SplinePathGuideQTask_PlaceAtStartPosition (igi2 0x5E6D60): the share of the whole length walked off segment by segment.
    private void PlaceAtStart()
    {
        if (PathId < 0 || PathId >= LevelIds || Tree.FromId(PathId) is not Igi2SplinePathTask path || path.Nodes.Count < Igi2SplinePathTask.MinNodes)
        {
            return;
        }

        IReadOnlyList<Igi2SplinePathNodeTask> nodes = path.Nodes;
        Fpu whole = 0f;
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            whole += nodes[i].DistanceToNext;
        }

        Fpu left = whole * StartPosition;
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            float length = nodes[i].DistanceToNext;
            if (!(left.Value > length))
            {
                Fpu share = left / length;
                _segment = i;
                _t = share.F;
                _position = ((Fpu)(double)i + share).Value;
                return;
            }

            left -= length;
        }
    }
}
