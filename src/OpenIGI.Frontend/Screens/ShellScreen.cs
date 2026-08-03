using OpenIGI.Game.Profiles;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>Which top-level section of the shell a screen is.</summary>
public enum ShellTab
{
    /// <summary>The landing page — the bar over the showcase, nothing selected.</summary>
    Home,

    /// <summary>The campaign / mission select.</summary>
    Campaign,

    /// <summary>The settings.</summary>
    Settings,

    /// <summary>Profiles and saves.</summary>
    Profile,

    /// <summary>The extras.</summary>
    Extras,
}

/// <summary>
/// The common frame every shell page shares: a persistent horizontal tab bar across the top — title,
/// the section tabs, Quit and the game selector — with the page's own content below it.
/// </summary>
/// <remarks>
/// <para>
/// Navigation is by <em>replacement</em>, not a stack: selecting a tab resets the shell to that section,
/// so there is only ever one page on screen and never two drawing over each other. The bar is part of
/// every page, so it is always there and always interactive — a tab can be clicked or steered to from
/// anywhere — and the current section's tab is drawn selected. Back returns to the home page.
/// </para>
/// <para>
/// The content is laid out below the bar's height, so the two never overlap; a page with no content is
/// the home page, just the bar over the orbiting showcase.
/// </para>
/// </remarks>
public abstract class ShellScreen : Screen
{
    /// <summary>The fraction of the screen height the top bar occupies.</summary>
    protected const float BarHeightFraction = 0.13f;

    /// <summary>Initializes a new instance of the <see cref="ShellScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    protected ShellScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Name = GetType().Name;
    }

    /// <summary>Gets the shared shell context.</summary>
    protected ShellContext Context { get; }

    /// <summary>Gets which section this page is, for the selected tab.</summary>
    protected abstract ShellTab Tab { get; }

    /// <summary>Builds the page's content, shown below the bar, or <see langword="null"/> for none.</summary>
    /// <param name="context">The layout context.</param>
    /// <returns>The content element, or <see langword="null"/>.</returns>
    protected abstract UiElement? BuildContent(LayoutContext context);

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        var canvas = new Canvas();

        // Content first in tree order so focus starts on it rather than on a tab; the two do not overlap,
        // so draw order does not matter.
        if (BuildContent(context) is { } content)
        {
            canvas.Add(content, new Anchor(new UiVec2(0f, BarHeightFraction + 0.02f), UiVec2.One, new Thickness(48f, 24f, 48f, 48f)));
        }

        canvas.Add(BuildTopBar(), new Anchor(UiVec2.Zero, new UiVec2(1f, BarHeightFraction)));
        return canvas;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        if (nav.Back && Tab != ShellTab.Home)
        {
            Context.Stack.Reset(new MainMenuScreen(Context));
            return false;
        }

        return base.HandleInput(nav, input);
    }

    private Panel BuildTopBar()
    {
        UiTheme theme = Context.Theme;
        var bar = new Panel { Background = theme.Panel };
        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 4f,
            Padding = new Thickness(44f, 0f),
            Justify = FlexJustify.Start,
        };

        var title = new Button(theme, "PROJECT I.G.I.")
        {
            TextSize = theme.HeadingTextSize * 0.58f,
            Margin = new Thickness(0f, 0f, 32f, 0f),
        };
        title.Activated += _ => GoTo(ShellTab.Home);
        row.Add(title);

        AddTab(row, "Campaign", ShellTab.Campaign);
        AddTab(row, "Settings", ShellTab.Settings);
        AddTab(row, "Profile", ShellTab.Profile);
        AddTab(row, "Extras", ShellTab.Extras);

        row.Add(new Panel(), grow: 1f); // pushes what follows to the right edge

        var quit = new Button(theme, "Quit") { TextSize = theme.BodyTextSize * 1.15f };
        quit.Activated += _ => Context.Host.Quit();
        row.Add(quit);

        row.Add(BuildGameSelector());

        bar.AddChild(row);
        return bar;
    }

    private void AddTab(FlexPanel row, string text, ShellTab tab)
    {
        var button = new Button(Context.Theme, text)
        {
            TextSize = Context.Theme.BodyTextSize * 1.15f,
            Selected = tab == Tab,
        };
        button.Activated += _ => GoTo(tab);
        row.Add(button);
    }

    private Dropdown BuildGameSelector()
    {
        string[] games = new string[Context.Games.Count];
        for (int i = 0; i < games.Length; i++)
        {
            games[i] = Context.Games[i].IsPlayable
                ? Context.Games[i].DisplayName
                : Context.Games[i].DisplayName + "  (unavailable)";
        }

        var selector = new Dropdown(Context.Theme, "Game", games) { Width = 320f };
        selector.SelectionChanged += index =>
        {
            if (index < 0 || index >= Context.Games.Count || !Context.Games[index].IsPlayable)
            {
                selector.SelectedIndex = 0;
            }
        };
        return selector;
    }

    private void GoTo(ShellTab tab)
    {
        if (tab == Tab)
        {
            return;
        }

        Context.Stack.Reset(CreateScreen(tab, Context));
    }

    private static Screen CreateScreen(ShellTab tab, ShellContext context) => tab switch
    {
        ShellTab.Campaign => new CampaignScreen(context),
        ShellTab.Settings => new SettingsScreen(context),
        ShellTab.Profile => new ProfilesScreen(context),
        ShellTab.Extras => new ExtrasScreen(context),
        _ => new MainMenuScreen(context),
    };
}
