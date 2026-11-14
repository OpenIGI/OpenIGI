using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The shell's landing page: the mission the operative is up to, one press from being played, over the
/// slowly-orbiting showcase level; and under it the ways into multiplayer, the settings and the profiles.
/// </summary>
/// <remarks>
/// A returning player wants one thing nine times in ten: to carry on. So that is the page -- the mission's name as
/// large as the screen allows, and Continue under it holding the focus, so Enter from a cold start is the game.
/// </remarks>
public sealed class MainMenuScreen : ShellScreen
{
    private PrimaryAction? _continue;

    /// <summary>Initializes a new instance of the <see cref="MainMenuScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MainMenuScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    /// <remarks>The landing page is the level and the bar over it: here the picture stays sharp.</remarks>
    public override float BackdropBlur => 0f;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("Q E", "Sections"), new("Enter", "Select")];

    /// <inheritdoc/>
    protected override string FooterNote => BuildStamp.Version.Length > 0 ? $"OpenIGI {BuildStamp.Version}" : "OpenIGI";

    /// <inheritdoc/>
    protected override Control? InitialFocus => _continue;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        bool compact = Size == ShellSize.Compact;
        bool keys = !Context.Host.TouchScreen;

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = compact ? 10f : 18f,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // The mission the profile is up to in the game's own campaign; with no missions found, the ways in alone.
        (int done, int total) = CampaignProgress();
        MissionInfo? mission = Context.Missions.Where(static m => m.Campaign is null).Skip(done).FirstOrDefault();
        _continue = null;
        if (mission is not null)
        {
            bool fresh = done == 0;
            column.AddChild(new Hero(theme, Size)
            {
                Eyebrow = string.Create(CultureInfo.InvariantCulture, $"{(fresh ? "START" : "CONTINUE")} CAMPAIGN · MISSION {done + 1} OF {total}"),
                Title = mission.Name,
                Briefing = mission.Description,
                Difficulty = DifficultyName(),
                Done = done,
                Total = total,
            });

            _continue = new PrimaryAction(theme, fresh ? "Start" : "Continue")
            {
                Key = keys && !compact ? "Enter" : string.Empty,
                Width = Size switch { ShellSize.Wide => 460f, ShellSize.Medium => 420f, _ => 300f },
            };
            _continue.Activated += _ => Context.Host.StartMission(mission.Id, Context.Config.Active?.Difficulty ?? 1);

            var select = new PrimaryAction(theme, "Mission select") { Secondary = true, Icon = PrimaryActionIcon.None, TitleSize = 19f };
            select.Activated += _ => GoTo(ShellTab.Campaign);
            if (compact)
            {
                foreach (PrimaryAction action in new[] { _continue, select })
                {
                    action.Height = 44f;
                    action.TitleSize = 16f;
                    action.CutCorner = 10f;
                    action.SidePadding = 16f;
                }
            }

            var actions = new FlexPanel
            {
                Orientation = Orientation.Horizontal,
                Gap = compact ? 10f : 16f,
                Margin = new Thickness(0f, compact ? 0f : 8f, 0f, 0f),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            actions.Add(_continue);
            actions.Add(select);
            column.AddChild(actions);
        }

        column.AddChild(BuildWaysIn(theme, compact));

        var page = new Canvas();
        if (compact)
        {
            // No header to hold them here: which game this is, and the way out of it, across the top.
            var top = new FlexPanel { Orientation = Orientation.Horizontal };
            top.Add(BuildGameSwitcher());
            top.Add(new Panel(), grow: 1f);
            if (keys)
            {
                var quit = new Button(theme, "Quit") { Accent = ButtonAccent.Text, TextSize = 16f, Tracking = 0.12f, Padding = new Thickness(12f, 10f) };
                quit.Activated += _ => Context.Host.Quit();
                top.Add(quit);
            }

            // After the column, so the list of games it opens is drawn over the page and takes the press.
            column.HorizontalAlignment = HorizontalAlignment.Stretch;
            page.Add(column, new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(SideMargin, 50f, SideMargin, 16f)));
            page.Add(top, new Anchor(UiVec2.Zero, new UiVec2(1f, 0f), new Thickness(SideMargin, 6f, SideMargin, -50f)));
        }
        else
        {
            page.Add(column, new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(SideMargin, 0f, SideMargin, 64f)));
        }

        return page;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only out of game. Over a live mission the landing page is the <see cref="PauseScreen"/> and the
    /// backdrop is the level being played, so there is nothing here to show off.
    /// </remarks>
    protected override void OnEnter()
    {
        if (!Context.InGame)
        {
            Context.Host.PreviewMission(null); // the default showcase
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The level is left sharp here, and may be a hillside at noon: a wash that is deepest on the left, where the words
    /// are, keeps them readable over it without hiding the picture on the right.
    /// </remarks>
    protected override void OnDrawBackground(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        var screen = new UiRect(0f, 0f, painter.Context.Viewport.X, painter.Context.Viewport.Y);
        bool compact = Size == ShellSize.Compact;
        Span<UiGradientStop> stops =
        [
            new(0f, UiPalette.InkAt(compact ? 0.86f : 0.72f)),
            new(0.55f, UiPalette.InkAt(compact ? 0.78f : 0.5f)),
            new(1f, UiPalette.InkAt(compact ? 0.72f : 0.4f)),
        ];
        painter.FillGradient(screen, stops, UiGradientDirection.Horizontal);
    }

    private FlexPanel BuildWaysIn(UiTheme theme, bool compact)
    {
        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = compact ? 10f : 16f,
            Margin = new Thickness(0f, compact ? 4f : 16f, 0f, 0f),
            HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
        };

        string? last = Context.Host.Coop?.LastPlayed;
        string profiles = Context.Config.Profiles.Count == 1 ? "1 operative" : string.Create(CultureInfo.InvariantCulture, $"{Context.Config.Profiles.Count} operatives");
        (int done, int total) = CampaignProgress();
        (KitIcon Icon, string Title, string Detail, ShellTab Tab)[] ways =
        [
            (KitIcon.Globe, "Multiplayer", last is null ? "Browse servers or host a game" : compact ? "Reconnect" : $"Reconnect to {last}", ShellTab.Coop),
            (KitIcon.Sliders, "Settings", compact ? "Graphics, audio" : "Graphics, controls, audio", ShellTab.Settings),
            compact
                ? (KitIcon.Person, Context.Config.Active?.Name ?? "Profile", $"{DifficultyName()} · {done}/{total}", ShellTab.Profile)
                : (KitIcon.Person, "Profile", $"{Context.Config.Active?.Name ?? "No profile"} · {DifficultyName()} · {profiles}", ShellTab.Profile),
        ];

        foreach ((KitIcon icon, string title, string detail, ShellTab tab) in ways)
        {
            var card = new WayIn(theme, icon, title, detail, compact);
            card.Activated += _ => GoTo(tab);
            if (compact)
            {
                row.Add(card, grow: 1f);
            }
            else
            {
                row.Add(card);
            }
        }

        return row;
    }

    /// <summary>The mission to carry on with: a line saying so, its name, its briefing, and how far the campaign has got.</summary>
    private sealed class Hero(UiTheme theme, ShellSize size) : UiElement
    {
        private string _wrapped = string.Empty;
        private float _wrappedAt = -1f;

        public string Eyebrow { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Briefing { get; init; } = string.Empty;

        public string Difficulty { get; init; } = string.Empty;

        public int Done { get; init; }

        public int Total { get; init; }

        private bool Compact => size == ShellSize.Compact;

        private float TitleSize => size switch { ShellSize.Wide => 112f, ShellSize.Medium => 96f, _ => 40f };

        private float EyebrowSize => Compact ? 13f : 14f;

        private float BriefingSize => size switch { ShellSize.Wide => 22f, ShellSize.Medium => 21f, _ => 15f };

        private float Gap => Compact ? 10f : 18f;

        private float ProgressWidth => size switch { ShellSize.Wide => 520f, ShellSize.Medium => 494f, _ => 300f };

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
        {
            float width = MathF.Min(available.X, context.Px(1000f));
            string briefing = Wrapped(context, width);
            float height = (context.Px(EyebrowSize) * 1.2f) + context.Px(Gap) + TitleHeight(context, width);
            if (briefing.Length > 0)
            {
                height += context.Px(Gap) + theme.Body.Measure(briefing, context.Px(BriefingSize)).Y;
            }

            if (Total > 0)
            {
                height += context.Px(Gap) + context.Px(Compact ? 4f : 24f);
            }

            return new UiVec2(width, height);
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float y = Bounds.Y;

            float eyebrow = context.Px(EyebrowSize);
            var eyebrowBox = new UiRect(Bounds.X, y, Bounds.Width, eyebrow * 1.2f);
            painter.DrawLabel(theme.Heading, Eyebrow, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Heading, eyebrow, eyebrowBox)), eyebrow, theme.Accent, 0.14f);
            y = eyebrowBox.Bottom + context.Px(Gap);

            // The name, as large as the page allows; one too long for the line is set smaller, not cut.
            string title = Title.ToUpperInvariant();
            float size = FittedTitle(context, Bounds.Width);
            var titleBox = new UiRect(Bounds.X, y, Bounds.Width, TitleHeight(context, Bounds.Width));
            painter.DrawLabel(theme.Display, title, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Display, size, titleBox)), size, theme.Text, 0.03f);
            y = titleBox.Bottom;

            string briefing = Wrapped(context, Bounds.Width);
            if (briefing.Length > 0)
            {
                y += context.Px(Gap);
                float px = context.Px(BriefingSize);
                painter.DrawText(theme.Body, briefing, new UiVec2(Bounds.X, y), px, theme.TextSecondary);
                y += theme.Body.Measure(briefing, px).Y;
            }

            if (Total <= 0)
            {
                return;
            }

            // How far the campaign has got: the difficulty it is being played at, a segment a mission, and the count.
            y += context.Px(Gap);
            float rowHeight = context.Px(Compact ? 4f : 24f);
            float x = Bounds.X;
            float right = Bounds.X + MathF.Min(Bounds.Width, context.Px(ProgressWidth));
            if (!Compact)
            {
                float chipSize = context.Px(13f);
                string difficulty = Difficulty.ToUpperInvariant();
                float chipWidth = UiPainter.MeasureLabel(theme.Heading, difficulty, chipSize, 0.08f) + context.Px(16f);
                var chip = new UiRect(x, y, chipWidth, rowHeight);
                painter.FillRect(chip, UiPalette.InkAt(0.35f));
                painter.StrokeRect(chip, context.Px(1f), theme.Accent);
                painter.DrawLabel(theme.Heading, difficulty, new UiVec2(chip.X + context.Px(8f), UiRowStyle.CapsCenterY(theme.Heading, chipSize, chip)), chipSize, theme.Accent, 0.08f);
                x = chip.Right + context.Px(16f);

                float countSize = context.Px(15f);
                string count = string.Create(CultureInfo.InvariantCulture, $"{Done}/{Total}");
                float countWidth = theme.Mono.MeasureWidth(count, countSize);
                painter.DrawText(theme.Mono, count, new UiVec2(right - countWidth, UiRowStyle.CenterY(theme.Mono, countSize, chip)), countSize, theme.TextSecondary);
                right -= countWidth + context.Px(16f);
            }

            // Done in amber, the one being played in white, the rest to come dim.
            float gap = context.Px(3f);
            float segment = (right - x - ((Total - 1) * gap)) / Total;
            float bar = context.Px(4f);
            float top = y + ((rowHeight - bar) * 0.5f);
            for (int i = 0; i < Total; i++)
            {
                Bgra32 ink = i < Done ? theme.Accent : i == Done ? theme.Text : UiPalette.White(0.22f);
                painter.FillRect(new UiRect(x + (i * (segment + gap)), top, segment, bar), ink);
            }
        }

        private float FittedTitle(LayoutContext context, float width)
        {
            float size = context.Px(TitleSize);
            float natural = UiPainter.MeasureLabel(theme.Display, Title.ToUpperInvariant(), size, 0.03f);
            return natural > width && natural > 0f ? size * (width / natural) : size;
        }

        // Capitals stand about 0.72 of the size: the title's box is their height, so the lines above and below sit
        // where the eye puts them, not where the font's line height would.
        private float TitleHeight(LayoutContext context, float width) => FittedTitle(context, width) * 0.74f;

        private string Wrapped(LayoutContext context, float available)
        {
            float width = MathF.Min(available, context.Px(720f));
            if (_wrappedAt != width)
            {
                _wrappedAt = width;
                _wrapped = Briefing.Length == 0 ? string.Empty : theme.Body.WrapText(Briefing, context.Px(BriefingSize), width);
            }

            return _wrapped;
        }
    }

    /// <summary>A way into another section: its icon, its name, and a line of what is waiting there.</summary>
    private sealed class WayIn(UiTheme theme, KitIcon icon, string title, string detail, bool compact) : Control(theme)
    {
        private readonly string _title = title.ToUpperInvariant();

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            compact ? new UiVec2(context.Px(160f), context.Px(56f)) : new UiVec2(context.Px(300f), context.Px(116f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float appear = Visual.Appear;
            float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
            painter.FillRect(Bounds, Bgra32.FromRgba(12, 14, 15, (byte)((compact ? 235f : 184f) * appear)));
            if (hot > 0.001f)
            {
                painter.FillRect(Bounds, UiPalette.White(0.06f * hot * appear));
            }

            painter.StrokeRect(Bounds, context.Px(Visual.Focus > 0.5f ? 2f : 1f), UiPalette.Fade(Interpolate.Lerp(UiPalette.White(0.12f), Theme.AccentHover, hot), appear));

            float titleSize = context.Px(compact ? 16f : 24f);
            float detailSize = context.Px(compact ? 14f : 15f);
            float pad = context.Px(compact ? 14f : 20f);
            Bgra32 iconInk = UiPalette.Fade(Interpolate.Lerp(Theme.TextSecondary, Theme.Accent, hot), appear);
            float titleCaps = titleSize * 0.72f;
            float detailLine = Theme.Body.LineHeight(detailSize);
            float gap = context.Px(compact ? 4f : 6f);
            float x = Bounds.X + pad;
            float top;
            if (compact)
            {
                float glyph = context.Px(20f);
                KitIcons.Draw(painter, icon, new UiVec2(x + (glyph * 0.5f), Bounds.Center.Y), glyph, iconInk);
                x += glyph + context.Px(10f);
                top = Bounds.Center.Y - ((titleCaps + gap + detailLine) * 0.5f);
            }
            else
            {
                float glyph = context.Px(24f);
                KitIcons.Draw(painter, icon, new UiVec2(x + (glyph * 0.5f), Bounds.Y + pad + (glyph * 0.5f)), glyph, iconInk);
                top = Bounds.Bottom - pad - detailLine - gap - titleCaps;
            }

            float room = Bounds.Right - pad - x;
            var titleBox = new UiRect(x, top, room, titleCaps);
            painter.DrawLabel(Theme.Heading, TextFit.ToWidth(Theme.Heading, _title, titleSize, room), new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, titleSize, titleBox)), titleSize, UiPalette.Fade(Theme.Text, appear), 0.1f);
            painter.DrawText(Theme.Body, TextFit.ToWidth(Theme.Body, detail, detailSize, room), new UiVec2(x, titleBox.Bottom + gap), detailSize, UiPalette.Fade(Theme.TextSecondary, appear));
        }
    }
}
