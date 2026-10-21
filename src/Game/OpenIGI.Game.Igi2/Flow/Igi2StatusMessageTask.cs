using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// A line said to the player, I.G.I. 2's <c>StatusMessage</c>: once its script sends it, its sound plays and its text is shown a
/// piece at a time on the status message screen, exporting <c>isSendt</c>, <c>nTickSendt</c>, <c>isFinishedDisplay</c>,
/// <c>nFinishedDisplay</c> and <c>nTicksSinceFinishedDisplay</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its text is a list of keys, each a piece shown in turn: split at a comma, a tab or a line's end, a <c>//</c> commenting out
/// the rest of a line, each key looked up in the language (<c>StatusMessage_UpdateText</c>, <c>igi2 0x50F830</c>). Its duration
/// is shared out among the pieces by their length (<c>StatusMessage_InitText</c>, <c>0x50F9A0</c>); a sound it finds replaces
/// the duration with the sound's own length (<c>StatusMessage_PlaySound</c>, <c>0x50F750</c>).
/// </para>
/// <para>
/// Its tick (<c>StatusMessage_RunHandler</c>, <c>0x50FB30</c>): once sent it is shown, each piece registered on the screen in
/// turn as the last one's share of the duration runs out; when the last is out and the screen has finished printing it, the
/// line ends, its sound is cut, and it counts as finished display. One sent once is then done; another may be sent again. Its
/// children run every tick.
/// </para>
/// <para>
/// A line with no text is never put on the screen and so never finishes: its sound plays out, it stays sent and shown, and its
/// clock restarts each time its duration is out, as retail's does.
/// </para>
/// </remarks>
public sealed partial class Igi2StatusMessageTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "StatusMessage";

    /// <summary>The tick a line is stamped as sent with, so its first piece shows at once: <c>-10000000</c>.</summary>
    public const int SentAtOnce = -10000000;

    /// <summary>The most letters of a key, less its end, <c>255</c>.</summary>
    public const int KeyLength = 255;

    private const string Separators = ",\t\n";
    private const string Comment = "//";

    private LevelExpression? _send;
    private Igi2LevelFlowTask? _flow;
    private Igi2Sound? _sound;
    private int? _next;
    private int _slot = -1;
    private int _ticks;
    private int _tickSent;
    private int _finishedDisplay = -1;
    private int _ticksSinceFinishedDisplay;
    private byte _isDisplay;
    private byte _isSent;
    private byte _isFinishedDisplay;

    /// <summary>Gets or sets where it is.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that sends it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Send")]
    public string SendScript { get; set; } = string.Empty;

    /// <summary>Gets or sets its text, the keys of its pieces; empty for none.</summary>
    [ScriptParam(ScriptParamType.VarString, "Text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the icon its box shows, one of the status screen's four.</summary>
    [ScriptParam(ScriptParamType.String256, "Sprite")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it plays, at the listener.</summary>
    [ScriptParam(ScriptParamType.String256, "Sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it is said only once.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is send once")]
    public bool IsSendOnce { get; set; }

    /// <summary>Gets or sets a value indicating whether it is a cutscene's subtitle, laid out whole and kept out of the log.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Cutscene message")]
    public bool IsCutsceneMessage { get; set; }

    /// <summary>Gets or sets the seconds it is shown, five unless authored; its sound's length once that plays.</summary>
    [ScriptParam(ScriptParamType.Real32, "Duration")]
    public float Duration { get; set; } = 5f;

    /// <summary>Gets or sets the bodies its sound's lip sync moves the faces of (not ported).</summary>
    [ScriptParam(ScriptParamType.String256, "Morph QTask IDs")]
    public string MorphTaskIds { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether its keys are looked up, <c>+0x2C7</c>; a level's always are.</summary>
    public bool IsLocalized { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it is sent without its script and deleted once shown, as the engine's own are.</summary>
    public bool IsForceSend { get; set; }

    /// <summary>Gets or sets a value indicating whether the screen has finished printing its current piece, <c>isFinishedPrinting</c>.</summary>
    public bool IsFinishedPrinting { get; set; }

    /// <summary>Gets the piece it shows, <c>pzText</c>, or <see langword="null"/> before any.</summary>
    public string? CurrentText { get; private set; }

    /// <summary>Gets the number of its pieces.</summary>
    public int PieceCount { get; private set; }

    /// <summary>Gets the letters of all its pieces together, which share out its duration.</summary>
    public int TextLength { get; private set; }

    /// <summary>Gets a value indicating whether it has been sent, <c>isSendt</c>.</summary>
    public bool IsSent => _isSent != 0;

    /// <summary>Gets a value indicating whether it is being shown, <c>isDisplay</c>.</summary>
    public bool IsDisplay => _isDisplay != 0;

    /// <summary>Gets a value indicating whether it has finished being shown, <c>isFinishedDisplay</c>.</summary>
    public bool IsFinishedDisplay => _isFinishedDisplay != 0;

    /// <summary>Gets the times it has finished being shown, less one, <c>nFinishedDisplay</c>.</summary>
    public int FinishedDisplayCount => _finishedDisplay;

    /// <summary>Gets the level tick its current piece was shown on, <c>nTickSendt</c>.</summary>
    public int TickSent => _tickSent;

    /// <summary>Gets the ticks it has been shown, <c>nTicks</c>.</summary>
    public int Ticks => _ticks;

    /// <summary>Gets the screen place its piece is printed in, <c>-1</c> for none.</summary>
    public int Slot => _slot;

    /// <summary>Gets or sets the language its keys are looked up in.</summary>
    public Igi2Strings? Strings { get; set; }

    /// <summary>Gets or sets the screen it is shown on.</summary>
    public Igi2StatusMessageScreen? Screen { get; set; }

    /// <summary>Gets or sets the level's sounds.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets where the listener is, which its sound plays at.</summary>
    public Func<Vector3d>? Listener { get; set; }

    /// <summary>
    /// Counts its pieces and their letters, and stands ready to show the first, <c>StatusMessage_InitText</c>
    /// (<c>igi2 0x50F9A0</c>), which <c>StatusMessage_UpdateInternalDataHandler</c> (<c>0x50FF60</c>) runs once its parameters are
    /// read; here once the language is known.
    /// </summary>
    public void InitText()
    {
        _next = Text.Length > 0 ? 0 : null;
        PieceCount = 0;
        TextLength = 0;
        if (_next is null)
        {
            return;
        }

        do
        {
            PieceCount++;
            UpdateText();
            if (CurrentText is { } piece)
            {
                TextLength += piece.Length;
            }
        }
        while (_next is not null);

        _next = 0;
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _flow = level.LevelFlow;
        if (_isFinishedDisplay != 0)
        {
            _ticksSinceFinishedDisplay++;
            if (IsSendOnce)
            {
                return;
            }
        }

        if (_isDisplay == 0)
        {
            _send ??= Igi2LevelExpressions.Create(SendScript, Name, level.Natives);
            if (IsForceSend || (!_send.IsEmpty && level.Evaluate(_send) != 0.0))
            {
                _tickSent = SentAtOnce;
                _isDisplay = 1;
                _isFinishedDisplay = 0;
                PlaySound();
                _isSent = 1;
            }
            else
            {
                _isDisplay = 0;
            }
        }

        if (_isDisplay != 0)
        {
            // StatusMessage_UpdateMorph (igi2 0x50F660) sets the morph bodies' faces from the sound's lip sync; not ported.
            _ticks++;
            int now = _flow?.Tick ?? 0;
            if (now > unchecked(_tickSent + Wait()))
            {
                if (_next is not null)
                {
                    UpdateText();
                    if (_slot != -1)
                    {
                        Screen?.Remove(_slot);
                    }

                    _slot = Screen?.Register(this) ?? -1;
                    _tickSent = now;
                }
                else
                {
                    if (_tickSent != SentAtOnce && IsFinishedPrinting)
                    {
                        if (_slot != -1)
                        {
                            Screen?.Remove(_slot);
                            _slot = -1;
                        }

                        KillSound();
                        if (IsForceSend)
                        {
                            Tree.Delete(this);
                            return;
                        }

                        _ticks = 0;
                        _isDisplay = 0;
                        _isFinishedDisplay = 1;
                        _finishedDisplay++;
                        _next = Text.Length > 0 ? 0 : null;
                    }

                    _tickSent = now;
                }
            }
        }

        level.RunChildren(this);
    }

    /// <summary>Exports its five (<c>StatusMessage_RegisterScriptSymbolsHandler</c>, <c>igi2 0x50FF80</c>).</summary>
    /// <param name="variables">The level's variables.</param>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isSendt", () => _isSent, value => _isSent = (byte)value, sizeof(byte));
        variables.BindInt(this, "nTickSendt", () => _tickSent, value => _tickSent = value);
        variables.BindInt(this, "isFinishedDisplay", () => _isFinishedDisplay, value => _isFinishedDisplay = (byte)value, sizeof(byte));
        variables.BindInt(this, "nFinishedDisplay", () => _finishedDisplay, value => _finishedDisplay = value);
        variables.BindInt(this, "nTicksSinceFinishedDisplay", () => _ticksSinceFinishedDisplay, value => _ticksSinceFinishedDisplay = value);
    }

    // x87 fistp: a value out of range, or not a number, is the integer indefinite.
    private static int Store(Fpu value)
    {
        double v = Math.Truncate(value.Value);
        return double.IsNaN(v) || v < int.MinValue || v > int.MaxValue ? int.MinValue : (int)v;
    }

    private static int Cspn(string s, int at, string set)
    {
        int i = at;
        while (i < s.Length && set.IndexOf(s[i], StringComparison.Ordinal) < 0)
        {
            i++;
        }

        return i - at;
    }

    private static int? Pbrk(string s, int at, string set)
    {
        int i = at + Cspn(s, at, set);
        return i < s.Length ? i : null;
    }

    // The current piece's share of the duration in ticks: the game's frequency times the seconds, times the piece's letters over
    // all the letters; the whole duration where nothing is shown.
    private int Wait()
    {
        Fpu ticks = (Fpu)(double)Igi2Sounds.Frequency * Duration;
        return CurrentText is { } piece
            ? Store((ticks * (double)(uint)piece.Length) / (double)TextLength)
            : Store(ticks);
    }

    // StatusMessage_UpdateText (igi2 0x50F830): the next key's text, and where the key after it starts.
    private void UpdateText()
    {
        if (_next is not int at)
        {
            return;
        }

        string text = Text;
        if (!IsLocalized)
        {
            CurrentText = text[at..];
            _next = null;
            return;
        }

        // The key runs to the first separator, or to a comment anywhere after it, at most 255 letters.
        int length = Cspn(text, at, Separators);
        int comment = text.IndexOf(Comment, at, StringComparison.Ordinal);
        if (comment >= 0)
        {
            length = comment - at;
        }

        if (length > KeyLength)
        {
            length = KeyLength;
        }

        string key = text.Substring(at, Math.Min(length, text.Length - at));
        CurrentText = Strings?.Get(key) ?? key;

        int? next = Pbrk(text, at, Separators);
        while (next is int p)
        {
            while (p < text.Length && (Separators.Contains(text[p], StringComparison.Ordinal) || text[p] == ' '))
            {
                p++;
            }

            if (string.CompareOrdinal(text, p, Comment, 0, Comment.Length) != 0)
            {
                next = p < text.Length ? p : null;
                break;
            }

            // A comment runs to its line's end.
            next = Pbrk(text, p, "\n") is int end ? Pbrk(text, end, Separators) : null;
        }

        _next = next;
    }

    private void PlaySound()
    {
        if (Sound.Length == 0 || _sound is not null || Sounds is not { } sounds)
        {
            return;
        }

        Vector3d at = Listener?.Invoke() ?? Position;
        if (sounds.New(Sound, at, Listener) is { } sound)
        {
            _sound = sound;

            // Its length, a whole number of ticks, over the frequency.
            Duration = ((Fpu)(float)sounds.LengthTicks(Sound) / (double)Igi2Sounds.Frequency).F;
        }
    }

    private void KillSound()
    {
        _sound?.Delete();
        _sound = null;
    }
}
