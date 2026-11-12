using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One status message's send latch and on-screen state: an authored <c>StatusMessage</c> task, or a transient one with no task.</summary>
/// <remarks>Sent, slotted and advanced by <c>MissionStatusMessages</c>.</remarks>
internal sealed class StatusMessage(
    GameTask? owner,
    string sendExpression,
    string textResource,
    bool sendOnce,
    bool cutscene,
    float duration) : Saves.IExchangedState
{
    public GameTask? Task { get; } = owner;
    public string SendExpression { get; } = sendExpression;
    public string TextResource { get; } = textResource;
    public bool SendOnce { get; } = sendOnce;
    public bool Cutscene { get; } = cutscene;
    public float Duration { get; } = duration;
    public string Sound { get; init; } = string.Empty;

    /// <summary>Gets the sprite drawn beside the text, from the status archive; none for an authored message.</summary>
    public string Portrait { get; init; } = string.Empty;
    public bool IsSent { get; set; }

    /// <summary>Gets or sets how many times the message has been sent, which a co-op server tells its clients.</summary>
    public int SendCount { get; set; }
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

    // As the match began: whether and when it was sent, and how far its display had got.
    private (bool Sent, int Count, int SentTick, bool Finished, int FinishedTick, int SinceFinished, bool Displaying, string Text, int Frame, int Remaining, int Hold) _start;

    internal void MarkLevelStart() => _start = (
        IsSent, SendCount, SentTick, IsFinishedDisplay, FinishedDisplayTick, TicksSinceFinishedDisplay, IsDisplaying, DisplayText,
        DisplayFrame, CharactersRemaining, HoldTicks);

    internal void ResetToLevelStart() =>
        (IsSent, SendCount, SentTick, IsFinishedDisplay, FinishedDisplayTick, TicksSinceFinishedDisplay, IsDisplaying, DisplayText,
            DisplayFrame, CharactersRemaining, HoldTicks) = _start;

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        var levelStart = _start;
        MarkLevelStart();
        _start = (
            exchange.Bool(_start.Sent),
            exchange.Int(_start.Count),
            exchange.Int(_start.SentTick),
            exchange.Bool(_start.Finished),
            exchange.Int(_start.FinishedTick),
            exchange.Int(_start.SinceFinished),
            exchange.Bool(_start.Displaying),
            exchange.Text(_start.Text),
            exchange.Int(_start.Frame),
            exchange.Int(_start.Remaining),
            exchange.Int(_start.Hold));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

    /// <summary>Passes a message no task sent through a save: how long it shows and beside what, then its state.</summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="message">The message as it stands, or <see langword="null"/> for one a save holds and the level does not.</param>
    /// <returns>The message as it is to be: a new one while a save is read.</returns>
    internal static StatusMessage ExchangeTransient(Saves.StateExchange exchange, StatusMessage? message)
    {
        float duration = exchange.Real(message?.Duration ?? 0f);
        string portrait = exchange.Text(message?.Portrait ?? string.Empty);
        StatusMessage kept = exchange.IsReading || message is null
            ? new StatusMessage(null, string.Empty, string.Empty, sendOnce: true, cutscene: false, duration) { Portrait = portrait }
            : message;
        ((Saves.IExchangedState)kept).Exchange(exchange);
        return kept;
    }

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

    public void StopDisplay() => IsDisplaying = false;

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
