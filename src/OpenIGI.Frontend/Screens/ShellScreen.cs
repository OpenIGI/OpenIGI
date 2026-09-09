using OpenIGI.Core.Titles;
using OpenIGI.Input;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;

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
/// The common frame every shell page shares: a persistent horizontal tab bar across the top — the title,
/// the section tabs and Quit — with the page's own content below it.
/// </summary>
/// <remarks>
/// <para>
/// Navigation is by <em>replacement</em>, not a stack: selecting a tab resets the shell to that section,
/// so there is only ever one page on screen and never two drawing over each other. The bar is part of
/// every page, so it is always there and always interactive — a tab can be clicked or steered to from
/// anywhere — and the current section's tab is drawn selected. Back returns to the home page.
/// </para>
/// <para>
/// The title doubles as the game selector, opening a list of the titles the shell knows about. There is no
/// second picker at the other end of the bar: the name at the top left <em>is</em> the statement of which
/// game is running, and having both said the same thing twice.
/// </para>
/// <para>
/// The content is laid out below the bar's height, so the two never overlap; a page with no content is
/// the home page, just the bar over the orbiting showcase.
/// </para>
/// </remarks>
public abstract class ShellScreen : Screen
{
    /// <summary>The fraction of the screen height the top bar occupies.</summary>
    protected const float BarHeightFraction = 0.095f;

    private Panel? _bar;
    private DropdownButton? _title;

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

    /// <summary>Gets a value indicating whether the page's content is laid on a translucent backdrop.</summary>
    /// <remarks>
    /// A form — settings rows, profiles, credits — is dense text that has to be readable over whatever the
    /// showcase happens to be looking at, and over a sunlit hillside muted grey on nothing is not. The
    /// backdrop is translucent, so the level still reads behind it and around it. A page whose content
    /// <em>is</em> the level, like the campaign's, turns it off and carries its own legibility instead.
    /// </remarks>
    protected virtual bool ContentOnBackdrop => true;

    /// <inheritdoc/>
    protected override UiElement? Chrome => _bar;

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        var canvas = new Canvas();

        // Content first in tree order so focus starts on it rather than on a tab; the two do not overlap,
        // so draw order does not matter.
        if (BuildContent(context) is { } content)
        {
            // The backdrop fills the whole page area rather than sitting in the middle of it: a form that
            // has been given the screen should use it. What keeps that from turning a settings row into a
            // caption at one edge and a value at the other is the page laying its content out in columns,
            // not the panel being narrowed.
            UiElement page = ContentOnBackdrop
                ? new Panel
                {
                    Background = Context.Theme.Panel.WithAlpha(205),
                    Padding = new Thickness(40f, 28f),
                }.WithChild(content)
                : content;

            canvas.Add(page, new Anchor(new UiVec2(0f, BarHeightFraction), UiVec2.One, new Thickness(56f, 26f, 56f, 34f)));
        }

        _bar = BuildTopBar();
        canvas.Add(_bar, new Anchor(UiVec2.Zero, new UiVec2(1f, BarHeightFraction)));
        return canvas;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // An open menu takes back for itself: shutting it is what the player meant, not leaving the page.
        if (nav.Back && _title is { IsOpen: true } openTitle)
        {
            openTitle.Close();
            return false;
        }

        if (nav.Back && Tab != ShellTab.Home)
        {
            Context.Stack.Reset(CreateScreen(ShellTab.Home, Context));
            return false;
        }

        return base.HandleInput(nav, input);
    }

    private Panel BuildTopBar()
    {
        UiTheme theme = Context.Theme;
        var bar = new Panel { Background = theme.Panel };

        // A canvas rather than the row alone, so a hairline can be laid over the bar's bottom edge and
        // give the content below it something to sit under.
        var layers = new Canvas();

        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 2f,
            Padding = new Thickness(48f, 0f),
            Justify = FlexJustify.Start,
        };

        // The bar's controls carry a vertical margin so their highlight is a band around the caption
        // rather than a block the full height of the bar, and the underline lands just under the text
        // instead of merging into the bar's own hairline.
        //
        // The title is also the game selector: it looks exactly like the button it was, and opens a list
        // of the titles this shell can run. That is where the choice belongs — the game being played is
        // what the name at the top left of a screen is announcing — rather than in a second picker at the
        // far end of the bar saying the same thing twice.
        row.Add(BuildTitle(theme));

        AddTab(row, "Campaign", ShellTab.Campaign);
        AddTab(row, "Settings", ShellTab.Settings);
        AddTab(row, "Profile", ShellTab.Profile);
        AddTab(row, "Extras", ShellTab.Extras);

        row.Add(new Panel(), grow: 1f); // pushes what follows to the right edge

        var quit = new Button(theme, "Quit")
        {
            TextSize = theme.BodyTextSize * 1.1f,
            Padding = new Thickness(20f, 8f),
            Margin = new Thickness(24f, 20f, 0f, 20f),
        };
        quit.Activated += _ => Context.Host.Quit();
        row.Add(quit);

        // The hairline goes in BEFORE the row, so the row draws over it. It is background chrome — the
        // edge the page below hangs off — and a canvas draws its children in order, so adding it last put
        // it over everything the bar contains, including the list the title drops below itself.
        layers.Add(
            new Panel
            {
                Background = theme.Accent.WithAlpha(110),
                Height = 2f,
                VerticalAlignment = VerticalAlignment.Bottom,
            },
            new Anchor(new UiVec2(0f, 0.8f), UiVec2.One));

        layers.Add(row);
        bar.AddChild(layers);
        return bar;
    }

    private void AddTab(FlexPanel row, string text, ShellTab tab)
    {
        var button = new Button(Context.Theme, text)
        {
            TextSize = Context.Theme.BodyTextSize * 1.15f,
            Padding = new Thickness(22f, 8f),
            Margin = new Thickness(0f, 20f),
            Selected = tab == Tab,

            // A bar down the left edge reads as "this row" in a column and as noise in a horizontal nav;
            // an underline is what a tab in a bar wants.
            Accent = ButtonAccent.Underline,
        };
        button.Activated += _ => GoTo(tab);
        row.Add(button);
    }

    private DropdownButton BuildTitle(UiTheme theme)
    {
        string[] games = new string[Context.Games.Count];
        for (int i = 0; i < games.Length; i++)
        {
            games[i] = Context.Games[i].DisplayName;
        }

        var title = new DropdownButton(theme, "PROJECT I.G.I.", games)
        {
            Font = theme.Heading,
            TextSize = theme.HeadingTextSize * 0.62f,
            Padding = new Thickness(14f, 8f),
            Margin = new Thickness(0f, 20f, 40f, 20f),
            Accent = ButtonAccent.Underline,
            CurrentIndex = 0,

            // I.G.I. 2 is listed but cannot be taken: the shell has no support for it yet, and a title the
            // player can see is coming is worth more than a list with one entry. Saying so in the list is
            // also what stops the click reading as broken.
            ItemEnabled = index => index >= 0 && index < Context.Games.Count && Context.Games[index].IsPlayable,
        };

        title.ItemSelected += index =>
        {
            // Picking the game already running is a way back to the landing page rather than a reload.
            if (index == 0)
            {
                GoTo(ShellTab.Home);
            }
        };

        _title = title;
        return title;
    }

    private void GoTo(ShellTab tab)
    {
        if (tab == Tab)
        {
            return;
        }

        Context.Stack.Reset(CreateScreen(tab, Context));
    }

    // Home is the landing page out of game and the pause menu in it: the tabs, the title and Back all lead
    // to "the page this section hangs off", and inside a mission that page is the one holding Resume.
    private static Screen CreateScreen(ShellTab tab, ShellContext context) => tab switch
    {
        ShellTab.Campaign => new CampaignScreen(context),
        ShellTab.Settings => new SettingsScreen(context),
        ShellTab.Profile => new ProfilesScreen(context),
        ShellTab.Extras => new ExtrasScreen(context),
        _ => context.InGame ? new PauseScreen(context) : new MainMenuScreen(context),
    };
}
