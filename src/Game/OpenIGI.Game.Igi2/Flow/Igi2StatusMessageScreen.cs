namespace OpenIGI.Game.Igi2.Flow;

/// <summary>One of the status message screen's places, and the line it is printing.</summary>
public sealed class Igi2StatusMessageSlot
{
    /// <summary>Gets a value indicating whether a message holds it.</summary>
    public bool IsUsed { get; internal set; }

    /// <summary>Gets the message printing in it, or <see langword="null"/> once that message is gone.</summary>
    public Igi2StatusMessageTask? Message { get; internal set; }

    /// <summary>Gets the line it prints, the message's text as it was registered.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Gets how much of <see cref="Text"/> is printed so far.</summary>
    public int Printed { get; internal set; }

    /// <summary>Gets the ticks it has been shown.</summary>
    public int Age { get; internal set; }

    /// <summary>Gets the ticks since it finished printing.</summary>
    public int DoneTicks { get; internal set; }

    /// <summary>Gets how far its box has faded in, a seventh a tick from its third tick.</summary>
    public float Fade { get; internal set; }

    /// <summary>Gets how far its box has opened, a half a tick over its first two.</summary>
    public float Open { get; internal set; }
}

/// <summary>
/// Where the status messages are printed, I.G.I. 2's <c>StatusMessageScreen</c>: twenty-four places, each a line printed a
/// letter at a time, which tells its message when it is done printing.
/// </summary>
/// <remarks>
/// <para>
/// A message registers into the first free place (<c>StatusMessageScreen_RegisterMessage</c>, <c>igi2 0x43D8A0</c>) and is
/// unprinted again; a line that is not a cutscene's is also kept in the computer's log, and starts printing from its first
/// letter, a cutscene's from its end.
/// </para>
/// <para>
/// Each tick (<c>StatusMessageScreen_UpdateMessage</c>, <c>0x43D540</c>), unless the game is paused: a cutscene's line is laid
/// out whole and counts as done; another's box opens over two ticks and fades in over seven, and from its ninth tick a letter
/// is printed every other tick until the line is out, after which it counts as done. Thirty ticks of done, and its message is
/// told it has finished printing, which is what lets a message end.
/// </para>
/// <para>The screen is a game screen's, which runs after the level each tick.</para>
/// </remarks>
public sealed class Igi2StatusMessageScreen
{
    /// <summary>The number of places, <c>24</c>.</summary>
    public const int SlotCount = 24;

    /// <summary>The ticks a line's box takes to open and fade in before it prints, <c>9</c>.</summary>
    public const int PrintStart = 9;

    /// <summary>The ticks of done after which a message has finished printing; it is told on the one after.</summary>
    public const int DoneTicksToFinish = 30;

    private readonly Igi2StatusMessageSlot[] _slots = [.. Enumerable.Range(0, SlotCount).Select(static _ => new Igi2StatusMessageSlot())];
    private readonly List<string> _log = [];

    /// <summary>Gets the places, for drawing.</summary>
    public IReadOnlyList<Igi2StatusMessageSlot> Slots => _slots;

    /// <summary>Gets the lines kept in the computer's log, <c>ComputerLog_ptQVector</c>, oldest first.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>Gets or sets a value indicating whether the game is paused, which holds every line as it is.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Registers a message into the first free place.</summary>
    /// <param name="message">The message, whose current text is the line.</param>
    /// <returns>The place, or <c>-1</c> with all twenty-four taken.</returns>
    public int Register(Igi2StatusMessageTask message)
    {
        ArgumentNullException.ThrowIfNull(message);
        int index = Array.FindIndex(_slots, static s => !s.IsUsed);
        if (index < 0)
        {
            return -1;
        }

        Igi2StatusMessageSlot slot = _slots[index];
        slot.IsUsed = true;
        slot.Message = message;
        slot.Age = 0;
        slot.DoneTicks = 0;
        slot.Fade = 0f;
        slot.Open = 0f;
        message.IsFinishedPrinting = false;

        // The line is copied in at most 4095 letters; a message showing no text leaves the place's last line in it.
        if (message.CurrentText is { } text)
        {
            slot.Text = text.Length > 4095 ? text[..4095] : text;
            if (!message.IsCutsceneMessage)
            {
                _log.Add(text);
            }
        }

        slot.Printed = message.IsCutsceneMessage ? slot.Text.Length : 0;
        return index;
    }

    /// <summary>Frees a place, <c>StatusMessageScreen_RemoveMessage</c> (<c>igi2 0x43DBB0</c>).</summary>
    /// <param name="index">The place.</param>
    public void Remove(int index)
    {
        Igi2StatusMessageSlot slot = _slots[index];
        slot.Message = null;
        slot.IsUsed = false;
    }

    /// <summary>A tick of every place in use, <c>StatusMessageScreen_RunHandler</c> (<c>igi2 0x43D620</c>).</summary>
    public void Run()
    {
        if (IsPaused)
        {
            return;
        }

        foreach (Igi2StatusMessageSlot slot in _slots)
        {
            if (slot.IsUsed)
            {
                Update(slot);
            }
        }
    }

    private static void Update(Igi2StatusMessageSlot slot)
    {
        if (slot.Message is not { } message)
        {
            return;
        }

        if (message.IsCutsceneMessage)
        {
            // Laid out whole on its first tick (StatusMessageScreen_FormatMessageText, not ported), and done from then on.
            slot.DoneTicks++;
        }
        else if (slot.Age >= PrintStart)
        {
            if (slot.Printed < slot.Text.Length)
            {
                if ((slot.Age - PrintStart) % 2 == 0)
                {
                    slot.Printed++;
                }
            }
            else
            {
                slot.DoneTicks++;
            }
        }
        else if (slot.Age >= 2)
        {
            slot.Fade = ((Fpu)slot.Fade + 0.14285715f).F;
        }
        else
        {
            slot.Open = ((Fpu)slot.Open + 0.5f).F;
        }

        if (slot.DoneTicks > DoneTicksToFinish)
        {
            message.IsFinishedPrinting = true;
        }

        slot.Age++;
    }
}
