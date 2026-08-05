using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>The retail IGI1 objective definitions and LevelFlow result for one loaded mission.</summary>
/// <remarks>
/// IGI1 keeps these as two separate task types. <c>DefineComputerObjective</c> owns the six rows shown
/// by the map computer; <c>LevelFlow</c> alone decides whether the level has completed or failed. A
/// level may contain several objective definitions and switch between them with <c>Objectives Valid</c>.
/// The last valid definition in task-update order is the one the original publishes globally.
/// </remarks>
public sealed class MissionObjectiveRuntime
{
    /// <summary>The maximum number of objective rows in one IGI1 definition.</summary>
    public const int ObjectiveSlotCount = 6;

    /// <summary>The number of status messages IGI1 can have on screen at once.</summary>
    /// <remarks>The slot array <c>0x486650</c> searches, at a stride of 4140 bytes.</remarks>
    public const int MessageSlotCount = 24;

    private readonly GameTask _levelRoot;
    private readonly LevelExpressionRuntime _expressions;
    private readonly Func<string, string> _resolveText;
    private readonly List<Definition> _definitions = [];
    private readonly List<AreaActivation> _areaActivations = [];
    private readonly List<EditVariable> _editVariables = [];
    private readonly List<OutroCutScene> _outroCutScenes = [];
    private readonly List<StatusMessage> _statusMessages = [];

    /// <summary>The live message slots, exactly as many as <c>0x486650</c> searches.</summary>
    /// <remarks>
    /// Twenty-four, and a send that finds them all busy is dropped rather than queued — the original
    /// walks the array looking for a free entry and simply returns when it does not find one. Modelled as
    /// an array rather than a list because the slot index is the draw order.
    /// </remarks>
    private readonly StatusMessage?[] _messageSlots = new StatusMessage?[MessageSlotCount];
    private readonly List<MissionComputerLogEntry> _log = [];
    private readonly string _completeExpression = string.Empty;
    private readonly string _failedExpression = string.Empty;
    private readonly bool _hasLevelFlow;
    private int _tick;

    /// <summary>Builds the runtime from the authored level task tree.</summary>
    public MissionObjectiveRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, string>? resolveText = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        _levelRoot = levelRoot;
        _expressions = new LevelExpressionRuntime(variables);
        _resolveText = resolveText ?? (static key => key);

        List<GameTask> tasks = Walk(levelRoot).ToList();

        // The port skips cinematics by presenting the intro chain as finished. That steady-play stub must
        // not also finish the authored outro or every mission's terminal StatusMessage fires at spawn.
        // All retail missions 1..13 place their terminal CutScene below a specifically named Outro
        // ConditionalContainer; keep it unfinished until that container's authored trigger becomes true.
        foreach (GameTask task in tasks)
        {
            if (task is CutSceneTask cutScene && FindOutroContainer(task) is { } outro)
            {
                cutScene.IsRun = false;
                cutScene.IsFinished = false;
                cutScene.Ticks = 0;
                _outroCutScenes.Add(new OutroCutScene(cutScene, outro.Condition));
            }
        }

        foreach (GameTask task in tasks)
        {
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
                    Vector(declared, "Position"),
                    Orientation(declared),
                    Vector32(declared, "Dimensions"),
                    String(declared, "Criteria"));
                _areaActivations.Add(area);
                variables.BindInt(declared, "nActive", () => area.ActiveCount, value => area.ActiveCount = value);
            }
            else if (string.Equals(task.Type.Name, "EditVariable", StringComparison.Ordinal))
            {
                EditVariable edit = new(
                    Int(declared, "Initial value", 0),
                    String(declared, "Add"),
                    String(declared, "Sub"));
                _editVariables.Add(edit);
                variables.BindInt(declared, "nValue", () => edit.Value, value => edit.Value = value);
            }
            else if (string.Equals(task.Type.Name, "LevelFlow", StringComparison.Ordinal))
            {
                _hasLevelFlow = true;
                _completeExpression = String(declared, "Complete");
                _failedExpression = String(declared, "Failed");
            }
            else if (string.Equals(task.Type.Name, "StatusMessage", StringComparison.Ordinal))
            {
                StatusMessage message = new(
                    String(declared, "Send"),
                    String(declared, "Text"),
                    Bool(declared, "Is send once"),
                    Bool(declared, "Cutscene message"),
                    Real(declared, "Duration", 2f));
                _statusMessages.Add(message);

                // 0x485770 exports these five fields. In particular, retail LevelFlow expressions
                // use StatusMessage_N.isSendt to finish missions, so these must be live aliases.
                variables.BindBool(declared, "isSendt", () => message.IsSent, value => message.IsSent = value);
                variables.BindInt(declared, "nTickSendt", () => message.SentTick, value => message.SentTick = value);
                variables.BindBool(declared, "isFinishedDisplay", () => message.IsFinishedDisplay,
                    value => message.IsFinishedDisplay = value);
                variables.BindInt(declared, "nFinishedDisplay", () => message.FinishedDisplayTick,
                    value => message.FinishedDisplayTick = value);
                variables.BindInt(declared, "nTicksSinceFinishedDisplay", () => message.TicksSinceFinishedDisplay,
                    value => message.TicksSinceFinishedDisplay = value);
            }
        }

        Objectives = [];
        Tick();
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

    /// <summary>Gets authored, non-cutscene status messages recorded by IGI1's ComputerLog.</summary>
    public IReadOnlyList<MissionComputerLogEntry> Log => _log;

    /// <summary>Gets how many authored status-message tasks the mission contains.</summary>
    public int StatusMessageCount => _statusMessages.Count;

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
    public IEnumerable<StatusMessageDisplay> DisplayedMessages
    {
        get
        {
            foreach (StatusMessage? slot in _messageSlots)
            {
                if (slot is { IsDisplaying: true })
                {
                    yield return new StatusMessageDisplay(
                        slot.DisplayText,
                        slot.RevealedCharacters,
                        slot.DisplayFrame,
                        slot.Cutscene);
                }
            }
        }
    }

    /// <summary>Gets how many authored objective definitions the mission contains.</summary>
    public int DefinitionCount => _definitions.Count;

    /// <summary>Gets whether the level declares the LevelFlow task that owns mission success and failure.</summary>
    public bool HasLevelFlow => _hasLevelFlow;

    /// <summary>Gets the result of the LevelFlow <c>Complete</c> expression.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the result of the LevelFlow <c>Failed</c> expression.</summary>
    public bool IsFailed { get; private set; }

    /// <summary>Evaluates the authored definitions once, after the rest of the mission's tick.</summary>
    public void Tick(Vector3d? playerPosition = null)
    {
        // AreaActivate::Update (0x5048A0) clears nActive, queries the authored oriented volume and
        // sums matching occupants. The current world exposes the player as the sole mission-query
        // occupant; the criteria filter is retained so explicitly non-player areas do not fire.
        foreach (AreaActivation area in _areaActivations)
        {
            area.ActiveCount = playerPosition is { } point && area.AcceptsPlayer && area.Contains(point) ? 1 : 0;
        }

        // EditVariable::Update (0x4889B0) evaluates Add first and Sub second on every simulation tick.
        foreach (EditVariable edit in _editVariables)
        {
            if (!string.IsNullOrEmpty(edit.AddExpression))
            {
                edit.Value += _expressions.EvaluateInt32(edit.AddExpression);
            }
            if (!string.IsNullOrEmpty(edit.SubExpression))
            {
                edit.Value -= _expressions.EvaluateInt32(edit.SubExpression);
            }
        }

        // With no cinematic player, entering an outro completes it immediately; the trigger is still the
        // retail ConditionalContainer expression, so gameplay must perform the real authored action first.
        foreach (OutroCutScene outro in _outroCutScenes)
        {
            if (!outro.Task.IsFinished
                && _expressions.EvaluateCondition(outro.TriggerExpression, emptyValue: false))
            {
                outro.Task.IsRun = false;
                outro.Task.IsFinished = true;
            }
        }

        Definition? active = null;
        foreach (Definition definition in _definitions)
        {
            if (_expressions.EvaluateCondition(definition.ValidExpression, emptyValue: true))
            {
                active = definition;
            }
        }

        List<MissionObjective> current = [];
        if (active is not null)
        {
            foreach (Slot slot in active.Slots)
            {
                if (string.IsNullOrEmpty(slot.TextResource))
                {
                    continue;
                }

                bool complete = _expressions.EvaluateCondition(slot.CompleteExpression, emptyValue: false);
                bool failed = _expressions.EvaluateCondition(slot.FailedExpression, emptyValue: false);
                MissionObjectiveState state = failed ? MissionObjectiveState.Failed
                    : complete ? MissionObjectiveState.Complete
                    : MissionObjectiveState.Incomplete;
                Vector3d? position = slot.LinkTaskId >= 0 && _levelRoot.Tree.FromId(slot.LinkTaskId) is not null
                    ? FindPosition(_levelRoot.Tree.FromId(slot.LinkTaskId)!)
                    : null;
                current.Add(new MissionObjective(slot.TextResource, slot.LinkTaskId, position, state));
            }
        }

        Objectives = current;

        // StatusMessage::Run (0x4855F0) throttles sends by 210 simulation ticks. A one-shot refuses
        // further sends after isSendt becomes true; repeating messages may send again after the delay.
        foreach (StatusMessage message in _statusMessages)
        {
            if ((!message.SendOnce || !message.IsSent)
                && _tick > message.SentTick + 210
                && _expressions.EvaluateCondition(message.SendExpression, emptyValue: false))
            {
                message.IsSent = true;
                message.SentTick = _tick;

                // 0x486650 takes the lowest free slot and drops the message outright when there is
                // none. The display only begins if it got one: a message with nowhere to go is not
                // shown, and the original does not queue it for later.
                if (TryTakeMessageSlot(message))
                {
                    message.BeginDisplay(_resolveText(message.TextResource));
                }

                if (!message.Cutscene && !string.IsNullOrEmpty(message.TextResource))
                {
                    if (_log.Count == 50)
                    {
                        _log.RemoveAt(0);
                    }
                    _log.Add(new MissionComputerLogEntry(message.TextResource));
                }
            }

            if (message.IsFinishedDisplay)
            {
                message.TicksSinceFinishedDisplay++;
            }
            else if (message.IsDisplaying && message.AdvanceDisplay(ExtraMessageHoldSeconds))
            {
                message.IsFinishedDisplay = true;
                message.FinishedDisplayTick = _tick;
                message.TicksSinceFinishedDisplay = 0;
                ReleaseMessageSlot(message);
            }
        }

        bool failedLevel = _expressions.EvaluateCondition(_failedExpression, emptyValue: false);
        bool completeLevel = _expressions.EvaluateCondition(_completeExpression, emptyValue: false);
        IsFailed = failedLevel;
        IsComplete = completeLevel && !failedLevel;
        _tick++;
    }

    private static Definition ReadDefinition(DeclaredTask task)
    {
        List<Slot> slots = [];
        for (int number = 1; number <= ObjectiveSlotCount; number++)
        {
            slots.Add(new Slot(
                String(task, $"Objective {number} Text Resource"),
                Int(task, $"Objective {number} Link To Position", -1),
                String(task, $"Objective {number} Complete Expression"),
                String(task, $"Objective {number} Failed Expression")));
        }

        return new Definition(String(task, "Objectives Valid"), slots);
    }

    private static string String(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text.TrimEnd('\r', '\n') : string.Empty;

    private static int Int(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

    private static bool Bool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value switch
        {
            bool flag => flag,
            byte number => number != 0,
            int number => number != 0,
            _ => false,
        };

    private static float Real(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float number => number,
            double number => (float)number,
            _ => fallback,
        } : fallback;

    /// <summary>Puts a message in the lowest free slot, as <c>0x486650</c> does.</summary>
    /// <param name="message">The message being sent.</param>
    /// <returns><see langword="false"/> when all the slots are busy and the send is dropped.</returns>
    private bool TryTakeMessageSlot(StatusMessage message)
    {
        // A repeating message that is sent again while its previous showing is still on screen keeps the
        // slot it already has rather than taking a second one, which is what stops one noisy trigger from
        // filling the column with copies of itself.
        for (int i = 0; i < _messageSlots.Length; i++)
        {
            if (ReferenceEquals(_messageSlots[i], message))
            {
                return true;
            }
        }

        for (int i = 0; i < _messageSlots.Length; i++)
        {
            if (_messageSlots[i] is null)
            {
                _messageSlots[i] = message;
                return true;
            }
        }

        return false;
    }

    private void ReleaseMessageSlot(StatusMessage message)
    {
        for (int i = 0; i < _messageSlots.Length; i++)
        {
            if (ReferenceEquals(_messageSlots[i], message))
            {
                _messageSlots[i] = null;
                return;
            }
        }
    }

    private static Vector3d Vector(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : default;

    private static Vector3f Vector32(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3f vector ? vector : default;

    private static Matrix3f Orientation(DeclaredTask task) =>
        task.TryGetValue("Orientation", out object? value) && value is Vector3f angles
            ? Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z)
            : Matrix3f.Identity;

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

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

    private static ConditionalContainerTask? FindOutroContainer(GameTask task)
    {
        for (GameTask? ancestor = task.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ConditionalContainerTask container
                && container.Label.Contains("outro", StringComparison.OrdinalIgnoreCase)
                && !container.Label.Contains("intro and outro", StringComparison.OrdinalIgnoreCase))
            {
                return container;
            }
        }
        return null;
    }

    private sealed record Definition(string ValidExpression, IReadOnlyList<Slot> Slots);
    private sealed record Slot(string TextResource, int LinkTaskId, string CompleteExpression, string FailedExpression);
    private sealed record OutroCutScene(CutSceneTask Task, string TriggerExpression);

    private sealed class EditVariable(int initialValue, string addExpression, string subExpression)
    {
        public int Value { get; set; } = initialValue;
        public string AddExpression { get; } = addExpression;
        public string SubExpression { get; } = subExpression;
    }

    private sealed class AreaActivation
    {
        private readonly Vector3d _minimum;
        private readonly Vector3d _maximum;

        public AreaActivation(Vector3d position, Matrix3f orientation, Vector3f dimensions, string criteria)
        {
            // AreaActivate::Prepare (0x5045F0) halves Dimensions, rotates all eight corners and
            // publishes their component-wise min/max as the query bounds.
            Vector3f half = dimensions * 0.5f;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3f local = new(
                    (corner & 1) == 0 ? half.X : -half.X,
                    (corner & 2) == 0 ? half.Y : -half.Y,
                    (corner & 4) == 0 ? half.Z : -half.Z);
                Vector3f rotated = orientation.Transposed().Transform(local);
                Vector3d world = position + new Vector3d(rotated.X, rotated.Y, rotated.Z);
                minX = Math.Min(minX, world.X); minY = Math.Min(minY, world.Y); minZ = Math.Min(minZ, world.Z);
                maxX = Math.Max(maxX, world.X); maxY = Math.Max(maxY, world.Y); maxZ = Math.Max(maxZ, world.Z);
            }
            _minimum = new Vector3d(minX, minY, minZ);
            _maximum = new Vector3d(maxX, maxY, maxZ);
            string filter = criteria.Trim();
            AcceptsPlayer = filter.Length == 0
                || filter.Equals("CRITERIA_HUMAN0", StringComparison.OrdinalIgnoreCase)
                || filter.Contains("HumanPlayer", StringComparison.OrdinalIgnoreCase)
                || filter.Equals("PLAYER", StringComparison.OrdinalIgnoreCase);
        }

        public int ActiveCount { get; set; }
        public bool AcceptsPlayer { get; }
        public bool Contains(Vector3d point) =>
            point.X >= _minimum.X && point.X <= _maximum.X
            && point.Y >= _minimum.Y && point.Y <= _maximum.Y
            && point.Z >= _minimum.Z && point.Z <= _maximum.Z;
    }

    private sealed class StatusMessage(
        string sendExpression,
        string textResource,
        bool sendOnce,
        bool cutscene,
        float duration)
    {
        public string SendExpression { get; } = sendExpression;
        public string TextResource { get; } = textResource;
        public bool SendOnce { get; } = sendOnce;
        public bool Cutscene { get; } = cutscene;
        public float Duration { get; } = duration;
        public bool IsSent { get; set; }
        public int SentTick { get; set; } = -100000;
        public bool IsFinishedDisplay { get; set; }
        public int FinishedDisplayTick { get; set; } = -1;
        public int TicksSinceFinishedDisplay { get; set; }
        public bool IsDisplaying { get; private set; }

        /// <summary>Gets the localized text, kept so a renderer has something to draw.</summary>
        public string DisplayText { get; private set; } = string.Empty;

        /// <summary>Gets how much of <see cref="DisplayText"/> has been typed out so far.</summary>
        /// <remarks>
        /// The original keeps a cursor into the string (<c>+44</c>) rather than a count, and starts it at
        /// the <em>end</em> for a cutscene message so a subtitle is whole from its first frame. Same thing
        /// counted from the other side, which is what the reveal below already maintains.
        /// </remarks>
        public int RevealedCharacters => DisplayText.Length - CharactersRemaining;

        /// <summary>Gets how many ticks this message has been on screen, for the panel's opening flash.</summary>
        public int DisplayFrame { get; private set; }

        private int CharactersRemaining { get; set; }
        private int HoldTicks { get; set; }

        public void BeginDisplay(string text)
        {
            IsDisplaying = true;
            DisplayFrame = 0;
            DisplayText = text;

            // A repeating message gets a fresh slot in the original, so its display state starts over.
            // Without this the second send of a message never advances again -- Tick only advances a
            // message that has not finished -- and the card sticks on screen for the rest of the mission.
            IsFinishedDisplay = false;
            TicksSinceFinishedDisplay = 0;

            // A cutscene message is fully revealed on its first frame; everything else types out.
            CharactersRemaining = Cutscene ? 0 : text.Length;
            HoldTicks = 0;
        }

        public bool AdvanceDisplay(float extraHoldSeconds)
        {
            // StatusMessageScreen::UpdateSlot, 0x485B80. Cutscene subtitles hold immediately;
            // ordinary messages fade for nine ticks, reveal one character every other tick, then hold.
            // The original's hold is Duration seconds at 30 Hz; the extra is added in the same units and
            // is zero unless the host asked for it.
            int hold = (int)(Duration * 30f) + (int)(MathF.Max(0f, extraHoldSeconds) * 30f);

            if (Cutscene)
            {
                if (HoldTicks++ > hold)
                {
                    IsDisplaying = false;
                    return true;
                }
                return false;
            }

            if (DisplayFrame >= 9)
            {
                if (CharactersRemaining > 0)
                {
                    if (((DisplayFrame - 9) & 1) == 0)
                    {
                        CharactersRemaining--;
                    }
                }
                else if (++HoldTicks > hold)
                {
                    IsDisplaying = false;
                    return true;
                }
            }
            DisplayFrame++;
            return false;
        }
    }
}

/// <summary>The three objective states IGI1's objectives screen labels.</summary>
public enum MissionObjectiveState
{
    /// <summary>The objective has neither completed nor failed.</summary>
    Incomplete,
    /// <summary>The objective's authored completion expression is true.</summary>
    Complete,
    /// <summary>The objective's authored failure expression is true.</summary>
    Failed,
}

/// <summary>One row in the active map-computer objective definition.</summary>
public sealed record MissionObjective(
    string TextResource,
    int LinkTaskId,
    Vector3d? Position,
    MissionObjectiveState State);

/// <summary>One localized StatusMessage captured by IGI1's map-computer log.</summary>
public sealed record MissionComputerLogEntry(string TextResource);

/// <summary>One status message as it is on screen this tick.</summary>
/// <remarks>
/// A snapshot for a renderer, not the message itself: the state machine that produces it belongs to the
/// simulation and steps at 30 Hz, while whatever draws this may run several times per tick and must not
/// be able to advance it.
/// </remarks>
/// <param name="Text">The localized text in full.</param>
/// <param name="RevealedCharacters">
/// How many leading characters of <paramref name="Text"/> have been typed out. Renderers should draw
/// <c>Text.AsSpan(0, RevealedCharacters)</c>, which needs no allocation per frame.
/// </param>
/// <param name="DisplayFrame">
/// How many ticks the message has been up. The original spends the first nine of them fading its panel in
/// from three-quarters opacity to a sixth, and holds the category icon back until they have passed.
/// </param>
/// <param name="IsCutscene">
/// Whether this is a cutscene message — the flag the original calls <c>+454</c>. Those are drawn as a
/// subtitle low on the screen, are whole from their first frame, and are kept out of the computer log.
/// </param>
public readonly record struct StatusMessageDisplay(
    string Text,
    int RevealedCharacters,
    int DisplayFrame,
    bool IsCutscene);
