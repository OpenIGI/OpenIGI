using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// A preset across the top of a settings page: segments to press, one of them a reading -- Custom -- that shows what the
/// settings below add up to and cannot itself be chosen.
/// </summary>
/// <remarks>
/// The Graphics page's quality preset and the Gameplay and Interface pages' experience. Each says what its presets are,
/// which the settings read as now, what choosing one does, and when to read again; the band is the same for all of them.
/// </remarks>
internal sealed class PresetBand
{
    /// <summary>Gets the band's heading, as the player reads it.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the line under the heading.</summary>
    public required string Line { get; init; }

    /// <summary>Gets what the band does, for the panel that explains the row in focus.</summary>
    public required string Help { get; init; }

    /// <summary>Gets the presets' captions, in the order shown.</summary>
    public required IReadOnlyList<string> Captions { get; init; }

    /// <summary>Gets whether a preset can be chosen, by index: the reading cannot.</summary>
    public required Func<int, bool> Choosable { get; init; }

    /// <summary>Gets which preset the settings add up to now, by index.</summary>
    public required Func<int> Reading { get; init; }

    /// <summary>Gets what choosing a preset does, by index.</summary>
    public required Action<int> Choose { get; init; }

    /// <summary>Gets what calls back whenever the reading may have moved, and returns what stops it.</summary>
    public required Func<Action, IDisposable> Watch { get; init; }

    /// <summary>Builds the band, as a group across the top of the page's columns.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="bindings">Receives what unhooks the band when the page is rebuilt or left.</param>
    /// <returns>The band's group.</returns>
    public SettingsGroup Build(UiTheme theme, List<IDisposable> bindings)
    {
        bool compact = ShellSizes.IsCompact(theme);

        // Segments to press where there is room across; a row that steps where there is not.
        Control control;
        Action<int> show;
        Action<Action<int>> onPicked;
        if (compact)
        {
            var row = new Dropdown(theme, Title, Captions, Reading()) { Tooltip = Help, OptionEnabled = Choosable };
            control = row;
            show = index => row.SelectedIndex = index;
            onPicked = handler => row.SelectionChanged += handler;
        }
        else
        {
            var strip = new TabStrip(theme, Captions)
            {
                SegmentHeight = 36f,
                TabPadding = 18f,
                TextSize = 17f,
                VerticalAlignment = VerticalAlignment.Center,
                Tooltip = Help,
                TabEnabled = Choosable,
            };
            strip.SelectedIndex = Reading();
            control = strip;
            show = index => strip.SelectedIndex = index;
            onPicked = handler => strip.SelectionChanged += handler;
        }

        // Choosing a preset writes each of its values, and each reports itself; the band reads the preset back once, at
        // the end, rather than at every one of them.
        bool updating = false;
        void ShowReading()
        {
            updating = true;
            try
            {
                show(Reading());
            }
            finally
            {
                updating = false;
            }
        }

        onPicked(index =>
        {
            if (updating || !Choosable(index))
            {
                return;
            }

            updating = true;
            try
            {
                Choose(index);
            }
            finally
            {
                updating = false;
            }

            ShowReading();
        });

        bindings.Add(Watch(() =>
        {
            if (!updating)
            {
                ShowReading();
            }
        }));

        if (compact)
        {
            return SettingsGroup.Of("General", [control]);
        }

        var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f, VerticalAlignment = VerticalAlignment.Center };
        words.AddChild(new Label(theme.BodyStrong, Title, theme.BodyTextSize, theme.Text));
        words.AddChild(new Label(theme.Body, Line, 15f, theme.TextSecondary) { Wrap = true });

        var inner = new Grid { ColumnSpacing = 24f };
        inner.RowDefinitions.Add(GridLength.Auto);
        inner.ColumnDefinitions.Add(GridLength.Star);
        inner.ColumnDefinitions.Add(GridLength.Auto);
        inner.Add(words, 0, 0);
        inner.Add(control, 0, 1);

        var band = new OpenIGI.UI.Kit.Widgets.GlassPanel(theme)
        {
            Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, 199),
            Border = UiPalette.White(0.12f),
            Padding = new Thickness(20f, 14f),
        };
        band.AddChild(inner);
        return new SettingsGroup(Title, [control]) { Banner = band };
    }
}
