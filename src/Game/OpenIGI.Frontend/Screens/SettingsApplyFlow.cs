using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// How the settings page's staged categories reach the game: the draft their rows write to, the Apply bar
/// that appears with a change, and the questions over the page -- keep a display mode just applied, or what
/// to do with changes on the way out.
/// </summary>
/// <remarks>
/// Made with the page and kept across its rebuilds -- a resize, a resolution just applied -- so a half-made
/// choice, and a question on screen, survive them. Dropped on leaving.
/// </remarks>
internal sealed class SettingsApplyFlow
{
    /// <summary>How long a newly applied display mode waits to be kept before it is put back.</summary>
    internal const float KeepSeconds = 15f;

    /// <summary>The variables a change of which can leave the player with no picture they can use, and so has to be kept.</summary>
    private static readonly string[] DisplayModeVariables = ["r_window_mode", "r_resolution"];

    private readonly ShellContext _context;
    private readonly Func<FocusManager?> _focus;
    private readonly Action _refreshHints;
    private readonly Func<bool> _isStagedTab;
    private readonly List<Button> _promptButtons = [];

    // The Graphics and Display categories' unapplied changes. Made with the page and kept across its
    // rebuilds -- a resize, a resolution just applied -- so a half-made choice survives them; dropped on leaving.
    private SettingsDraft? _draft;

    private UiElement? _page;
    private UiElement? _applyBar;
    private Label? _applyNote;
    private Button? _apply;
    private Button? _discard;

    // The question on screen, if any. Kept as data, so it survives the page being rebuilt under it -- which
    // is exactly what applying a resolution does.
    private SettingsPrompt? _prompt;
    private Panel? _promptLayer;
    private Label? _promptMessage;

    /// <summary>Initializes a new instance of the <see cref="SettingsApplyFlow"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="focus">The page's focus manager, which changes with every build of it.</param>
    /// <param name="refreshHints">Puts the page's key hints back in the footer.</param>
    /// <param name="isStagedTab">Whether the category on screen holds its changes until Apply.</param>
    public SettingsApplyFlow(ShellContext context, Func<FocusManager?> focus, Action refreshHints, Func<bool> isStagedTab)
    {
        _context = context;
        _focus = focus;
        _refreshHints = refreshHints;
        _isStagedTab = isStagedTab;
    }

    /// <summary>Gets the question on screen, if any.</summary>
    public SettingsPrompt? Prompt => _prompt;

    /// <summary>Gets a value indicating whether the draft holds anything to apply.</summary>
    public bool HasChanges => _draft?.HasChanges == true;

    private FocusManager? Focus => _focus();

    /// <summary>The draft the staged categories write to, made the first time it is wanted.</summary>
    public SettingsDraft EnsureDraft() => _draft ??= CreateDraft();

    /// <summary>Drops whatever the draft holds.</summary>
    public void Discard() => _draft?.Discard();

    /// <summary>Takes in a new build of the page, and puts the Apply bar and any question back on it.</summary>
    /// <param name="page">The page behind a question, which stops taking clicks while one is asked.</param>
    /// <param name="promptLayer">The layer over the page that a question goes in.</param>
    public void Attach(UiElement page, Panel promptLayer)
    {
        _page = page;
        _promptLayer = promptLayer;
        UpdateApplyBar();
        ShowPrompt(_prompt);
    }

    /// <summary>Gives focus back to a question still on screen after a rebuild made a new focus manager.</summary>
    public void FocusPrompt()
    {
        // A rebuild makes a new focus manager, which starts on the page's first row: a question still on
        // screen has to keep it.
        if (_prompt is not null && _promptButtons.Count > 0)
        {
            Focus?.Focus(_promptButtons[0]);
        }
    }

    /// <summary>Holds focus among a question's answers, which sit in a row.</summary>
    /// <returns>Whether a question took the move.</returns>
    public bool TryMoveFocus(NavDirection direction)
    {
        // A question holds focus among its own answers, which sit in a row.
        if (_prompt is not null && _promptButtons.Count > 0)
        {
            int at = Focus?.Focused is Button focusedButton ? _promptButtons.IndexOf(focusedButton) : -1;
            int step = direction is NavDirection.Left or NavDirection.Up ? -1 : 1;
            Focus?.Focus(_promptButtons[at < 0 ? 0 : (at + step + _promptButtons.Count) % _promptButtons.Count]);
            return true;
        }

        return false;
    }

    /// <summary>Decides whether leaving the page has to wait for an answer first.</summary>
    /// <param name="leave">What leaves the page, once it may.</param>
    /// <returns>Whether leaving is held.</returns>
    public bool HoldDeparture(Action leave)
    {
        switch (_prompt)
        {
            case { Kind: SettingsPromptKind.Keep } keep:
                // Leaving is as good an answer as any that the new display mode works.
                keep.Choices[0].Chosen();
                return false;

            case not null:
                return true;
        }

        if (_draft is not { HasChanges: true })
        {
            return false;
        }

        ShowPrompt(new SettingsPrompt(
            SettingsPromptKind.Leave,
            "Unapplied changes",
            "Apply your changes?",
            _ => "Graphics or display settings were changed and not applied.",
            "Cancel",
            [
                new("Apply", ButtonAccent.Solid, () => Apply(then: leave)),
                new("Discard", ButtonAccent.Ghost, () =>
                {
                    ShowPrompt(null);
                    _draft?.Discard();
                    leave();
                }),
                new("Cancel", ButtonAccent.Ghost, () => ShowPrompt(null)),
            ]));
        return true;
    }

    /// <summary>Counts a question's time on screen, and answers it the cautious way once that runs out.</summary>
    public void Update(float elapsedSeconds)
    {
        if (_prompt is { } prompt)
        {
            prompt.Elapsed += elapsedSeconds;
            if (_promptMessage is not null)
            {
                _promptMessage.Text = prompt.Message(prompt.Elapsed);
            }

            if (prompt.Expires is { } expires && prompt.Elapsed >= expires)
            {
                prompt.Back();
            }
        }
    }

    /// <summary>Drops the draft, and whatever was not applied with it, as the page is left.</summary>
    public void Exit()
    {
        // Whatever was not applied is dropped with the page; it never reached the game.
        _draft?.Dispose();
        _draft = null;
        _prompt = null;
    }

    /// <summary>The note and the two buttons beside the help line on a staged category.</summary>
    /// <remarks>
    /// Sized as the category tabs are, not as the page's call-to-action buttons: it is a quiet status that
    /// appears with a change and goes with it, rather than two greyed boxes standing by on every page.
    /// </remarks>
    public FlexPanel BuildApplyBar(UiTheme theme)
    {
        _applyNote = new Label(theme.Heading, "UNAPPLIED CHANGES", 14f, theme.Accent)
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        _discard = new Button(theme, "Discard") { Accent = ButtonAccent.Ghost, TextSize = 14f, Tracking = theme.LabelTracking, Padding = new Thickness(20f, 8f) };
        _discard.Activated += _ => _draft?.Discard();
        _apply = new Button(theme, "Apply") { Accent = ButtonAccent.Solid, CutCorner = 8f, TextSize = 14f, Tracking = theme.LabelTracking, Padding = new Thickness(22f, 8f) };
        _apply.Activated += _ => Apply();

        var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, VerticalAlignment = VerticalAlignment.Center };
        bar.Add(_applyNote);
        bar.Add(_discard);
        bar.Add(_apply);
        _applyBar = bar;
        return bar;
    }

    /// <summary>Shows the Apply bar while a staged category has something to apply, and its keys in the footer.</summary>
    public void UpdateApplyBar()
    {
        bool shown = _isStagedTab() && _draft?.HasChanges == true;
        if (_applyBar is not null)
        {
            _applyBar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        Focus?.Validate();
        if (_prompt is null)
        {
            _refreshHints();
        }
    }

    /// <summary>Writes the draft to the game, then asks for a new display mode to be kept.</summary>
    /// <param name="then">What to do once it is applied -- leave the page -- unless a display mode now has to be kept.</param>
    public void Apply(Action? then = null)
    {
        ShowPrompt(null);
        if (_draft is not { HasChanges: true } draft)
        {
            then?.Invoke();
            return;
        }

        // The graphics API last: changing it rebuilds the game, which should come back with everything else
        // already in force and saved.
        bool switchesApi = draft.Pending.Any(static change => change.Variable.Name == "r_renderer");
        IReadOnlyList<(ICVar Variable, string Previous)> written =
            draft.Commit(static variable => variable.Name == "r_renderer");

        // The writes each read as the player taking over from the preset; the preset is whatever they add up to.
        _context.Presets.Detect();
        _context.Host.SaveSettings();

        // A rebuilt game starts with the new API and window both, and nothing on screen to answer with.
        List<(ICVar Variable, string Previous)> display =
            [.. written.Where(static change => DisplayModeVariables.Contains(change.Variable.Name))];
        if (display.Count > 0 && !switchesApi)
        {
            AskToKeep(display);
            return;
        }

        then?.Invoke();
    }

    /// <summary>The draft the staged categories write to, which keeps the Apply bar's state.</summary>
    private SettingsDraft CreateDraft()
    {
        var draft = new SettingsDraft(_context.Cvars);
        draft.Changed += (_, _) => UpdateApplyBar();
        return draft;
    }

    /// <summary>Asks for a display mode just applied to be kept, and puts it back if it is not.</summary>
    /// <param name="previous">Each display variable written, with what it held before.</param>
    private void AskToKeep(List<(ICVar Variable, string Previous)> previous)
    {
        void Revert()
        {
            ShowPrompt(null);
            foreach ((ICVar variable, string text) in previous)
            {
                variable.TrySetText(text, out _);
            }

            _context.Host.SaveSettings();
        }

        ShowPrompt(new SettingsPrompt(
            SettingsPromptKind.Keep,
            "Display",
            "Keep these display settings?",
            elapsed => string.Create(
                CultureInfo.InvariantCulture,
                $"The previous ones come back in {Math.Max(0, (int)MathF.Ceiling(KeepSeconds - elapsed))} s."),
            "Revert",
            [
                new("Keep", ButtonAccent.Solid, () => ShowPrompt(null)),
                new("Revert", ButtonAccent.Ghost, Revert),
            ],
            KeepSeconds));
    }

    /// <summary>Puts a question on screen over the page, or takes it away with <see langword="null"/>.</summary>
    private void ShowPrompt(SettingsPrompt? prompt)
    {
        _prompt = prompt;
        _promptButtons.Clear();
        _promptMessage = null;
        if (_page is not null)
        {
            // The page behind a question is not there to be clicked.
            _page.IsHitTestVisible = prompt is null;
        }

        if (_promptLayer is null)
        {
            return;
        }

        _promptLayer.ClearChildren();
        if (prompt is null)
        {
            Focus?.Validate();
            Focus?.FocusFirst();
            _refreshHints();
            return;
        }

        UiTheme theme = _context.Theme;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14f };
        column.AddChild(new SectionTitle(theme, prompt.Eyebrow, prompt.Title) { HorizontalAlignment = HorizontalAlignment.Left, TitleSize = 34f, Margin = new Thickness(0f, 0f, 0f, 4f) });
        _promptMessage = new Label(theme.Body, prompt.Message(prompt.Elapsed), 17f, UiPalette.TextBody) { Wrap = true };
        column.AddChild(_promptMessage);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f, Margin = new Thickness(0f, 10f, 0f, 0f) };
        foreach (SettingsPromptChoice choice in prompt.Choices)
        {
            var button = new Button(theme, choice.Caption) { Accent = choice.Accent, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
            if (choice.Accent == ButtonAccent.Solid)
            {
                button.CutCorner = 10f;
            }

            button.Activated += _ => choice.Chosen();

            // Made after the page's entrance, so it plays its own: a control starts transparent until one has.
            button.PlayEnter(_promptButtons.Count * Screen.EntranceStagger);
            actions.Add(button);
            _promptButtons.Add(button);
        }

        column.AddChild(actions);
        var panel = new GlassPanel(theme)
        {
            Padding = new Thickness(44f, 32f),
            Width = 640f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.AddChild(column);
        _promptLayer.AddChild(panel);

        Focus?.Focus(_promptButtons[0]);
        _refreshHints();
    }
}
