using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A switch or button the player presses, I.G.I. 2's <c>Switch</c>, exporting <c>isActive</c>, <c>isOn</c>, <c>isLastOn</c>,
/// <c>isPressed</c>, <c>isLastPressed</c>, <c>isDestroyed</c>, <c>isLastDestroyed</c> and <c>nClientID</c>.
/// </summary>
/// <remarks>
/// <para>
/// A press (<c>Switch_Pressed</c>, <c>igi2 0x4DB3E0</c>) holds it pressed and notes the level's tick.
/// <c>Switch_RunHandler</c> (<c>0x4DA760</c>): its children run, its <c>Active</c> script says whether it can be found;
/// three ticks after the press it lets go; the tick it is first held it flips on or off and sounds at itself; and it wears
/// the model its state names (<c>Switch_SetModel</c>, <c>0x4DA360</c>): on, on and pressed, off, off and pressed, destroyed.
/// </para>
/// <para>
/// A human's view finds it (<c>Switch_EvaluateActivateHandler</c>, <c>0x4DACE0</c>) while it is whole and active, from in
/// front of it — along its Y axis — and within 3072 units of its centre line across, measured from its model's first magic
/// vertex.
/// </para>
/// <para>
/// A round that strikes it (<c>Switch_WeaponHitHandler</c>, Project I.G.I.'s <c>0x445840</c> the same) destroys it when it is
/// <c>Destructable</c>, whichever game's round it is: the handler reads nothing of the hit. Nothing finds it after that, but
/// its run copies <c>isDestroyed</c> to <c>isLastDestroyed</c> before it sets its model, so it wears the destroyed one only
/// once its on or pressed state next changes. Project I.G.I.'s run (<c>0x4450F0</c>) copies after, and swaps at once.
/// </para>
/// <para>It is still placed by the world scene, which reads its parameters by name (<see cref="IIgi2PlacedTask"/>).</para>
/// </remarks>
public sealed partial class Igi2SwitchTask : Igi2Task, IIgi2Pressable, IIgi2PlacedTask, IWeaponHitTarget
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Switch";

    /// <summary>How far across its centre line it can be found, in units.</summary>
    public const double FindAcross = 3072.0;

    /// <summary>The ticks after a press it lets go on: it stays pressed while the level's tick is no more than this past it.</summary>
    public const int PressTicks = 3;

    /// <summary>The name a sound slot holds until the level gives it one, <c>"$!undefined!$"</c>.</summary>
    public const string UndefinedSound = "$!undefined!$";

    private LevelExpression? _active;
    private Igi2LevelFlowTask? _flow;
    private byte _isActive = 1;
    private byte _isOn;
    private byte _isLastOn;
    private byte _isPressed;
    private byte _isLastPressed;
    private byte _isDestroyed;
    private byte _isLastDestroyed;
    private int _lastPressedTick;

    /// <summary>Gets the tick it was last pressed on, <c>nLastPressedTick</c>.</summary>
    public int LastPressedTick => _lastPressedTick;
    private int _clientId;
    private bool _started;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way it faces, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that says whether it can be found.</summary>
    [ScriptParam(ScriptParamType.VarString, "Active")]
    public string ActiveScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it starts on.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Initial on")]
    public bool InitialOn { get; set; }

    /// <summary>Gets or sets the sound a press makes.</summary>
    [ScriptParam(ScriptParamType.String16, "Sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets its model while on.</summary>
    [ScriptParam(ScriptParamType.String16, "On model")]
    public string OnModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its model while on and held.</summary>
    [ScriptParam(ScriptParamType.String16, "On pressed model")]
    public string OnPressedModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its model while off.</summary>
    [ScriptParam(ScriptParamType.String16, "Off model")]
    public string OffModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its model while off and held.</summary>
    [ScriptParam(ScriptParamType.String16, "Off pressed model")]
    public string OffPressedModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its model once destroyed.</summary>
    [ScriptParam(ScriptParamType.String16, "Destroyed model")]
    public string DestroyedModel { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it can be destroyed.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Destructable")]
    public bool Destructable { get; set; }

    /// <summary>Gets or sets the action icon the status screen shows for it.</summary>
    [ScriptParam(ScriptParamType.DropDownCombo, "Click to select sprite")]
    public string ActionSprite { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it can be found, <c>isActive</c>.</summary>
    public bool IsActive => _isActive != 0;

    /// <summary>Gets a value indicating whether it is on, <c>isOn</c>.</summary>
    public bool IsOn => _isOn != 0;

    /// <summary>Gets a value indicating whether it is held pressed, <c>isPressed</c>.</summary>
    public bool IsPressed => _isPressed != 0;

    /// <summary>Gets a value indicating whether it is destroyed, <c>isDestroyed</c>.</summary>
    public bool IsDestroyed => _isDestroyed != 0;

    /// <summary>Gets the model slot it wears: 0 on, 1 on and pressed, 2 off, 3 off and pressed, 4 destroyed.</summary>
    public int ModelSlot { get; private set; }

    /// <summary>Gets the model it wears.</summary>
    public string Model => ModelSlot switch
    {
        0 => OnModel,
        1 => OnPressedModel,
        2 => OffModel,
        3 => OffPressedModel,
        _ => DestroyedModel,
    };

    /// <summary>Gets or sets the level's models, which its magic vertex and its radius are read from.</summary>
    [LevelService]
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the level's sounds a press sounds through, or <see langword="null"/> for none.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    /// <remarks>Its model's radius, as <c>World_RegisterDynCubeObjFirstTimeRadius</c> is given it by <c>Switch_CreateHandler</c> (<c>igi2 0x4DA480</c>).</remarks>
    public float Radius => Models?.Invoke(Model)?.NativeMarkerBound ?? 1f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Switch;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Start(level);
        level.RunChildren(this);
        if (!_active!.IsEmpty)
        {
            _isActive = (byte)LevelExpression.Truncate(level.Evaluate(_active));
        }

        bool wasOn = IsOn;
        bool wasPressed = _isLastPressed != 0;
        bool wasDestroyed = IsDestroyed;
        _isLastDestroyed = _isDestroyed;
        _isLastOn = _isOn;
        if ((_flow?.Tick ?? 0) > _lastPressedTick + PressTicks)
        {
            _isPressed = 0;
        }

        if (IsPressed && _isLastPressed == 0)
        {
            _isOn = IsOn ? (byte)0 : (byte)1;
            Sounds?.New(Sound, Position);
        }

        if (IsOn != wasOn || IsPressed != wasPressed || IsDestroyed != wasDestroyed)
        {
            ModelSlot = SlotFor(IsOn, IsPressed, IsDestroyed);
        }

        _isLastPressed = _isPressed;
    }

    /// <summary>Gets or sets what else is told as the switch is pressed: another game's trigger it stands for, for one.</summary>
    public Action? Pressed { get; set; }

    /// <inheritdoc/>
    public void Press(int clientId)
    {
        _isPressed = 1;
        _lastPressedTick = _flow?.Tick ?? 0;
        _clientId = clientId;
        Pressed?.Invoke();
    }

    /// <inheritdoc/>
    public void WeaponHit(in WeaponHit hit)
    {
        if (Destructable)
        {
            _isDestroyed = 1;
        }
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsDestroyed || !IsActive)
        {
            return;
        }

        float[] m = Frame();
        Vector3d vertex = Igi2MagicVertex.Position(Models?.Invoke(Model), 0, m, Position);
        Fpu dx = (Fpu)context.ViewPosition.X - vertex.X;
        Fpu dy = (Fpu)context.ViewPosition.Y - vertex.Y;
        Fpu dz = (Fpu)context.ViewPosition.Z - vertex.Z;
        Fpu across = (((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx);
        Fpu ahead = (((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx);
        if (ahead.Value > 0.0 && Math.Abs(across.Value) < FindAcross)
        {
            context.Resolve(vertex, this);
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Active" => ActiveScript,
            "Initial on" => InitialOn,
            "Sound" => Sound,
            "On model" => OnModel,
            "On pressed model" => OnPressedModel,
            "Off model" => OffModel,
            "Off pressed model" => OffPressedModel,
            "Destroyed model" => DestroyedModel,
            "Destructable" => Destructable,
            "Click to select sprite" => ActionSprite,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    /// <remarks><c>Switch_RegisterScriptSymbolsHandler</c> (<c>igi2 0x4DAE00</c>): bytes and an integer, all writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isActive", () => _isActive, value => _isActive = (byte)value, sizeof(byte));
        variables.BindInt(this, "isOn", () => _isOn, value => _isOn = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastOn", () => _isLastOn, value => _isLastOn = (byte)value, sizeof(byte));
        variables.BindInt(this, "isPressed", () => _isPressed, value => _isPressed = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastPressed", () => _isLastPressed, value => _isLastPressed = (byte)value, sizeof(byte));
        variables.BindInt(this, "isDestroyed", () => _isDestroyed, value => _isDestroyed = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastDestroyed", () => _isLastDestroyed, value => _isLastDestroyed = (byte)value, sizeof(byte));
        variables.BindInt(this, "nClientID", () => _clientId, value => _clientId = value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Switch_UpdateInternalDataHandler</c> (<c>igi2 0x4DA9C0</c>): it starts as its <c>Initial on</c> says, wearing the
    /// matching model, and a sound left undefined becomes a lever's click or a button's.
    /// </remarks>
    protected override void OnParsed()
    {
        _isOn = InitialOn ? (byte)1 : (byte)0;
        _isLastOn = _isOn;
        ModelSlot = IsOn ? 0 : 2;
        if (Sound.StartsWith("$!un", StringComparison.Ordinal))
        {
            Sound = OnModel is "360_14_1" or "360_15_1" ? "switch_1" : "button_1";
        }
    }

    private static int SlotFor(bool on, bool pressed, bool destroyed) =>
        destroyed ? 4 : on ? (pressed ? 1 : 0) : (pressed ? 3 : 2);

    private void Start(Igi2LevelRun level)
    {
        if (_started)
        {
            return;
        }

        _active = Igi2LevelExpressions.Create(ActiveScript, Name, level.Natives);
        _flow = level.LevelFlow;
        _started = true;
    }

    private float[] Frame() => Igi2Pose.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
}
