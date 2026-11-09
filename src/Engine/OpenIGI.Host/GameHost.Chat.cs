using OpenIGI.Game.Multiplayer;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Hud.Remastered.Touch;

namespace OpenIGI.Host;

/// <content>
/// The chat a multiplayer game types and reads: the line being typed -- to everyone or to the team, edited as a text field is,
/// with the lines sent before a key away -- and the history it is typed over, scrolled back while it is open. On a touch screen
/// the browser's keyboard holds the line itself, its return key sends it, and the panel rises above it.
/// </content>
public sealed partial class GameHost
{
    // How many lines the history keeps, how many sent lines Up brings back, and how many fade in the corner while it is shut.
    private const int ChatHistoryLines = 64;
    private const int ChatRecallLines = 20;
    private const int ChatIdleLines = 7;

    // How long the caret is lit and dark, in milliseconds.
    private const int CaretBlinkMilliseconds = 530;

    // Said, newest last: by whom, to whom, and how they stood when they said it.
    private readonly List<ChatHeard> _coopChat = [];

    // The lines this player sent, newest last, for Up to bring back; and which is brought back now (-1 for none).
    private readonly List<string> _chatSent = [];
    private int _chatRecall = -1;

    // The caret in the line being typed, whether it goes to the team, how far the history is scrolled back, when it was last
    // typed in (the caret stays lit while typing), and what a touch screen's keyboard was given to start from.
    private int _chatCaret;
    private bool _chatTeam;
    private int _chatScroll;
    private int _chatTypedAt;
    private string? _chatSeed;

    // Whether the touch chat button was held last frame, so that one tap opens one line.
    private bool _touchChatHeld;

    private readonly record struct ChatHeard(string Name, string Text, ChatKind Kind, int Team, bool Dead, int At);

    /// <summary>Opens a line to type, to everyone or to the team.</summary>
    /// <param name="team">Whether only the team hears it.</param>
    private void OpenChat(bool team)
    {
        if (Talk is null || _session.Mission is null || _menuFlow.MenuMode)
        {
            return;
        }

        _chatEntry = string.Empty;
        _chatCaret = 0;
        _chatTeam = team;
        _chatScroll = 0;
        _chatRecall = -1;
        _chatSeed = string.Empty;
        _chatJustOpened = true;
        _chatTypedAt = _loop.TimeSource.Milliseconds;
    }

    // The editing keys repeat while held, as a text box's do: Backspace held clears the line rather than one character.
    private readonly UI.Focus.KeyRepeater _chatBackspace = new();
    private readonly UI.Focus.KeyRepeater _chatDelete = new();
    private readonly UI.Focus.KeyRepeater _chatLeft = new();
    private readonly UI.Focus.KeyRepeater _chatRight = new();
    private double _chatKeysAt = double.NaN;

    // The line being typed takes the keyboard: characters go in at the caret, the keys that edit edit, Enter says it.
    private void UpdateChatEntry(ISessionTalk talk)
    {
        if (_chatEntry is not { } entry)
        {
            return;
        }

        KeyboardState keys = _window.Input.Keyboard;
        bool control = keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl);
        float wheel = _window.Input.Mouse.WheelDelta;
        int was = entry.Length;
        int caret = Math.Clamp(_chatCaret, 0, entry.Length);

        // A touch screen's keyboard says the whole line as it stands, its own corrections and Backspace in it.
        if (keys.TryTakeLine(out string line))
        {
            entry = Cut(line);
            caret = entry.Length;
        }

        // The key that opened the line is in the queue as a character too; it is not part of what is said.
        while (keys.TryDequeueCharacter(out char typed))
        {
            if (!_chatJustOpened && !char.IsControl(typed))
            {
                Insert(typed.ToString());
            }
        }

        _chatJustOpened = false;
        bool say = false;
        bool cancel = _menuFlow.MenuMode;
        while (keys.TryDequeuePress(out KeyCode key))
        {
            switch (key)
            {
                case KeyCode.Return or KeyCode.NumpadEnter:
                    say = true;
                    break;
                case KeyCode.Escape:
                    cancel = true;
                    break;
                case KeyCode.Tab:
                    _chatTeam = !_chatTeam;
                    break;
                case KeyCode.Home:
                    caret = 0;
                    break;
                case KeyCode.End:
                    caret = entry.Length;
                    break;
                case KeyCode.Up when _chatSent.Count > 0:
                    _chatRecall = _chatRecall < 0 ? _chatSent.Count - 1 : Math.Max(0, _chatRecall - 1);
                    entry = _chatSent[_chatRecall];
                    caret = entry.Length;
                    break;
                case KeyCode.Down when _chatRecall >= 0:
                    _chatRecall = _chatRecall + 1 < _chatSent.Count ? _chatRecall + 1 : -1;
                    entry = _chatRecall < 0 ? string.Empty : _chatSent[_chatRecall];
                    caret = entry.Length;
                    break;
                case KeyCode.PageUp:
                    _chatScroll += 4;
                    break;
                case KeyCode.PageDown:
                    _chatScroll -= 4;
                    break;
                case KeyCode.V when control:
                    Insert(_window.ClipboardText ?? string.Empty);
                    break;
            }
        }

        // The keys that repeat while held: their first press and then each repeat.
        double now = _loop.TimeSource.Milliseconds;
        float elapsed = double.IsNaN(_chatKeysAt) ? 0f : (float)Math.Clamp((now - _chatKeysAt) / 1000.0, 0.0, 0.25);
        _chatKeysAt = now;
        if (_chatBackspace.Poll(keys, KeyCode.Backspace, elapsed) && caret > 0)
        {
            int from = control ? WordStart(entry, caret) : caret - 1;
            entry = entry.Remove(from, caret - from);
            caret = from;
        }

        if (_chatDelete.Poll(keys, KeyCode.Delete, elapsed) && caret < entry.Length)
        {
            int to = control ? WordEnd(entry, caret) : caret + 1;
            entry = entry.Remove(caret, to - caret);
        }

        if (_chatLeft.Poll(keys, KeyCode.Left, elapsed))
        {
            caret = control ? WordStart(entry, caret) : Math.Max(0, caret - 1);
        }

        if (_chatRight.Poll(keys, KeyCode.Right, elapsed))
        {
            caret = control ? WordEnd(entry, caret) : Math.Min(entry.Length, caret + 1);
        }

        if (wheel != 0f)
        {
            _chatScroll += wheel > 0f ? 1 : -1;
        }

        _chatScroll = Math.Clamp(_chatScroll, 0, Math.Max(0, _coopChat.Count - 1));
        _chatCaret = caret;
        if (entry.Length != was || entry != _chatEntry)
        {
            _chatTypedAt = _loop.TimeSource.Milliseconds;
        }

        _chatEntry = say || cancel ? null : entry;
        if (say && entry.Trim().Length > 0)
        {
            talk.Say(entry.Trim(), _chatTeam);
            _chatSent.Remove(entry.Trim());
            _chatSent.Add(entry.Trim());
            if (_chatSent.Count > ChatRecallLines)
            {
                _chatSent.RemoveAt(0);
            }
        }

        // The game and its binds see an idle keyboard and mouse while a line is typed, Escape included.
        _window.Input.Captured = true;

        // Typed text at the caret, a pasted run's line breaks as spaces, and nothing past the longest line said.
        void Insert(string text)
        {
            string clean = new([.. text.Select(c => char.IsControl(c) ? ' ' : c)]);
            int room = SessionTalk.MaxChatLength - entry.Length;
            if (room <= 0 || clean.Length == 0)
            {
                return;
            }

            clean = clean.Length > room ? clean[..room] : clean;
            entry = entry.Insert(caret, clean);
            caret += clean.Length;
        }
    }

    // What the platform's keyboard is told each frame: the line it holds while one is typed, and where a touch raises it.
    private void TellPlatformAboutChat()
    {
        _touchControls.ChatShown = Talk is not null && _session.Mission is not null;
        _window.SetTextLine(_chatEntry is null ? null : _chatSeed ?? string.Empty);
        TouchButtonSpot? chat = _touchControls.ChatShown && _window.Input.Touch.InUse
            ? _touchControls.Layout.FirstOrDefault(s => s.Button == TouchButton.Chat) is { Radius: > 0f } spot ? spot : null
            : null;
        _window.SetTextTrigger(chat is { } c ? (c.Centre.X - c.Radius, c.Centre.Y - c.Radius, c.Radius * 2f, c.Radius * 2f) : null);
    }

    // A line said, kept with who said it as they stood then.
    private void HearChat(ChatLine line, IReadOnlyList<ScoreboardLine> lines)
    {
        ScoreboardLine who = lines.FirstOrDefault(l => l.Entity == line.Entity);
        string name = line.Kind == ChatKind.Server ? string.Empty : who.Name is { Length: > 0 } known ? known : "Player";
        bool dead = line.Kind != ChatKind.Server && who.Name is { Length: > 0 } && !who.IsAlive;
        _coopChat.Add(new ChatHeard(name, line.Text, line.Kind, who.Team, dead, _loop.TimeSource.Milliseconds));
        if (_coopChat.Count > ChatHistoryLines)
        {
            _coopChat.RemoveAt(0);
        }

        // Scrolled back, the history stays on what was being read.
        if (_chatScroll > 0)
        {
            _chatScroll++;
        }
    }

    // The lines the HUD shows: the whole history while a line is typed; otherwise the last few, fading at the end of their time.
    private List<HudChatLine> ChatShown(int now)
    {
        ISessionScoreboard? board = TickObserver as ISessionScoreboard;
        int? self = board?.LocalEntity;
        ScoreboardLine own = board?.CurrentScoreboard?.Lines.FirstOrDefault(l => l.Entity == self) ?? default;
        int ownTeam = own.Name is { Length: > 0 } ? own.Team : -1;
        bool teams = Game.Multiplayer.Modes.GameModes.HasTeams(board?.Mode);
        bool open = _chatEntry is not null;
        var shown = new List<HudChatLine>(open ? _coopChat.Count : ChatIdleLines);
        for (int i = open ? 0 : Math.Max(0, _coopChat.Count - ChatIdleLines); i < _coopChat.Count; i++)
        {
            ChatHeard heard = _coopChat[i];
            float opacity = open ? 1f : Math.Clamp((ChatLineMilliseconds - (now - heard.At)) / 1500f, 0f, 1f);
            if (opacity <= 0f)
            {
                continue;
            }

            bool enemy = heard.Kind != ChatKind.Server && ownTeam >= 0 && (!teams || heard.Team != ownTeam) && heard.Name != own.Name;
            shown.Add(new HudChatLine(heard.Name, heard.Text, opacity, heard.Kind, enemy, heard.Dead));
        }

        return shown;
    }

    // The line being typed as the HUD shows it.
    private HudChatEntry? ChatEntryShown(int now) => _chatEntry is not { } entry
        ? null
        : new HudChatEntry(
            entry,
            Math.Clamp(_chatCaret, 0, entry.Length),
            _chatTeam,
            _chatScroll,
            (now - _chatTypedAt) / CaretBlinkMilliseconds % 2 == 0,
            _window.Input.Touch.InUse,
            _window.TextEntryInset);

    private static string Cut(string line) => line.Length > SessionTalk.MaxChatLength ? line[..SessionTalk.MaxChatLength] : line;

    // Where the word before the caret begins, and where the one after it ends, spaces before it taken with it.
    private static int WordStart(string text, int caret)
    {
        int i = caret;
        while (i > 0 && text[i - 1] == ' ')
        {
            i--;
        }

        while (i > 0 && text[i - 1] != ' ')
        {
            i--;
        }

        return i;
    }

    private static int WordEnd(string text, int caret)
    {
        int i = caret;
        while (i < text.Length && text[i] == ' ')
        {
            i++;
        }

        while (i < text.Length && text[i] != ' ')
        {
            i++;
        }

        return i;
    }
}
