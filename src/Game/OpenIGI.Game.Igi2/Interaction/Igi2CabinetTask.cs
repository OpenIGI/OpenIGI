using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Something that moves a part of its own drawn tree, rather than the whole of it: a cabinet's drawer.</summary>
public interface IIgi2PartMover
{
    /// <summary>Gets the level id of the task that placed it.</summary>
    int Id { get; }

    /// <summary>Gets where one of its parts is drawn now, when the part is one it moves.</summary>
    /// <param name="placement">The part as the walk placed it.</param>
    /// <param name="positionMetres">Receives where it is drawn, in metres.</param>
    /// <param name="orientation">Receives its orientation.</param>
    /// <returns>Whether it moves that part.</returns>
    bool TryMovePart(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation);
}

/// <summary>
/// A cabinet, I.G.I. 2's <c>Cabinet</c> (cabinet.c): an object turned by its heading that the player searches as a timed action from its
/// model's first magic vertex, while its drawer, the attach point of the magic type <c>Drawer</c>, slides out and back on the search clip's
/// triggers. The level's scripts follow it through <c>isInUse</c>, <c>isLastInUse</c> and <c>isSearched</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Cabinet_RunHandler</c>: its children run; a drawer on the move runs; it stays in use while it was searched at in the last two
/// ticks. <c>Drawer_RunHandler</c>: opening, the drawer is out by its full 0.2 m at once; closing, it is in at once; told to snap, it
/// snaps; either way it then stands still.
/// </para>
/// <para>
/// <c>Cabinet_EvaluateActivateHandler</c>: from its first magic vertex, to a view in front of it within three quarters of a metre to
/// either side. <c>Cabinet_ActivateHandler</c>: in use, as of this tick.
/// </para>
/// </remarks>
public sealed partial class Igi2CabinetTask : Igi2Task, IIgi2PlacedTask, IIgi2Cabinet, IIgi2PartMover
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Cabinet";

    /// <summary>The magic type of its drawer.</summary>
    public const string DrawerType = "Drawer";

    /// <summary>How far its drawer comes out, 0.2 m.</summary>
    public const float DrawerTravel = 819.2000122070312f;

    /// <summary>How far its drawer moves a tick, the original's constant, which brings it out or in at once.</summary>
    public const float DrawerSpeed = 671088.625f;

    private float[] _frame = EngineEuler.Rotation(0f, 0f, 0f);
    private bool _isSnapOpen;
    private bool _isSnapClosed;
    private bool _isDrawerMoving;
    private bool _isOpening;
    private bool _isClosing;
    private byte _isInUse;
    private byte _isLastInUse;
    private byte _isSearched;
    private int _lastInUseTick;
    private Igi2LevelFlowTask? _flow;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its heading.</summary>
    [ScriptParam(ScriptParamType.Angle, "Orientation")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the seconds a search takes.</summary>
    [ScriptParam(ScriptParamType.Real32, "Search Time (s)")]
    public float SearchTime { get; set; } = 3f;

    /// <summary>Gets or sets its activate sound; the search plays <c>tba_searchcab</c> whatever it says.</summary>
    [ScriptParam(ScriptParamType.String32, "Activate Sound")]
    public string ActivateSound { get; set; } = "tba_searchcab";

    /// <summary>Gets a value indicating whether it is searched at, <c>isInUse</c>.</summary>
    public bool IsInUse => _isInUse != 0;

    /// <summary>Gets a value indicating whether it has been searched, <c>isSearched</c>.</summary>
    public bool IsSearched => _isSearched != 0;

    /// <summary>Gets how far its drawer is out.</summary>
    public float DrawerOffset { get; private set; }

    /// <summary>Gets its drawer's model, or <see langword="null"/> when its model carries none.</summary>
    public string? DrawerModel { get; private set; }

    /// <summary>Gets its frame, row by row: a turn by its heading.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <summary>Gets or sets the level's models.</summary>
    [LevelService]
    public Func<string, MefModel?>? Models { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Models?.Invoke(Model)?.NativeMarkerBound ?? 0f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Cabinet;

    /// <summary>Gets its model's first magic vertex, where it is searched from.</summary>
    public Vector3d SearchVertex => Igi2MagicVertex.Position(Models?.Invoke(Model), 0, _frame, Position);

    /// <summary>Turns it by its heading and finds its drawer among its attach points, <c>Cabinet_UpdateInternalDataHandler</c>.</summary>
    /// <param name="magic">The magic object types, or <see langword="null"/>.</param>
    public void Prepare(Igi2MagicObjTypes? magic = null)
    {
        float sin = Fpu.Sin(Gamma).F, cos = Fpu.Cos(Gamma).F;
        _frame = [cos, -sin, 0f, sin, cos, 0f, 0f, 0f, 1f];
        DrawerModel = null;
        if (magic is not null && Models?.Invoke(Model) is { } model)
        {
            foreach (MefAttachment attachment in model.Attachments)
            {
                if (magic.Find(attachment.Name) is { TaskType: DrawerType } drawer)
                {
                    DrawerModel = drawer.Model;
                    break;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _flow = level.LevelFlow;
        level.RunChildren(this);
        if (DrawerModel is not null && _isDrawerMoving)
        {
            RunDrawer();
        }

        if ((_flow?.Tick ?? 0) > _lastInUseTick + 2)
        {
            _isInUse = 0;
        }

        _isLastInUse = _isInUse;
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.OfferFromFront(SearchVertex, _frame, 3072.0, this);
    }

    /// <inheritdoc/>
    public void Activate(int activateTime)
    {
        _isInUse = 1;
        _lastInUseTick = _flow?.Tick ?? 0;
    }

    /// <summary>Starts its drawer out, as the search clip's open-drawer trigger does.</summary>
    public void OpenDrawer()
    {
        _isOpening = true;
        _isClosing = false;
        _isDrawerMoving = true;
    }

    /// <summary>Starts its drawer in, as the search clip's close-drawer trigger does.</summary>
    public void CloseDrawer()
    {
        _isClosing = true;
        _isOpening = false;
        _isDrawerMoving = true;
    }

    /// <summary>Snaps its drawer shut, as a search let go of does.</summary>
    public void SnapDrawerClosed()
    {
        _isSnapClosed = true;
        _isDrawerMoving = true;
    }

    /// <summary>Marks it searched, as a search held to its end does.</summary>
    public void Searched() => _isSearched = 1;

    /// <inheritdoc/>
    public bool TryMovePart(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        orientation = placement.Orientation;
        if (DrawerModel is null || !string.Equals(placement.Instance.Name, DrawerModel, StringComparison.Ordinal))
        {
            positionMetres = default;
            return false;
        }

        Matrix3f o = placement.Orientation;
        double distance = DrawerOffset / WorldScene.UnitsPerMetre;
        positionMetres = new Vector3d(
            placement.Position.X + (o.M12 * distance),
            placement.Position.Y + (o.M22 * distance),
            placement.Position.Z + (o.M32 * distance));
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Gamma,
            "Model" => Model,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isInUse", () => _isInUse, value => _isInUse = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastInUse", () => _isLastInUse, value => _isLastInUse = (byte)value, sizeof(byte));
        variables.BindInt(this, "isSearched", () => _isSearched, value => _isSearched = (byte)value, sizeof(byte));
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    // Drawer_RunHandler.
    private void RunDrawer()
    {
        if (_isOpening)
        {
            DrawerOffset = ((Fpu)DrawerOffset + DrawerSpeed).F;
            if (DrawerOffset > DrawerTravel)
            {
                DrawerOffset = DrawerTravel;
                _isDrawerMoving = false;
            }
        }

        if (_isClosing)
        {
            DrawerOffset = ((Fpu)DrawerOffset - DrawerSpeed).F;
            if (DrawerOffset < 0f)
            {
                DrawerOffset = 0f;
                _isDrawerMoving = false;
            }
        }

        if (_isSnapClosed)
        {
            DrawerOffset = 0f;
            _isSnapClosed = false;
            _isDrawerMoving = false;
        }

        if (_isSnapOpen)
        {
            DrawerOffset = DrawerTravel;
            _isSnapOpen = false;
            _isDrawerMoving = false;
        }
    }
}
