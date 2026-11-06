using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// An <c>AnimTask</c>'s <c>AnimData</c> edited as keys on a timeline: when each value is set, on which of its target's
/// channels (named for the target, Thrust, Steering, Collective…), and to what, with the whole programme drawn as lanes.
/// </summary>
internal sealed class AnimDataEditor : EditorElement
{
    // The key rows shown at once; more scroll.
    private const int VisibleKeys = 10;

    // The strip's height in grid rows.
    private const int StripLines = 3;

    private readonly Func<IReadOnlyList<QscExpression>> _get;
    private readonly Action<IReadOnlyList<QscExpression>> _set;
    private readonly Func<string> _targetType;
    private readonly Action _relayout;
    private readonly Button _add;
    private readonly NumberField _length;
    private readonly List<KeyRow> _rows = [];
    private AnimTimeline _timeline = new();
    private string _shown = string.Empty;
    private string _type = string.Empty;
    private int _first;
    private int _hovered = -1;
    private UiRect _strip;

    /// <summary>Initializes a new instance of the <see cref="AnimDataEditor"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="get">Reads the parameter's arguments.</param>
    /// <param name="set">Writes them.</param>
    /// <param name="targetType">Reads the target's task type, which names its channels.</param>
    /// <param name="relayout">Asks for the row to be laid out again, after keys were added or removed.</param>
    public AnimDataEditor(EditorTheme theme, Func<IReadOnlyList<QscExpression>> get, Action<IReadOnlyList<QscExpression>> set, Func<string> targetType, Action relayout)
        : base(theme)
    {
        _get = get;
        _set = set;
        _targetType = targetType;
        _relayout = relayout;
        _add = new Button(theme, "Key", KitIcon.Plus) { Style = ButtonStyle.Flat, Tooltip = "Add a key at the end, on the first channel the target reads" };
        _add.Clicked += AddKey;
        _length = new NumberField(theme) { Step = 1.0 / AnimTimeline.TicksPerSecond, Decimals = 2, Minimum = 0, Tooltip = "When the programme ends, in seconds; never before its last key" };
        _length.ValueChanged += (seconds, final) =>
        {
            if (final)
            {
                Change(() => _timeline.Length = Math.Max(Ticks(seconds), _timeline.LastKeyTick), shape: false);
            }
        };
        AddChild(_add);
        AddChild(_length);
        Load(announce: false);
    }

    /// <summary>Gets how many grid rows it wants: a bar, the strip and the key rows.</summary>
    public int Lines => 1 + StripLines + Math.Max(1, Math.Min(_timeline.Keys.Count, VisibleKeys));

    /// <summary>Gets whether a parameter's arguments read as a timeline; one that does not is shown as written.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>Whether this editor can take it.</returns>
    public static bool Takes(IReadOnlyList<QscExpression> arguments) => AnimTimeline.TryParse(arguments, out _);

    /// <summary>Pulls the programme in again when it changed from outside (an undo).</summary>
    public void Refresh() => Load(announce: true);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float line = Theme.RowHeight + Theme.Px(4f);
        float gap = Theme.Px(4f);
        _add.Place(context, new UiRect(contentRect.Right - Theme.Px(60f), contentRect.Y, Theme.Px(60f), Theme.ControlHeight));
        _length.Place(context, new UiRect(contentRect.Right - Theme.Px(170f), contentRect.Y, Theme.Px(104f), Theme.ControlHeight));
        _strip = new UiRect(contentRect.X, contentRect.Y + line, contentRect.Width, (line * StripLines) - gap);

        float y = _strip.Bottom + gap;
        float right = contentRect.Right - Theme.Px(10f);
        for (int i = 0; i < _rows.Count; i++)
        {
            KeyRow row = _rows[i];
            bool shown = i >= _first && i < _first + VisibleKeys;
            if (!shown)
            {
                row.Place(context, default, default, default, default, default);
                continue;
            }

            float x = contentRect.X;
            UiRect time = new(x, y, Theme.Px(70f), Theme.ControlHeight);
            x = time.Right + gap;
            float valueWidth = Theme.Px(78f);
            float holdWidth = Theme.Px(44f);
            float removeWidth = Theme.Px(20f);
            UiRect channel = new(x, y, MathF.Max(Theme.Px(40f), right - x - valueWidth - holdWidth - removeWidth - (gap * 3f)), Theme.ControlHeight);
            UiRect value = new(channel.Right + gap, y, valueWidth, Theme.ControlHeight);
            UiRect hold = new(value.Right + gap, y, holdWidth, Theme.ControlHeight);
            UiRect remove = new(hold.Right + gap, y, removeWidth, Theme.ControlHeight);
            row.Place(context, time, channel, value, hold, remove);
            row.Top = y;
            y += line;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiRect bar = new(Bounds.X, Bounds.Y, Bounds.Width - Theme.Px(232f), Theme.ControlHeight);
        Small(painter, "ends at, s", new UiRect(_length.Bounds.X - Theme.Px(60f), Bounds.Y, Theme.Px(56f), Theme.ControlHeight), Theme.TextDisabled, end: true);
        string what = _type.Length == 0
            ? "No target"
            : AnimTimeline.Drives(_type) ? $"Drives a {_type}" : $"A {_type} reads no channels: this does nothing to it";
        EditorDraw.Label(painter, Theme, $"{what}  •  {_timeline.Keys.Count} key(s)", bar, AnimTimeline.Drives(_type) ? Theme.TextMuted : Theme.Warning);
        DrawStrip(painter);

        if (_timeline.Keys.Count > VisibleKeys)
        {
            int last = Math.Min(_timeline.Keys.Count, _first + VisibleKeys);
            UiRect more = new(Bounds.X, Bounds.Bottom - Theme.Px(12f), Bounds.Width, Theme.Px(12f));
            Small(painter, $"Keys {_first + 1}–{last} of {_timeline.Keys.Count}: scroll for more", more, Theme.TextDisabled, end: true);
        }
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        if (_timeline.Keys.Count <= VisibleKeys || position.Y < _strip.Bottom)
        {
            return false;
        }

        int first = Math.Clamp(_first - (int)MathF.Round(notches * 3f), 0, _timeline.Keys.Count - VisibleKeys);
        if (first != _first)
        {
            _first = first;
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        int hovered = -1;
        for (int i = _first; i < Math.Min(_rows.Count, _first + VisibleKeys); i++)
        {
            if (e.Position.Y >= _rows[i].Top && e.Position.Y < _rows[i].Top + Theme.ControlHeight)
            {
                hovered = i;
            }
        }

        _hovered = hovered;
    }

    // The programme as lanes, one per channel it sets: a key is a dot, and a line runs on at its value until the next.
    private void DrawStrip(UiPainter painter)
    {
        EditorDraw.Fill(painter, Theme, _strip, Theme.Input);
        int length = Math.Max(1, Math.Max(_timeline.Length, _timeline.LastKeyTick));
        int[] channels = [.. _timeline.Channels];
        float labelWidth = Theme.Px(78f);
        UiRect lanes = UiRect.FromEdges(_strip.X + labelWidth, _strip.Y + Theme.Px(4f), _strip.Right - Theme.Px(8f), _strip.Bottom - Theme.Px(12f));
        float X(int tick) => lanes.X + (lanes.Width * tick / length);

        // Second marks, thinned to keep them apart.
        int step = AnimTimeline.TicksPerSecond * Math.Max(1, (int)MathF.Ceiling(length / (float)AnimTimeline.TicksPerSecond / 12f));
        for (int tick = 0; tick <= length; tick += step)
        {
            painter.FillRect(new UiRect(X(tick), lanes.Y, 1f, lanes.Height), Theme.Border);
            Small(painter, AnimTimeline.Seconds(tick), new UiRect(X(tick) + Theme.Px(2f), lanes.Bottom, Theme.Px(60f), Theme.Px(12f)), Theme.TextDisabled);
        }

        if (channels.Length == 0)
        {
            EditorDraw.Label(painter, Theme, "No keys: add one with + Key", lanes, Theme.TextDisabled);
            return;
        }

        float laneHeight = lanes.Height / channels.Length;
        for (int c = 0; c < channels.Length; c++)
        {
            float mid = lanes.Y + (laneHeight * (c + 0.5f));
            Bgra32 colour = LaneColour(c);
            Small(painter, AnimTimeline.ChannelName(_type, channels[c]), new UiRect(_strip.X + Theme.Px(6f), mid - (laneHeight * 0.5f), labelWidth - Theme.Px(8f), laneHeight), colour);
            AnimKey[] keys = [.. _timeline.Keys.Where(k => k.Channel == channels[c]).OrderBy(static k => k.Tick)];
            for (int k = 0; k < keys.Length; k++)
            {
                int end = k + 1 < keys.Length ? keys[k + 1].Tick : length;
                if (keys[k].Value != AnimTimeline.Hold)
                {
                    painter.FillRect(new UiRect(X(keys[k].Tick), mid - Theme.Px(1f), MathF.Max(1f, X(end) - X(keys[k].Tick)), Theme.Px(2f)), colour.WithAlpha(150));
                }

                bool hot = _hovered >= 0 && _hovered < _timeline.Keys.Count && ReferenceEquals(_timeline.Keys[_hovered], keys[k]);
                painter.FillDot(new UiVec2(X(keys[k].Tick), mid), Theme.Px(hot ? 4.5f : 3f), hot ? Theme.Accent : colour);
            }
        }
    }

    // Small text in a box, vertically centred, at its left or its right.
    private void Small(UiPainter painter, string text, UiRect box, Bgra32 colour, bool end = false)
    {
        OpenIGI.UI.Text.UiFont font = Theme.SmallFont;
        float size = Theme.SmallTextSize;
        float width = font.MeasureWidth(text, size);
        float x = end ? box.Right - width : box.X;
        float y = MathF.Round(box.Y + ((box.Height - font.LineHeight(size)) * 0.5f));
        painter.DrawText(font, text, new UiVec2(MathF.Round(x), y), size, colour);
    }

    private static Bgra32 LaneColour(int lane) => (lane % 6) switch
    {
        0 => Bgra32.FromRgb(110, 170, 240),
        1 => Bgra32.FromRgb(240, 170, 90),
        2 => Bgra32.FromRgb(120, 210, 140),
        3 => Bgra32.FromRgb(220, 120, 200),
        4 => Bgra32.FromRgb(230, 220, 110),
        _ => Bgra32.FromRgb(150, 150, 230),
    };

    private void Load(bool announce)
    {
        IReadOnlyList<QscExpression> arguments = _get();
        string text = string.Join(", ", arguments.Select(QscPrinter.Print));
        string type = _targetType();
        if (text == _shown && type == _type)
        {
            return;
        }

        _shown = text;
        _type = type;
        if (!AnimTimeline.TryParse(arguments, out _timeline))
        {
            _timeline = new AnimTimeline();
        }
        int before = _rows.Count;
        Rebuild();
        if (announce && Math.Min(before, VisibleKeys) != Math.Min(_rows.Count, VisibleKeys))
        {
            _relayout();
        }
    }

    private void Rebuild()
    {
        foreach (KeyRow row in _rows)
        {
            row.Detach(this);
        }

        _rows.Clear();
        int[] order = [.. AnimTimeline.ChannelOrder(_type)];
        string[] names = [.. order.Select(c => AnimTimeline.ChannelName(_type, c))];
        foreach (AnimKey key in _timeline.Keys)
        {
            _rows.Add(new KeyRow(this, key, order, names));
        }

        _first = Math.Clamp(_first, 0, Math.Max(0, _rows.Count - VisibleKeys));
        _length.Value = Math.Max(_timeline.Length, _timeline.LastKeyTick) / (double)AnimTimeline.TicksPerSecond;
    }

    private void AddKey()
    {
        int channel = AnimTimeline.ChannelOrder(_type).First();
        AnimKey? last = _timeline.Keys.LastOrDefault(k => k.Channel == channel);
        Change(
            () =>
            {
                _timeline.Keys.Add(new AnimKey { Tick = Math.Max(_timeline.Length, _timeline.LastKeyTick), Channel = channel, Value = last?.Value ?? 0, IsReal = last?.IsReal ?? true });
                _first = Math.Max(0, _timeline.Keys.Count - VisibleKeys);
            },
            shape: true);
    }

    // A change: the keys put in order, written at once, and the rows made again when keys came or went or moved.
    private void Change(Action change, bool shape)
    {
        int before = _timeline.Keys.Count;
        change();
        _timeline.Sort();
        IReadOnlyList<QscExpression> arguments = _timeline.ToArguments();
        _shown = string.Join(", ", arguments.Select(QscPrinter.Print));
        _set(arguments);
        if (shape)
        {
            Rebuild();
            if (Math.Min(before, VisibleKeys) != Math.Min(_timeline.Keys.Count, VisibleKeys))
            {
                _relayout();
            }
        }
    }

    private static int Ticks(double seconds) => (int)Math.Round(seconds * AnimTimeline.TicksPerSecond);

    // One key's widgets: when, which channel, the value or hold, and remove.
    private sealed class KeyRow
    {
        private readonly NumberField _time;
        private readonly Dropdown _channel;
        private readonly NumberField _value;
        private readonly Button _hold;
        private readonly Button _remove;

        public KeyRow(AnimDataEditor owner, AnimKey key, int[] order, string[] names)
        {
            EditorTheme theme = owner.Theme;
            _time = new NumberField(theme) { Step = 1.0 / AnimTimeline.TicksPerSecond, Decimals = 2, Minimum = 0, Tooltip = "When the value is set, in seconds" };
            _time.Value = key.Tick / (double)AnimTimeline.TicksPerSecond;
            _time.ValueChanged += (seconds, final) =>
            {
                if (final)
                {
                    owner.Change(() => key.Tick = Ticks(seconds), shape: true);
                }
            };

            _channel = new Dropdown(theme) { Options = names, SelectedIndex = Array.IndexOf(order, key.Channel), Tooltip = "The target's channel it sets" };
            _channel.SelectionChanged += index => owner.Change(() => key.Channel = order[index], shape: true);

            bool held = key.Value == AnimTimeline.Hold;
            _value = new NumberField(theme) { Step = 0.01, Decimals = 3, IsEnabled = !held, Tooltip = "The value it is set to" };
            _value.Value = held ? 0 : key.Value;
            _value.ValueChanged += (value, final) => owner.Change(() => key.Value = value, shape: final);

            _hold = new Button(theme, "hold") { IsToggle = true, IsChecked = held, Tooltip = "Leave the channel as the target has it (-1)" };
            _hold.Clicked += () => owner.Change(() => key.Value = key.Value == AnimTimeline.Hold ? 0 : AnimTimeline.Hold, shape: true);

            _remove = new Button(theme, string.Empty, KitIcon.Close) { Style = ButtonStyle.Flat, Tooltip = "Remove the key" };
            _remove.Clicked += () => owner.Change(() => owner._timeline.Keys.Remove(key), shape: true);

            foreach (EditorElement widget in Widgets)
            {
                owner.AddChild(widget);
            }
        }

        public float Top { get; set; }

        private IEnumerable<EditorElement> Widgets => [_time, _channel, _value, _hold, _remove];

        public void Place(LayoutContext context, UiRect time, UiRect channel, UiRect value, UiRect hold, UiRect remove)
        {
            Visibility visibility = time.Width > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (EditorElement widget in Widgets)
            {
                widget.Visibility = visibility;
            }

            _time.Place(context, time);
            _channel.Place(context, channel);
            _value.Place(context, value);
            _hold.Place(context, hold);
            _remove.Place(context, remove);
        }

        public void Detach(AnimDataEditor owner)
        {
            foreach (EditorElement widget in Widgets)
            {
                owner.RemoveChild(widget);
            }
        }
    }
}
