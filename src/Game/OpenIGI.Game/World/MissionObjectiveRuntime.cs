using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>The retail IGI1 objective definitions and LevelFlow result for one loaded mission.</summary>
/// <remarks>
/// IGI1 keeps these as two separate task types. <c>DefineComputerObjective</c> owns the six rows shown
/// by the map computer; <c>LevelFlow</c> alone decides whether the level has completed or failed. A
/// level may contain several objective definitions and switch between them with <c>Objectives Valid</c>.
/// The last valid definition in task-update order is the one the original publishes globally.
/// </remarks>
public sealed class MissionObjectiveRuntime : IComputerObjectives
{
    /// <summary>The maximum number of objective rows in one IGI1 definition.</summary>
    public const int ObjectiveSlotCount = 6;

    /// <summary>The number of status messages IGI1 can have on screen at once.</summary>
    /// <remarks>The slot array <c>0x486650</c> searches, at a stride of 4140 bytes.</remarks>
    public const int MessageSlotCount = 24;

    private readonly GameTask _levelRoot;
    private readonly ScriptVarRegistry _variables;
    private readonly LevelExpressionRuntime _expressions;
    private readonly Func<string, string> _resolveText;
    private readonly List<Definition> _definitions = [];
    private readonly MissionAreaActivation _areas;
    private readonly List<EditVariable> _editVariables = [];
    private readonly MissionStatusMessages _messages;
    private readonly List<LevelFlow> _levelFlows = [];

    /// <summary>
    /// <c>Key0.isPressed</c> to <c>Key9.isPressed</c>: the globals <c>byte_5C8AA8</c>..<c>byte_5C8AB1</c>,
    /// indexed by digit, holding the raw DirectInput state byte (<c>0x80</c> when down).
    /// </summary>
    private readonly sbyte[] _digitKeys = new sbyte[10];
    private readonly Func<KeyCode, bool>? _isKeyDown;

    /// <summary>The LevelFlow the original's <c>flt_BC2360</c> points at: the last one initialised.</summary>
    private readonly LevelFlow? _levelFlow;
    private bool _attached;
    private int _tick;
    private bool _released;

    /// <summary>Plays a message's <c>Sound</c> as it goes up, through the engine's direct-play path.</summary>
    /// <remarks>
    /// <c>0x486650</c>, having found the message a slot, calls <c>0x4E7200(Sound, 1, {1, 1, 0})</c> when the field
    /// (<c>+436</c>) is not empty: no position, unit gain and pitch. Every level authors <c>message</c> on its
    /// radio transmissions and <c>fail</c> on its mission-failed line.
    /// </remarks>
    public Action<string>? MessageSound
    {
        get => _messages.MessageSound;
        set => _messages.MessageSound = value;
    }

    /// <summary>Builds the runtime from the authored level task tree.</summary>
    /// <param name="levelRoot">The loaded level's task tree.</param>
    /// <param name="variables">The mission's script variables.</param>
    /// <param name="resolveText">Resolves a message resource to its text.</param>
    /// <param name="native">Answers native calls made from level expressions.</param>
    /// <param name="difficulty">
    /// The difficulty LevelFlow exports as <c>eDifficulty</c>: the active profile's field <c>+0</c>, or 1
    /// when there is no profile, which is what <c>0x488ED0</c> falls back to.
    /// </param>
    /// <param name="isKeyDown">Reads the keyboard for the <c>Key0..9.isPressed</c> globals; without it they read up.</param>
    public MissionObjectiveRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, string>? resolveText = null,
        Func<string, ScriptArguments, QvmValue?>? native = null,
        int difficulty = 1,
        Func<KeyCode, bool>? isKeyDown = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        _levelRoot = levelRoot;
        _variables = variables;
        _expressions = new LevelExpressionRuntime(variables, native);
        _resolveText = resolveText ?? (static key => key);
        _isKeyDown = isKeyDown;
        _areas = new MissionAreaActivation(levelRoot);
        _messages = new MissionStatusMessages(_expressions, _resolveText, () => _tick);

        List<GameTask> tasks = levelRoot.Descendants().ToList();

        foreach (GameTask task in tasks)
        {
            // isDead is NOT bound here. HumanTask exports it, and eTeam beside it, the way the
            // original exports both on the shared Human class rather than per subclass -- see
            // HumanTask.OnExportVars. What this runtime still owns is feeding the latch: the host
            // reports occupants and player state, and SetSoldierOccupants and TickLevelState write
            // through to the tasks so the binding reads a live value.
            if (task is not DeclaredTask declared)
            {
                continue;
            }

            if (string.Equals(task.Type.Name, "DefineComputerObjective", StringComparison.Ordinal))
            {
                _definitions.Add(ReadDefinition(declared));
            }
            else if (string.Equals(task.Type.Name, "AreaActivate", StringComparison.Ordinal))
            {
                AreaActivation area = new(
                    declared,
                    Vector(declared, "Position"),
                    Orientation(declared),
                    Vector32(declared, "Dimensions"),
                    TextTrimmed(declared, "Criteria"));
                _areas.Declare(declared, area, variables);
            }
            else if (string.Equals(task.Type.Name, "EditVariable", StringComparison.Ordinal))
            {
                EditVariable edit = new(
                    declared,
                    IntStrict(declared, "Initial value", 0),
                    TextTrimmed(declared, "Add"),
                    TextTrimmed(declared, "Sub"));
                _editVariables.Add(edit);
                variables.BindInt(declared, "nValue", () => edit.Value, value => edit.Value = value);
            }
            else if (string.Equals(task.Type.Name, "LevelFlow", StringComparison.Ordinal))
            {
                // Level 14 sends MISSION_FAILED on LevelFlow_10.isFailed, and levels 6 and 14 time
                // messages off LevelFlow_10.nTick, so these fields have to be exported, not just computed.
                LevelFlow flow = new(
                    declared,
                    TextTrimmed(declared, "Complete"),
                    TextTrimmed(declared, "Failed"),
                    BoolLenient(declared, "Interface timer enabled"),
                    Math.Max(0f, RealFromFloatOrDouble(declared, "Max level play time", 0f)),
                    difficulty);
                flow.Export(variables, _digitKeys);
                _levelFlows.Add(flow);
                _levelFlow = flow;
            }
            else if (string.Equals(task.Type.Name, "StatusMessage", StringComparison.Ordinal))
            {
                StatusMessage message = new(
                    declared,
                    TextTrimmed(declared, "Send"),
                    TextTrimmed(declared, "Text"),
                    BoolLenient(declared, "Is send once"),
                    BoolLenient(declared, "Cutscene message"),
                    RealFromFloatOrDouble(declared, "Duration", 2f))
                {
                    Sound = TextTrimmed(declared, "Sound"),
                };
                _messages.Declare(declared, message, variables);
            }
        }

        // Only the six objective rows are published here, and nothing else runs. The rows are drawn from
        // the moment the level is up -- the mission-entry shot and the withheld frames both come before
        // the first simulation tick -- so they have to exist before one; every other part of a tick has
        // to WAIT for one.
        //
        // Priming the rest was a live bug. A tick evaluates each StatusMessage's Send expression and
        // latches the one-shots that answer true, and at construction there is no world for those
        // expressions to read: the host builds this runtime while it is loading the level and does not
        // stand the player up until much later, so every AreaActivate reads zero occupants. Retail has no
        // such moment. AreaActivate counts the player by resolving the level's task 0 and testing its
        // root position against the volume (HumanPlayer_AreaActivateCB, igi2 0x49E290), and that task
        // owns its object from the moment the level tree is built -- an update can never see the volume
        // the player is standing in as empty.
        //
        // What it cost: location1/level1 sends LEFT_AREA on '!AreaActivate_178.nActive', around the
        // area the player spawns in, once. Primed with no player it sent on the spot, and its LevelFlow
        // fails the level on 'StatusMessage_2906.isFinishedDisplay' -- so the mission ended about six
        // seconds in, as soon as a message nobody could have earned finished being displayed.
        Objectives = [];
        PublishObjectives();
    }

    /// <summary>Gets the currently selected six-row definition, with empty rows omitted.</summary>
    public IReadOnlyList<MissionObjective> Objectives { get; private set; }

    /// <summary>Gets or sets extra seconds every status message is held on screen for.</summary>
    /// <remarks>
    /// <para>
    /// Added to each message's authored <c>Duration</c>. The authored values are short — two seconds is
    /// the common case, and the hold does not begin until the last character has been typed, so a long
    /// message can be fully readable for under a second before it goes.
    /// </para>
    /// <para>
    /// <b>This is not free of consequences, which is why it is a setting and defaults to zero.</b> The
    /// hold ending is what raises <c>isFinishedDisplay</c> and starts <c>nTicksSinceFinishedDisplay</c>,
    /// and mission scripts gate on both — several LevelFlow expressions wait for a message to finish
    /// before they complete a level. Extending the hold therefore delays those triggers by the same
    /// amount. A delay is all it is: nothing is skipped and no expression changes its answer, the mission
    /// simply reaches it a few seconds later. Left at zero the timing is the original's exactly.
    /// </para>
    /// </remarks>
    public float ExtraMessageHoldSeconds { get; set; }

    /// <summary>Gets or sets what reads the level's other players, a co-op session's, whom the areas count as they count the player.</summary>
    public Func<IEnumerable<AreaPlayer>>? PartnerPlayers
    {
        get => _areas.Partners;
        set => _areas.Partners = value;
    }

    /// <summary>Gets authored, non-cutscene status messages recorded by IGI1's ComputerLog.</summary>
    public IReadOnlyList<MissionComputerLogEntry> Log => _messages.Log;

    /// <summary>Gets how many authored status-message tasks the mission contains.</summary>
    public int StatusMessageCount => _messages.Count;

    /// <summary>Shows a non-scripted retail-style status message in the next free top-left slot.</summary>
    /// <param name="text">The already formatted text to draw.</param>
    /// <param name="durationSeconds">How long the text holds after it has typed out.</param>
    /// <param name="portrait">The sprite drawn beside it, by its name in the status archive, or empty.</param>
    /// <returns><see langword="true"/> when a display slot was available.</returns>
    /// <remarks>
    /// <para>
    /// Some game events create a <c>StatusMessageScreen</c> child directly instead of running an authored
    /// <c>StatusMessage</c> task. GunPickup does exactly that at <c>0x46D3A7</c>, formatting
    /// <c>PICKEDUPWEAPON%s</c> from the weapon's display name and posting it through <c>0x485970</c>.
    /// These messages share the same twenty-four slots and display state as mission messages but do not
    /// alter script variables or the map-computer log.
    /// </para>
    /// </remarks>
    public bool ShowTransientMessage(string text, float durationSeconds = 2f, string portrait = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

        return _messages.ShowTransient(text, durationSeconds, portrait);
    }

    /// <summary>Gets the messages on screen now, in the slot order the original draws them in.</summary>
    /// <remarks>
    /// <para>
    /// Slot order, not send order, because that is what the player sees. <c>0x486650</c> puts a new
    /// message in the <em>lowest free</em> slot and <c>0x486020</c> walks the array from zero, so when a
    /// message in the middle of the column finishes, the next one to arrive drops into the hole it left
    /// rather than onto the bottom. Sorting by send tick instead would be tidier and would not match.
    /// </para>
    /// <para>
    /// Empty on almost every tick of a mission, so it is a lazily-walked view over the slot array rather
    /// than a list rebuilt thirty times a second for a renderer that usually draws nothing.
    /// </para>
    /// </remarks>
    public IEnumerable<StatusMessageDisplay> DisplayedMessages => _messages.DisplayedMessages;

    /// <summary>Empties every message slot, as the <c>StatusMessageScreen_ClearMessages</c> native does.</summary>
    /// <remarks>
    /// <para>
    /// The native (<c>0x485AD0</c>) walks all twenty-four slots through <c>0x4868D0</c> and releases each
    /// occupied one with <c>0x485D20</c>, ordinary messages and cutscene subtitles alike. Levels 1-14 run
    /// it from their gameplay container's <c>Run at start</c>, so the intro's messages do not carry over
    /// into play.
    /// </para>
    /// <para>
    /// Releasing a slot is not silent: <c>0x485D20</c> marks the owning StatusMessage task as finished
    /// (<c>isFinishedDisplay</c> = 1, <c>nFinishedDisplay</c> = now, <c>nTicksSinceFinishedDisplay</c> = 0),
    /// exactly as if its hold had run out. Scripts that wait on a cleared message still see it end.
    /// Messages without a task, such as weapon pickups, just leave the screen.
    /// </para>
    /// </remarks>
    public void ClearMessages() => _messages.ClearMessages();

    /// <summary>Removes subtitles that belong to a cutscene which the player skipped.</summary>
    /// <remarks>
    /// Cutscene messages share the same slots as ordinary status messages. Releasing them here prevents
    /// a subtitle from surviving the camera hand-off and being drawn as a normal gameplay message.
    /// </remarks>
    public void ClearCutsceneMessages() => _messages.ClearCutsceneMessages();

    /// <summary>Gets how many authored objective definitions the mission contains.</summary>
    public int DefinitionCount => _definitions.Count;

    /// <summary>Gets whether the level declares the LevelFlow task that owns mission success and failure.</summary>
    public bool HasLevelFlow => _levelFlow is not null;

    /// <summary>Gets whether LevelFlow asks StatusScreen to show its countdown.</summary>
    public bool InterfaceTimerEnabled => _levelFlow?.InterfaceTimerEnabled ?? false;

    /// <summary>Gets the authored maximum play time in seconds.</summary>
    public float MaximumLevelPlayTimeSeconds => _levelFlow?.MaximumLevelPlayTimeSeconds ?? 0f;

    /// <summary>Gets the retail countdown in 30 Hz ticks, or no value when the interface timer is disabled.</summary>
    /// <remarks>
    /// <c>sub_4894B0</c> computes <c>max(0, MaxLevelPlayTime*30 - LevelFlowTick)</c>. Keeping ticks here
    /// avoids introducing wall-clock drift and lets StatusScreen perform the original whole-second divide.
    /// </remarks>
    public int? InterfaceTimerRemainingTicks
    {
        get
        {
            if (_levelFlow is not { InterfaceTimerEnabled: true } flow)
            {
                return null;
            }

            int maximumTicks = flow.MaximumLevelPlayTimeSeconds >= int.MaxValue / 30f
                ? int.MaxValue
                : (int)MathF.Round(flow.MaximumLevelPlayTimeSeconds * 30f, MidpointRounding.ToEven);
            return Math.Max(0, maximumTicks - flow.Tick);
        }
    }

    /// <summary>Gets LevelFlow's <c>isComplete</c> byte, which a failure clears on the same update.</summary>
    public bool IsComplete => _levelFlow is { Complete: not 0 };

    /// <summary>
    /// Gets the level's tick, LevelFlow's <c>nTick</c> (<c>+104</c>), which the level's tasks stamp their pulses with; <see langword="null"/>
    /// for a level with no LevelFlow.
    /// </summary>
    public int? LevelTick => _levelFlow?.Tick;

    /// <summary>Gets LevelFlow's <c>isFailed</c> byte, the one <c>LevelFlow_LevelFailed</c> (<c>0x488E50</c>) returns.</summary>
    public bool IsFailed => _levelFlow is { Failed: not 0 };

    /// <summary>Releases the script fields this runtime adds to the loaded mission.</summary>
    /// <remarks>
    /// Each LevelFlow removes exactly what its export added, as the original's unexport handler
    /// (<c>0x4890F0</c>) does, rather than every binding its task carries.
    /// </remarks>
    public void Release()
    {
        if (_released)
        {
            return;
        }

        foreach (LevelFlow flow in _levelFlows)
        {
            flow.Unexport(_variables);
        }

        _released = true;
    }

    /// <summary>Evaluates the authored definitions once, after the rest of the mission's tick.</summary>
    public void Tick(Vector3d? playerPosition = null)
    {
        TickLevelState(playerPosition);
        TickAfterLevelState();
    }

    /// <summary>Advances area and edit-variable tasks before dependent alarm controls run.</summary>
    /// <param name="playerPosition">The player's current world position, when one exists.</param>
    /// <param name="playerTeam">The player's team, for criteria that name a team (CRITERIA_HUMAN0).</param>
    public void TickLevelState(Vector3d? playerPosition = null, int playerTeam = 0) =>
        TickLevelState(playerPosition, playerTeam, extraPlayerPosition: null, playerAlive: true);

    /// <summary>Advances level state with alternate player projection and liveness information.</summary>
    /// <param name="playerPosition">The player's primary world projection, when one exists.</param>
    /// <param name="playerTeam">The player's team, for criteria that name a team (CRITERIA_HUMAN0).</param>
    /// <param name="extraPlayerPosition">
    /// An alternate projection of the same logical player in world units. The two positions contribute
    /// at most one player to each area. The host chooses projections according to its camera/body policy.
    /// </param>
    /// <param name="playerAlive">
    /// Whether the logical player can occupy authored areas. A dead player contributes no occupancy,
    /// matching the retail human-registration scan used by <c>AreaActivate</c>.
    /// </param>
    /// <remarks>
    /// These tasks precede the mission's <c>AlarmControl</c>s in retail update order. Keeping this phase
    /// separate lets the desktop host preserve that ordering while evaluating the remaining objectives
    /// and messages after the rest of the simulation tick.
    ///
    /// AreaActivate::Update (0x5048A0) clears nActive, queries the authored oriented volume and sums
    /// matching occupants. The volume's criteria names whose occupants count: a team (CRITERIA_HUMAN0
    /// counts team 0 — the player and the friendly squad — while CRITERIA_HUMAN1 counts team 1, the
    /// guards) or the player object itself. Dead humans are not occupants, exactly as the detection scan
    /// unregisters a corpse. The renderer reports every brain to preserve <c>isDead</c> bindings, so this
    /// query also excludes soldier tasks outside the live task world, just as the collision-body pass does.
    /// </remarks>
    public void TickLevelState(
        Vector3d? playerPosition,
        int playerTeam,
        Vector3d? extraPlayerPosition,
        bool playerAlive = true)
    {
        SetLevelStateInputs(playerPosition, playerTeam, extraPlayerPosition, playerAlive);
        if (_attached)
        {
            // The level walk updates them where the tree places them; running them here too would step each
            // edit variable twice in one tick.
            return;
        }

        _areas.UpdateAll();

        foreach (EditVariable edit in _editVariables)
        {
            UpdateEdit(edit);
        }
    }

    /// <summary>
    /// Puts the areas and edit variables into the level's update walk, so each updates where the tree places it
    /// (<c>0x5048A0</c> counts, then walks its children; <c>0x4889B0</c>).
    /// </summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="vehicles">Reads the vehicles' live positions when an area counts them.</param>
    /// <param name="soldiers">Reads the soldiers where they stand when an area counts them.</param>
    /// <param name="player">Reads the player where it stands when an area counts it.</param>
    /// <remarks>
    /// An area scans the registered humans and vehicles at the moment it updates (<c>0x5048A0</c>), so it sees
    /// what the tasks before it in the tree did this tick and what those after it did last tick. Without a
    /// source the host's last report stands in (<see cref="SetLevelStateInputs"/>,
    /// <see cref="SetSoldierOccupants"/>).
    /// </remarks>
    public void AttachTo(
        LevelUpdateWalker walker,
        Func<IEnumerable<VehicleOccupant>>? vehicles = null,
        Func<IEnumerable<HumanOccupant>>? soldiers = null,
        Func<AreaPlayer>? player = null)
    {
        ArgumentNullException.ThrowIfNull(walker);
        _attached = true;
        _areas.AttachTo(walker, vehicles, soldiers, player);

        foreach (EditVariable edit in _editVariables)
        {
            walker.Bind(edit.Task, task =>
            {
                UpdateEdit(edit);
                walker.RunChildren(task);
            });
        }

        _messages.AttachTo(walker);
    }

    /// <summary>Latches the player's projections and liveness for the areas that count them this tick.</summary>
    /// <param name="playerPosition">The player's primary world projection, when one exists.</param>
    /// <param name="playerTeam">The player's team.</param>
    /// <param name="extraPlayerPosition">An alternate projection of the same player.</param>
    /// <param name="playerAlive">Whether the player can occupy areas.</param>
    public void SetLevelStateInputs(Vector3d? playerPosition, int playerTeam, Vector3d? extraPlayerPosition, bool playerAlive)
    {
        _areas.SetLevelStateInputs(playerPosition, playerTeam, extraPlayerPosition, playerAlive);
    }

    /// <summary>EditVariable::Update (<c>0x4889B0</c>): Add first, Sub second, every tick.</summary>
    private void UpdateEdit(EditVariable edit)
    {
        if (!string.IsNullOrEmpty(edit.AddExpression))
        {
            edit.Value += _expressions.EvaluateInt32(edit.AddExpression, owner: edit.Task);
        }

        if (!string.IsNullOrEmpty(edit.SubExpression))
        {
            edit.Value -= _expressions.EvaluateInt32(edit.SubExpression, owner: edit.Task);
        }
    }

    /// <summary>Feeds the per-tick state of every soldier the host is simulating.</summary>
    /// <param name="occupants">One entry per placed soldier, with its current position, team and death.</param>
    /// <remarks>
    /// The mission runtime is built from the level's task tree before the soldiers exist, so the script
    /// variables it binds (<c>isDead</c>) and the <c>AreaActivate</c> volume counts both read from this
    /// snapshot. Every reported occupant remains available to the death bindings, while AreaActivate
    /// independently projects the task through <see cref="ConditionalContainerGate.IsTaskActive"/> before
    /// counting it. The host calls this on the same tick it feeds the player position, so area occupancy
    /// and death gates never lag a full frame behind the simulation.
    /// </remarks>
    public void SetSoldierOccupants(IEnumerable<HumanOccupant> occupants)
    {
        ArgumentNullException.ThrowIfNull(occupants);
        _areas.SetSoldierOccupants(occupants);
    }

    /// <summary>Feeds the live positions of graph-driven vehicles into vehicle-only areas.</summary>
    public void SetVehicleOccupants(IEnumerable<VehicleOccupant> occupants)
    {
        ArgumentNullException.ThrowIfNull(occupants);
        _areas.SetVehicleOccupants(occupants);
    }

    /// <summary>Evaluates objectives, messages, cutscenes, and level flow after live level state has advanced.</summary>
    public void TickAfterLevelState()
    {
        PublishObjectives();

        _messages.Tick(ExtraMessageHoldSeconds);

        // LevelFlow::Update (0x489290) updates its child tasks before it writes its own result, so the
        // messages above see the result of the tick before this one -- as Level 14's MISSION_FAILED does.
        foreach (LevelFlow flow in _levelFlows)
        {
            flow.Update(_expressions);
            SampleDigitKeys();
        }

        _tick++;
    }

    /// <summary>Samples the number row the way the end of <c>0x489290</c> does.</summary>
    /// <remarks>
    /// Each global is the DirectInput state byte masked with <c>0x80</c>: <c>Key1</c>..<c>Key9</c> are
    /// DIK_1..DIK_9 and <c>Key0</c> is DIK_0, which the port's <see cref="KeyCode"/> numbers identically.
    /// The byte is read back sign-extended (<c>0x4B7F00</c>), so a held key reads -128.
    /// </remarks>
    private void SampleDigitKeys()
    {
        for (int digit = 0; digit < _digitKeys.Length; digit++)
        {
            KeyCode key = digit == 0 ? KeyCode.D0 : (KeyCode)((int)KeyCode.D1 + digit - 1);
            _digitKeys[digit] = _isKeyDown?.Invoke(key) == true ? unchecked((sbyte)0x80) : (sbyte)0;
        }
    }

    // The definition that ran last, the computer's (gObjective).
    private Definition? _current;

    /// <summary>Runs the objective definitions and publishes the rows of the one that ran last.</summary>
    /// <remarks>
    /// <c>0x467410</c>: a definition whose <c>Objectives Valid</c> is empty or holds becomes the computer's, and each of its
    /// rows takes its <c>Complete</c> and <c>Failed</c> flags from its expressions -- off where a row has none. One that does
    /// not hold leaves everything as it was, so the computer keeps the last that ran, flags and all. Running it at
    /// construction, before the host has a player to report, latches the level's opening state.
    /// </remarks>
    private void PublishObjectives()
    {
        foreach (Definition definition in _definitions)
        {
            if (!_expressions.EvaluateCondition(definition.ValidExpression, emptyValue: true, definition.Task))
            {
                continue;
            }

            _current = definition;
            foreach (Slot slot in definition.Slots)
            {
                slot.IsComplete = _expressions.EvaluateCondition(slot.CompleteExpression, emptyValue: false, definition.Task);
                slot.IsFailed = _expressions.EvaluateCondition(slot.FailedExpression, emptyValue: false, definition.Task);
            }
        }

        List<MissionObjective> current = [];
        if (_current is { } active)
        {
            for (int index = 0; index < active.Slots.Count; index++)
            {
                Slot slot = active.Slots[index];
                if (string.IsNullOrEmpty(slot.TextResource))
                {
                    continue;
                }

                MissionObjectiveState state = slot.IsFailed ? MissionObjectiveState.Failed
                    : slot.IsComplete ? MissionObjectiveState.Complete
                    : MissionObjectiveState.Incomplete;
                Vector3d? position = slot.LinkTaskId >= 0 && _levelRoot.Tree.FromId(slot.LinkTaskId) is not null
                    ? FindPosition(_levelRoot.Tree.FromId(slot.LinkTaskId)!)
                    : null;
                current.Add(new MissionObjective(slot.TextResource, slot.LinkTaskId, position, state)
                {
                    Text = _resolveText(slot.TextResource),
                    Slot = index,
                });
            }
        }

        Objectives = current;
    }

    private static Definition ReadDefinition(DeclaredTask task)
    {
        List<Slot> slots = [];
        for (int number = 1; number <= ObjectiveSlotCount; number++)
        {
            slots.Add(new Slot(
                TextTrimmed(task, $"Objective {number} Text Resource"),
                IntStrict(task, $"Objective {number} Link To Position", -1),
                TextTrimmed(task, $"Objective {number} Complete Expression"),
                TextTrimmed(task, $"Objective {number} Failed Expression")));
        }

        return new Definition(task, TextTrimmed(task, "Objectives Valid"), slots);
    }

    private static Matrix3f Orientation(DeclaredTask task) =>
        task.TryGetValue("Orientation", out object? value) && value is Vector3f angles
            ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
            : Matrix3f.Identity;

    private static Vector3d FindPosition(GameTask task)
    {
        if (task is DeclaredTask declared)
        {
            if (declared.TryGetValue("Graph position", out object? graph) && graph is Vector3d graphPosition)
            {
                return graphPosition;
            }

            if (declared.TryGetValue("Position", out object? raw) && raw is Vector3d position)
            {
                return position;
            }
        }

        return default;
    }

    private sealed record Definition(
        DeclaredTask Task,
        string ValidExpression,
        IReadOnlyList<Slot> Slots);
    private sealed record Slot(string TextResource, int LinkTaskId, string CompleteExpression, string FailedExpression)
    {
        // The flags the row's definition last left it with (+0x42, +0x43).
        public bool IsComplete { get; set; }

        public bool IsFailed { get; set; }
    }

    /// <summary>One LevelFlow task's state: the fields the original keeps in the task at <c>+96</c>..<c>+104</c>.</summary>
    private sealed class LevelFlow(
        DeclaredTask task,
        string completeExpression,
        string failedExpression,
        bool interfaceTimerEnabled,
        float maximumLevelPlayTimeSeconds,
        int difficulty)
    {
        public DeclaredTask Task { get; } = task;
        public string CompleteExpression { get; } = completeExpression;
        public string FailedExpression { get; } = failedExpression;
        public bool InterfaceTimerEnabled { get; } = interfaceTimerEnabled;
        public float MaximumLevelPlayTimeSeconds { get; } = maximumLevelPlayTimeSeconds;

        // 0x488ED0 zeroes the result bytes and nTick, and copies the active profile's difficulty.

        /// <summary><c>+96</c>, <c>isComplete</c>: a byte, read back sign-extended.</summary>
        public sbyte Complete { get; set; }

        /// <summary><c>+97</c>, <c>isFailed</c>.</summary>
        public sbyte Failed { get; set; }

        /// <summary><c>+100</c>, <c>eDifficulty</c>.</summary>
        public int Difficulty { get; set; } = difficulty;

        /// <summary><c>+104</c>, <c>nTick</c>: the updates run so far, which the interface timer counts down from.</summary>
        public int Tick { get; set; }

        /// <summary>Binds what <c>0x488F90</c> binds: four fields and the ten number-row globals.</summary>
        public void Export(ScriptVarRegistry variables, sbyte[] digitKeys)
        {
            variables.BindInt(Task, "isComplete", () => Complete, value => Complete = unchecked((sbyte)value), size: 1);
            variables.BindInt(Task, "isFailed", () => Failed, value => Failed = unchecked((sbyte)value), size: 1);
            variables.BindInt(Task, "nTick", () => Tick, value => Tick = value);
            variables.BindInt(Task, "eDifficulty", () => Difficulty, value => Difficulty = value);
            for (int digit = 0; digit < digitKeys.Length; digit++)
            {
                int index = digit;
                variables.BindGlobal(
                    Task,
                    DigitKeySymbol(index),
                    new QvmIntegerVariable(
                        () => digitKeys[index],
                        value => digitKeys[index] = unchecked((sbyte)value),
                        size: 1));
            }
        }

        /// <summary>Removes what <see cref="Export"/> bound, as <c>0x4890F0</c> does.</summary>
        public void Unexport(ScriptVarRegistry variables)
        {
            variables.Unbind(Task, "isComplete");
            variables.Unbind(Task, "isFailed");
            variables.Unbind(Task, "nTick");
            variables.Unbind(Task, "eDifficulty");
            for (int digit = 0; digit < 10; digit++)
            {
                variables.UnbindGlobal(Task, DigitKeySymbol(digit));
            }
        }

        /// <summary>The result half of <c>0x489290</c>, which runs after the task's children.</summary>
        /// <remarks>
        /// A slot that is empty or did not compile leaves its byte alone, so a script's own write to it
        /// stands. A present one stores its floating-point result truncated to a byte, so 0.5 is false.
        /// A failure clears completion whichever of the two wrote it.
        /// </remarks>
        public void Update(LevelExpressionRuntime expressions)
        {
            if (expressions.TryEvaluateReal(CompleteExpression, Task, out double complete))
            {
                Complete = unchecked((sbyte)(long)complete);
            }

            if (expressions.TryEvaluateReal(FailedExpression, Task, out double failed))
            {
                Failed = unchecked((sbyte)(long)failed);
            }

            if (Failed != 0)
            {
                Complete = 0;
            }

            Tick++;
        }

        private static string DigitKeySymbol(int digit) => $"Key{digit}.isPressed";
    }

    private sealed class EditVariable(
        DeclaredTask task,
        int initialValue,
        string addExpression,
        string subExpression)
    {
        public DeclaredTask Task { get; } = task;
        public int Value { get; set; } = initialValue;
        public string AddExpression { get; } = addExpression;
        public string SubExpression { get; } = subExpression;
    }
}
