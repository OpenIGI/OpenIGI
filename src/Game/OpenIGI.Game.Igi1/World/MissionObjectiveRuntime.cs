using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Flow;
using OpenIGI.Input;
using OpenIGI.Net;
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
public sealed class MissionObjectiveRuntime : IComputerObjectives, ILevelReset, Saves.ISavedStateSource, Hosting.IMissionObjectives
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
    private readonly List<DefineComputerObjectiveTask> _definitions = [];
    private readonly MissionAreaActivation _areas;
    private readonly List<EditVariableTask> _editVariables = [];

    // The level's walk once the runtime is attached to it; until then, the one the counters are stepped by here.
    private LevelUpdateWalker? _walker;
    private readonly MissionStatusMessages _messages;
    private readonly List<LevelFlowTask> _levelFlows = [];

    /// <summary>
    /// <c>Key0.isPressed</c> to <c>Key9.isPressed</c>: the globals <c>byte_5C8AA8</c>..<c>byte_5C8AB1</c>,
    /// indexed by digit, holding the raw DirectInput state byte (<c>0x80</c> when down).
    /// </summary>
    private readonly sbyte[] _digitKeys = new sbyte[10];
    private readonly Func<KeyCode, bool>? _isKeyDown;

    /// <summary>The LevelFlow the original's <c>flt_BC2360</c> points at: the last one initialised.</summary>
    private readonly LevelFlowTask? _levelFlow;
    private bool _attached;

    // The walk this runtime makes for itself when it is given none: a level stepped by its objectives alone.
    private LevelUpdateWalker? _ownWalk;
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
    /// <param name="expressions">The level's expressions, or <see langword="null"/> to compile with ones of its own.</param>
    public MissionObjectiveRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, string>? resolveText = null,
        Func<string, ScriptArguments, QvmValue?>? native = null,
        int difficulty = 1,
        Func<KeyCode, bool>? isKeyDown = null,
        LevelExpressionRuntime? expressions = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        _levelRoot = levelRoot;
        _variables = variables;
        _expressions = expressions ?? new LevelExpressionRuntime(variables, native);
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
            if (task is EditVariableTask counter)
            {
                _editVariables.Add(counter);
                continue;
            }

            if (task is AreaActivateTask box)
            {
                _areas.Add(box);
                continue;
            }

            if (task is DefineComputerObjectiveTask rows)
            {
                _definitions.Add(rows);
                continue;
            }

            if (task is LevelFlowTask flow)
            {
                // 0x488ED0 copies the active profile's difficulty; 0x488F90 binds the number row beside the flow's own fields.
                flow.Difficulty = difficulty;
                ExportDigitKeys(flow);
                _levelFlows.Add(flow);
                _levelFlow = flow;
                continue;
            }

            if (task is Igi1StatusMessageTask message)
            {
                _messages.Add(message);
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
    public IReadOnlyList<MissionObjective> Objectives
    {
        get => Silenced ? [] : _objectives;
        private set => _objectives = value;
    }

    private IReadOnlyList<MissionObjective> _objectives = [];

    /// <summary>
    /// Gets or sets whether the mission's objectives and messages are kept off the screen while it still runs them: a level
    /// played as a multiplayer arena, whose campaign's objectives are nobody's.
    /// </summary>
    public bool Silenced { get; set; }

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

    /// <summary>
    /// Gets or sets whether this level takes its running state from a co-op server's rather than working it out: its counters,
    /// the messages sent and the mission's result. Everything else it works out as ever, from those.
    /// </summary>
    public bool Mirrored
    {
        get => _messages.Mirrored;
        set
        {
            _messages.Mirrored = value;
            _walker?.IsReplica = value;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<GameTask, Saves.ISavedState>> SavedStates(ObjectCollisionIndex? solids) =>
        _editVariables.Select(static e => new KeyValuePair<GameTask, Saves.ISavedState>(e, e))
            .Concat(_levelFlows.Select(static f => new KeyValuePair<GameTask, Saves.ISavedState>(f, f)))
            .Concat(_areas.SavedStates)
            .Concat(_messages.SavedStates);

    /// <summary>
    /// Gets what a save keeps of the level's running state that belongs to no one task: its clock, whom its areas counted, and
    /// its messages' slots and log. Not retail: Project I.G.I. kept no save games.
    /// </summary>
    public Saves.ISaveSection SaveSection => new Saves.ExchangedSection("Igi1Objectives", exchange =>
    {
        _tick = exchange.Int(_tick);
        _areas.Exchange(exchange);
        _messages.Exchange(exchange);
    });

    // As the match began: each edit variable's value, each level flow's verdict and clock, each area's count.
    private (int[] Edits, (sbyte Complete, sbyte Failed, int Tick)[] Flows, int Tick) _start = ([], [], 0);

    /// <inheritdoc/>
    public void MarkLevelStart()
    {
        _start = ([.. _editVariables.Select(static e => e.Value)], [.. _levelFlows.Select(static f => (f.Complete, f.Failed, f.Tick))], _tick);
        _messages.MarkLevelStart();
        _areas.MarkLevelStart();
    }

    /// <inheritdoc/>
    /// <remarks>Every counter, message and verdict as the match began, so a level's own expressions read what they read then.</remarks>
    public void ResetToLevelStart(ObjectCollisionIndex? solids)
    {
        // A co-op client's counters, verdicts and messages are its server's, which sends them as they went back.
        if (Mirrored)
        {
            return;
        }

        for (int i = 0; i < _editVariables.Count && i < _start.Edits.Length; i++)
        {
            _editVariables[i].Value = _start.Edits[i];
        }

        for (int i = 0; i < _levelFlows.Count && i < _start.Flows.Length; i++)
        {
            (_levelFlows[i].Complete, _levelFlows[i].Failed, _levelFlows[i].Tick) = _start.Flows[i];
        }

        _tick = _start.Tick;
        _messages.ResetToLevelStart();
        _areas.ResetToLevelStart();
    }

    /// <summary>Writes the level's running state, for a co-op server's clients: counters, messages sent, result.</summary>
    /// <param name="writer">Where to.</param>
    public void WriteState(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt((uint)_editVariables.Count);
        foreach (EditVariableTask edit in _editVariables)
        {
            writer.WriteVarInt(edit.Value);
        }

        _messages.WriteSends(writer);
        writer.WriteVarUInt((uint)_levelFlows.Count);
        foreach (LevelFlowTask flow in _levelFlows)
        {
            writer.WriteBits(unchecked((byte)flow.Complete), 8);
            writer.WriteBits(unchecked((byte)flow.Failed), 8);
        }
    }

    /// <summary>Takes a co-op server's running state, as <see cref="WriteState"/> wrote it.</summary>
    /// <param name="reader">The state.</param>
    /// <param name="catchingUp">Whether this is the client's first word from the server, joining a level under way.</param>
    public void ReadState(ref BitReader reader, bool catchingUp)
    {
        int edits = (int)reader.ReadVarUInt();
        for (int i = 0; i < edits && !reader.IsOverflowed; i++)
        {
            int value = reader.ReadVarInt();
            if (i < _editVariables.Count)
            {
                _editVariables[i].Value = value;
            }
        }

        _messages.ReadSends(ref reader, catchingUp);
        int flows = (int)reader.ReadVarUInt();
        for (int i = 0; i < flows && !reader.IsOverflowed; i++)
        {
            sbyte complete = unchecked((sbyte)reader.ReadBits(8));
            sbyte failed = unchecked((sbyte)reader.ReadBits(8));
            if (i < _levelFlows.Count)
            {
                _levelFlows[i].Complete = complete;
                _levelFlows[i].Failed = failed;
            }
        }
    }

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

    /// <summary>Gets the messages on screen now, in the slot order the original draws them in; while <see cref="Silenced"/>, only
    /// what the game itself says of an event (a weapon picked up), as a multiplayer arena shows it.</summary>
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
    public IEnumerable<StatusMessageDisplay> DisplayedMessages => Silenced ? _messages.EventMessages : _messages.DisplayedMessages;

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
    public float MaximumLevelPlayTimeSeconds => Math.Max(0f, _levelFlow?.MaxPlayTime ?? 0f);

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

            float seconds = Math.Max(0f, flow.MaxPlayTime);
            int maximumTicks = seconds >= int.MaxValue / 30f
                ? int.MaxValue
                : (int)MathF.Round(seconds * 30f, MidpointRounding.ToEven);
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

        foreach (LevelFlowTask flow in _levelFlows)
        {
            _variables.Unbind(flow, "isComplete");
            _variables.Unbind(flow, "isFailed");
            _variables.Unbind(flow, "nTick");
            _variables.Unbind(flow, "eDifficulty");
            for (int digit = 0; digit < _digitKeys.Length; digit++)
            {
                _variables.UnbindGlobal(flow, DigitKeySymbol(digit));
            }
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

        // The level's walk updates each area, counter, definition, flow and message where the tree places it. A runtime no
        // walk was given to (AttachTo) walks its level itself, the same way: there is one path.
        if (!_attached)
        {
            _ = OwnWalk;
        }

        _ownWalk?.Tick();
    }

    /// <summary>
    /// Gets the walk a runtime that was given none walks its level with, made on first use: what a level stepped by its
    /// objectives alone is run by.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime rides another's walk (<see cref="AttachTo"/>).</exception>
    public LevelUpdateWalker OwnWalk
    {
        get
        {
            if (_ownWalk is null)
            {
                if (_attached)
                {
                    throw new InvalidOperationException("The objectives ride the walk they were attached to; they have none of their own.");
                }

                _ownWalk = new LevelUpdateWalker(_levelRoot);
                AttachTo(_ownWalk);
            }

            return _ownWalk;
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
        if (!ReferenceEquals(walker, _ownWalk))
        {
            // A walk given from outside takes the level over from the runtime's own.
            _ownWalk = null;
        }

        _attached = true;
        walker.CurrentObjectives = _walker?.CurrentObjectives;
        _walker = walker;
        walker.Expressions = _expressions;
        walker.IsReplica = Mirrored;
        _areas.SetSources(vehicles, soldiers, player);
        _areas.Register(walker.Areas);

        _messages.AttachToWalk();
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

        // A level flow runs itself where the level's walk reaches it (0x489290): its children first, then its verdicts.

        // The number row, as the end of 0x489290 samples it.
        if (_levelFlows.Count > 0)
        {
            SampleDigitKeys();
        }

        _tick++;
    }

    private static string DigitKeySymbol(int digit) => $"Key{digit}.isPressed";

    // Key0..Key9.isPressed, globals a level flow's export binds beside its own fields (0x488F90).
    private void ExportDigitKeys(LevelFlowTask flow)
    {
        for (int digit = 0; digit < _digitKeys.Length; digit++)
        {
            int index = digit;
            _variables.BindGlobal(
                flow,
                DigitKeySymbol(index),
                new QvmIntegerVariable(
                    () => _digitKeys[index],
                    value => _digitKeys[index] = unchecked((sbyte)value),
                    size: 1));
        }
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

    /// <summary>Publishes the rows of the objective definition that ran last, the computer's.</summary>
    /// <remarks>
    /// The definitions run themselves where the level's walk reaches them (<c>0x467410</c>); with no walk attached each
    /// runs here, in the tree's order. Running them at construction, before the host has a player to report, latches the
    /// level's opening state.
    /// </remarks>
    private void PublishObjectives()
    {
        List<MissionObjective> current = [];
        if (_walker?.CurrentObjectives is { } active)
        {
            for (int index = 0; index < active.Objectives.Count; index++)
            {
                ComputerObjective row = active.Objectives[index];
                string text = row.TextResource.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                MissionObjectiveState state = row.IsFailed ? MissionObjectiveState.Failed
                    : row.IsComplete ? MissionObjectiveState.Complete
                    : MissionObjectiveState.Incomplete;
                Vector3d? position = row.LinkToPosition >= 0 && _levelRoot.Tree.FromId(row.LinkToPosition) is { } linked
                    ? FindPosition(linked)
                    : null;
                current.Add(new MissionObjective(text, row.LinkToPosition, position, state)
                {
                    Text = _resolveText(text),
                    Slot = index,
                });
            }
        }

        Objectives = current;
    }

    private static Matrix3f Orientation(DeclaredTask task) =>
        task.TryGetValue("Orientation", out object? value) && value is Vector3f angles
            ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
            : Matrix3f.Identity;

    // Where a task a row points to stands: its graph position where it has one, else its position, whatever class makes it.
    private static Vector3d FindPosition(GameTask task) =>
        task.TryGetScriptValue("Graph position", out Vector3d graphPosition) ? graphPosition
        : task.TryGetScriptValue("Position", out Vector3d position) ? position
        : default;
}
