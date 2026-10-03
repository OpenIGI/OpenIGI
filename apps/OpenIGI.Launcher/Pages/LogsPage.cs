using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Launcher.Services;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The session's log: what the launcher did, and what the game it started printed, newest at the bottom.
/// Filtered by level and by text, copied to the clipboard, or cleared.
/// </summary>
internal sealed class LogsPage
{
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

    private readonly LauncherModel _model;
    private readonly ILauncherWindow _window;
    private readonly LogLevelFilter _filter;
    private readonly TextBox _query;
    private readonly Button _copy;
    private readonly LogList _list;
    private readonly List<LogRecord> _records = [];
    private readonly List<LogRow> _rows = [];
    private long _launcherVersion = -1;
    private long _gameVersion = -1;
    private int _shownBucket = -1;
    private string _shownQuery = string.Empty;
    private long _copiedAt;

    public LogsPage(UiTheme theme, LauncherModel model, ILauncherWindow window)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _window = window;

        _copy = HeaderButton(theme, "Copy", ButtonAccent.Ghost);
        _copy.Activated += _ => Copy();
        Button folder = HeaderButton(theme, "Open log folder", ButtonAccent.Ghost);
        folder.Activated += _ => model.OpenLogFolder();
        Button clear = HeaderButton(theme, "Clear", ButtonAccent.Destructive);
        clear.Activated += _ => model.ClearLog();

        var buttons = new FlexPanel { Gap = 8f, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Add(_copy);
        buttons.Add(folder);
        buttons.Add(clear);

        var header = new Grid();
        header.ColumnDefinitions.Add(GridLength.Star);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.RowDefinitions.Add(GridLength.Auto);
        header.Add(new SectionTitle(theme, "Launcher · session", "Logs") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        header.Add(buttons, 0, 1);

        _filter = new LogLevelFilter(theme);
        _query = new TextBox(theme) { Placeholder = "Filter log output", MaxLength = 200, Height = 44f };
        var tools = new FlexPanel { Gap = 10f };
        tools.Add(_filter);
        tools.Add(_query, grow: 1f);

        _list = new LogList(theme, _rows, _records);
        var panel = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(5, 6, 7, 209),
            Blur = 18f,
            Border = Bgra32.FromRgba(255, 255, 255, 20),
        };
        var inset = new Grid { Padding = new Thickness(0f, 14f) };
        inset.ColumnDefinitions.Add(GridLength.Star);
        inset.RowDefinitions.Add(GridLength.Star);
        inset.Add(_list, 0, 0);
        panel.AddChild(inset);

        var page = new Grid { RowSpacing = 18f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(header, 0, 0);
        page.Add(tools, 1, 0);
        page.Add(panel, 2, 0);
        Root = page;
    }

    public UiElement Root { get; }

    /// <summary>Gets how many errors the session has logged, for the tab's badge.</summary>
    public int Errors => _filter.Counts[LogLevelFilter.Error];

    /// <summary>Re-reads the logs when they, the level filter or the text filter have changed.</summary>
    public void Update()
    {
        LauncherLog log = _model.Log;
        if (log.Launcher.Version != _launcherVersion || log.Game.Version != _gameVersion
            || _filter.Selected != _shownBucket || !string.Equals(_query.Text, _shownQuery, StringComparison.Ordinal))
        {
            _launcherVersion = log.Launcher.Version;
            _gameVersion = log.Game.Version;
            _shownBucket = _filter.Selected;
            _shownQuery = _query.Text;
            Rebuild(log);
        }

        _copy.Text = _copiedAt != 0 && Stopwatch.GetElapsedTime(_copiedAt) < CopiedFor ? "Copied" : "Copy";
    }

    private static Button HeaderButton(UiTheme theme, string text, ButtonAccent accent) =>
        new(theme, text) { Accent = accent, TextSize = 17f, Padding = new Thickness(18f, 10f) };

    // The launcher's own records and the game's, merged in time order, then expanded into one row per line.
    private void Rebuild(LauncherLog log)
    {
        _records.Clear();
        _records.AddRange(log.Launcher.Snapshot());
        _records.AddRange(log.Game.Snapshot());
        _records.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));

        Array.Clear(_filter.Counts);
        _rows.Clear();
        string query = _shownQuery.Trim();
        for (int i = 0; i < _records.Count; i++)
        {
            LogRecord record = _records[i];
            int bucket = LogLevelFilter.BucketOf(record.Level);
            _filter.Counts[LogLevelFilter.All]++;
            _filter.Counts[bucket]++;

            if ((_shownBucket != LogLevelFilter.All && bucket != _shownBucket)
                || (query.Length > 0
                    && !record.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !LogFormatter.SingleLine(record).Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            bool first = true;
            foreach (string line in LogFormatter.SingleLine(record).Split('\n'))
            {
                _rows.Add(new LogRow(i, line.TrimEnd('\r'), first));
                first = false;
            }
        }

        _list.RowCount = _rows.Count;
    }

    private void Copy()
    {
        StringBuilder text = new();
        using StringWriter writer = new(text, CultureInfo.InvariantCulture);
        foreach (LogRecord record in _records)
        {
            LogFormatter.WriteFileLine(writer, record);
        }

        _window.ClipboardText = text.ToString();
        _copiedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>One drawn line: a record's first line carries its time, level and source; the rest only text.</summary>
    private readonly record struct LogRow(int Record, string Text, bool First);

    /// <summary>The rows, drawn as the design's four columns.</summary>
    private sealed class LogList : VirtualListView
    {
        private const float LineHeight = 24f;
        private const float TextSize = 16f;
        private const float Inset = 20f;
        private const float Gap = 14f;
        private const float TimeColumn = 110f;
        private const float LevelColumn = 70f;
        private const float SourceColumn = 120f;

        private static readonly Bgra32 TimeColour = Bgra32.FromRgb(90, 92, 95);
        private static readonly Bgra32 ErrorText = Bgra32.FromRgb(255, 184, 166);
        private static readonly Bgra32 ErrorRow = Bgra32.FromRgba(255, 122, 102, 15);

        private readonly UiTheme _theme;
        private readonly List<LogRow> _rows;
        private readonly List<LogRecord> _records;

        public LogList(UiTheme theme, List<LogRow> rows, List<LogRecord> records)
            : base(theme)
        {
            _theme = theme;
            _rows = rows;
            _records = records;
            AnchorToBottom = true;
            DrawRow = Draw;
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
        {
            RowHeight = context.Px(LineHeight);
            return base.MeasureContent(context, available);
        }

        private void Draw(UiPainter painter, int index, UiRect rect)
        {
            LogRow row = _rows[index];
            LogRecord record = _records[row.Record];
            LayoutContext context = painter.Context;
            float size = context.Px(TextSize);
            float y = UiRowStyle.CenterY(_theme.Mono, size, rect);

            if (record.Level >= LogLevel.Error)
            {
                painter.FillRect(rect, ErrorRow);
            }

            float x = rect.X + context.Px(Inset);
            if (row.First)
            {
                painter.DrawText(_theme.Mono, record.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture), new UiVec2(x, y), size, TimeColour);
                x += context.Px(TimeColumn + Gap);
                painter.DrawText(_theme.Mono, LevelLabel(record.Level), new UiVec2(x, y), size, LevelColour(record.Level));
                x += context.Px(LevelColumn + Gap);
                UiRect source = new(x, rect.Y, context.Px(SourceColumn), rect.Height);
                painter.PushClip(source);
                painter.DrawText(_theme.Mono, $"[{record.Category.ToLowerInvariant()}]", new UiVec2(x, y), size, UiPalette.TextMuted);
                painter.PopClip();
                x += context.Px(SourceColumn + Gap);
            }
            else
            {
                x += context.Px(TimeColumn + LevelColumn + SourceColumn + (Gap * 3f));
            }

            painter.DrawText(_theme.Mono, row.Text, new UiVec2(x, y), size, MessageColour(record.Level));
        }

        private static string LevelLabel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            _ => "ERROR",
        };

        private static Bgra32 LevelColour(LogLevel level) => level switch
        {
            LogLevel.Trace or LogLevel.Debug => UiPalette.TextDim,
            LogLevel.Information => UiPalette.Info,
            LogLevel.Warning => UiPalette.Accent,
            _ => UiPalette.Danger,
        };

        private static Bgra32 MessageColour(LogLevel level) => level switch
        {
            LogLevel.Trace or LogLevel.Debug => UiPalette.TextMuted,
            LogLevel.Information => UiPalette.TextBody,
            LogLevel.Warning => UiPalette.Warning,
            _ => ErrorText,
        };
    }

    /// <summary>The level filter: ALL, INFO, WARN and ERROR, each with its marker and how many records it holds.</summary>
    private sealed class LogLevelFilter : Control
    {
        public const int All = 0;
        public const int Info = 1;
        public const int Warn = 2;
        public const int Error = 3;

        private const float Pad = 4f;
        private const float SegmentHeight = 34f;
        private const float SegmentPadding = 14f;
        private const float Dot = 7f;
        private const float Spacing = 8f;
        private const float CaptionSize = 17f;
        private const float CountSize = 14f;

        private static readonly string[] Captions = ["ALL", "INFO", "WARN", "ERROR"];
        private static readonly Bgra32[] Dots = [UiPalette.Text, UiPalette.Info, UiPalette.Accent, UiPalette.Danger];

        private readonly UiRect[] _segments = new UiRect[Captions.Length];
        private int _hovered = -1;

        public LogLevelFilter(UiTheme theme)
            : base(theme)
        {
        }

        /// <summary>Gets how many records each segment holds, refilled by the page.</summary>
        public int[] Counts { get; } = new int[Captions.Length];

        /// <summary>Gets the selected segment.</summary>
        public int Selected { get; private set; }

        public static int BucketOf(LogLevel level) =>
            level >= LogLevel.Error ? Error : level == LogLevel.Warning ? Warn : Info;

        public override bool HandleNavigate(NavDirection direction)
        {
            switch (direction)
            {
                case NavDirection.Left when Selected > 0:
                    Selected--;
                    return true;
                case NavDirection.Right when Selected < Captions.Length - 1:
                    Selected++;
                    return true;
                default:
                    return false;
            }
        }

        public override void ActivateAt(UiVec2 point)
        {
            int index = IndexAt(point);
            if (index >= 0)
            {
                Selected = index;
            }

            Activate();
        }

        public override void PointerMoved(UiVec2 point) => _hovered = IndexAt(point);

        protected override void OnPointerOverChanged(bool over)
        {
            if (!over)
            {
                _hovered = -1;
            }
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
        {
            float width = context.Px(Pad);
            for (int i = 0; i < Captions.Length; i++)
            {
                width += SegmentWidth(context, i) + context.Px(Pad);
            }

            return new UiVec2(width, context.Px(SegmentHeight + (Pad * 2f)));
        }

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
        {
            float x = contentRect.X + context.Px(Pad);
            for (int i = 0; i < Captions.Length; i++)
            {
                float width = SegmentWidth(context, i);
                _segments[i] = new UiRect(x, contentRect.Y + context.Px(Pad), width, context.Px(SegmentHeight));
                x += width + context.Px(Pad);
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float appear = Visual.Appear;
            painter.FillRect(Bounds, UiPalette.Fade(UiPalette.Ink.WithAlpha(168), appear));
            painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(Interpolate.Lerp(Bgra32.FromRgba(255, 255, 255, 20), UiPalette.AccentAt(0.45f), Visual.Focus), appear));

            float caption = context.Px(CaptionSize);
            float count = context.Px(CountSize);
            for (int i = 0; i < Captions.Length; i++)
            {
                UiRect segment = _segments[i];
                bool selected = i == Selected;
                if (selected)
                {
                    painter.FillRect(segment, UiPalette.Fade(Bgra32.FromRgba(255, 255, 255, 26), appear));
                }

                Bgra32 text = selected || i == _hovered ? UiPalette.TextBright : UiPalette.TextMuted;
                float x = segment.X + context.Px(SegmentPadding);
                float dot = context.Px(Dot);
                painter.FillRect(new UiRect(x, segment.Center.Y - (dot * 0.5f), dot, dot), UiPalette.Fade(Dots[i], appear));
                x += dot + context.Px(Spacing);

                UiVec2 end = painter.DrawLabel(Theme.Heading, Captions[i], new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, caption, segment)), caption, UiPalette.Fade(text, appear), Theme.LabelTracking);
                painter.DrawText(
                    Theme.Mono,
                    CountText(i),
                    new UiVec2(end.X + context.Px(Spacing), UiRowStyle.CenterY(Theme.Mono, count, segment)),
                    count,
                    UiPalette.Fade(text, appear * 0.7f));
            }
        }

        private string CountText(int index) => Counts[index].ToString(CultureInfo.InvariantCulture);

        private float SegmentWidth(LayoutContext context, int index) =>
            context.Px(SegmentPadding * 2f) + context.Px(Dot + (Spacing * 2f))
            + UiPainter.MeasureLabel(Theme.Heading, Captions[index], context.Px(CaptionSize), Theme.LabelTracking)
            + UiPainter.MeasureLabel(Theme.Mono, CountText(index), context.Px(CountSize), 0f);

        private int IndexAt(UiVec2 point)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                if (_segments[i].Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
