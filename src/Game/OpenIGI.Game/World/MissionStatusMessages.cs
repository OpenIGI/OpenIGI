using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// The mission's status messages: the authored <c>StatusMessage</c> tasks, the transient ones game events post,
/// the twenty-four on-screen slots they share and the map-computer log the authored ones feed.
/// </summary>
/// <remarks>
/// Owned by <see cref="MissionObjectiveRuntime"/>, which keeps the level's tick and decides when each part runs;
/// this class only sends, slots and advances the messages it is told to.
/// </remarks>
internal sealed class MissionStatusMessages
{
    private readonly LevelExpressionRuntime _expressions;
    private readonly Func<string, string> _resolveText;
    private readonly Func<int> _tick;
    private readonly List<StatusMessage> _statusMessages = [];
    private readonly List<StatusMessage> _transientMessages = [];
    private readonly HashSet<StatusMessage> _walkedMessages = [];

    /// <summary>The live message slots, exactly as many as <c>0x486650</c> searches.</summary>
    /// <remarks>
    /// Twenty-four, and a send that finds them all busy is dropped rather than queued — the original
    /// walks the array looking for a free entry and simply returns when it does not find one. Modelled as
    /// an array rather than a list because the slot index is the draw order.
    /// </remarks>
    private readonly StatusMessage?[] _messageSlots = new StatusMessage?[MissionObjectiveRuntime.MessageSlotCount];
    private readonly List<MissionComputerLogEntry> _log = [];

    /// <summary>Builds the message set over the mission's expressions.</summary>
    /// <param name="expressions">Evaluates each message's <c>Send</c> expression.</param>
    /// <param name="resolveText">Resolves a message resource to its text.</param>
    /// <param name="tick">Reads the level's tick at the moment a message sends or finishes.</param>
    public MissionStatusMessages(LevelExpressionRuntime expressions, Func<string, string> resolveText, Func<int> tick)
    {
        _expressions = expressions;
        _resolveText = resolveText;
        _tick = tick;
    }

    /// <summary>Plays a message's <c>Sound</c> as it goes up; see <see cref="MissionObjectiveRuntime.MessageSound"/>.</summary>
    public Action<string>? MessageSound { get; set; }

    /// <summary>Gets the authored, non-cutscene messages recorded by the map-computer log.</summary>
    public IReadOnlyList<MissionComputerLogEntry> Log => _log;

    /// <summary>Gets how many authored status-message tasks the mission contains.</summary>
    public int Count => _statusMessages.Count;

    /// <summary>Adds one authored message and exports its fields on its task.</summary>
    /// <param name="declared">The message's task.</param>
    /// <param name="message">The message read from it.</param>
    /// <param name="variables">The mission's script variables.</param>
    public void Declare(DeclaredTask declared, StatusMessage message, ScriptVarRegistry variables)
    {
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

    /// <summary>Shows a non-scripted message in the next free slot; see <see cref="MissionObjectiveRuntime.ShowTransientMessage"/>.</summary>
    /// <param name="text">The already formatted text to draw.</param>
    /// <param name="durationSeconds">How long the text holds after it has typed out.</param>
    /// <returns><see langword="true"/> when a display slot was available.</returns>
    public bool ShowTransient(string text, float durationSeconds)
    {
        StatusMessage message = new(
            owner: null,
            string.Empty,
            string.Empty,
            sendOnce: true,
            cutscene: false,
            durationSeconds)
        {
            IsSent = true,
            SentTick = _tick(),
        };
        if (!TryTakeMessageSlot(message))
        {
            return false;
        }

        message.BeginDisplay(text);
        _transientMessages.Add(message);
        return true;
    }

    /// <summary>Gets the messages on screen now, in slot order; see <see cref="MissionObjectiveRuntime.DisplayedMessages"/>.</summary>
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

    /// <summary>Empties every message slot; see <see cref="MissionObjectiveRuntime.ClearMessages"/>.</summary>
    public void ClearMessages()
    {
        for (int i = 0; i < _messageSlots.Length; i++)
        {
            if (_messageSlots[i] is not { } message)
            {
                continue;
            }

            _messageSlots[i] = null;
            message.StopDisplay();
            if (_transientMessages.Remove(message))
            {
                continue;
            }

            message.IsFinishedDisplay = true;
            message.FinishedDisplayTick = _tick();
            message.TicksSinceFinishedDisplay = 0;
        }
    }

    /// <summary>Removes subtitles that belong to a skipped cutscene; see <see cref="MissionObjectiveRuntime.ClearCutsceneMessages"/>.</summary>
    public void ClearCutsceneMessages()
    {
        foreach (StatusMessage message in _statusMessages)
        {
            if (!message.Cutscene || !message.IsDisplaying)
            {
                continue;
            }

            message.StopDisplay();
            ReleaseMessageSlot(message);
        }
    }

    /// <summary>Puts each authored message into the level's update walk, where the tree places its task.</summary>
    /// <param name="walker">The level walk.</param>
    public void AttachTo(LevelUpdateWalker walker)
    {
        // StatusMessage::Run (0x4855F0) is the task's update: it runs where the walk reaches it, so a task later in
        // the tree sees this tick's send, and a message in a closed container does not update.
        foreach (StatusMessage message in _statusMessages)
        {
            if (message.Task is { } owner)
            {
                _walkedMessages.Add(message);
                walker.Bind(owner, task =>
                {
                    UpdateMessage(message);
                    walker.RunChildren(task);
                });
            }
        }
    }

    /// <summary>Updates the messages the walk does not reach, then advances every displayed message one tick.</summary>
    /// <param name="extraHoldSeconds">The extra hold; see <see cref="MissionObjectiveRuntime.ExtraMessageHoldSeconds"/>.</param>
    public void Tick(float extraHoldSeconds)
    {
        // A message the level walk reaches sends from its own update (UpdateMessage); without a walk they update
        // here, in list order.
        foreach (StatusMessage message in _statusMessages)
        {
            if (!_walkedMessages.Contains(message))
            {
                UpdateMessage(message);
            }

            if (message.IsFinishedDisplay)
            {
                continue;
            }

            if (message.IsDisplaying && message.AdvanceDisplay(extraHoldSeconds))
            {
                message.IsFinishedDisplay = true;
                message.FinishedDisplayTick = _tick();
                message.TicksSinceFinishedDisplay = 0;
                ReleaseMessageSlot(message);
            }
        }

        for (int i = _transientMessages.Count - 1; i >= 0; i--)
        {
            StatusMessage message = _transientMessages[i];
            if (message.IsDisplaying && message.AdvanceDisplay(extraHoldSeconds))
            {
                ReleaseMessageSlot(message);
                _transientMessages.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// StatusMessage::Run (<c>0x4855F0</c>): the ticks since its display finished count on, then it sends when its
    /// expression holds -- a one-shot once, a repeating one no sooner than 210 ticks after its last send.
    /// </summary>
    private void UpdateMessage(StatusMessage message)
    {
        if (message.IsFinishedDisplay)
        {
            message.TicksSinceFinishedDisplay++;
        }

        if ((!message.SendOnce || !message.IsSent)
            && _tick() > message.SentTick + 210
            && _expressions.EvaluateCondition(message.SendExpression, emptyValue: false, message.Task))
        {
            message.IsSent = true;
            message.SentTick = _tick();

            // 0x486650 takes the lowest free slot and drops the message outright when there is none. The display
            // only begins if it got one: a message with nowhere to go is not shown, and the original does not
            // queue it for later.
            if (TryTakeMessageSlot(message))
            {
                message.BeginDisplay(_resolveText(message.TextResource));
                if (message.Sound.Length > 0)
                {
                    MessageSound?.Invoke(message.Sound);
                }
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
    }

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
}
