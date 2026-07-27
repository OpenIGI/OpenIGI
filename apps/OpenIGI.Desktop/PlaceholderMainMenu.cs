using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// A <b>hand-written stand-in</b> for the main menu, drawn with the retail background, logo and fonts.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not how the game builds its menus and it is not part of the finished engine.</b> The
/// real main menu is entirely data: <c>menusystem/mainmenu.qvm</c> declares the task types
/// <c>MenuManager</c>, <c>MenuScreen</c>, <c>MenuFrame</c>, <c>MenuText</c>,
/// <c>MenuTextConditional</c>, <c>SlideBar</c>, <c>ListBox</c>, <c>ToggleBox</c>, <c>InputBox</c>,
/// <c>ScrollListBox</c>, <c>PictureBox</c>, <c>DialogWindow</c> and others with
/// <c>Task_DeclareParameters</c>, then instantiates them with <c>Task_New</c> — the M3 task system,
/// driving the M2 script VM. Entry labels, positions, fonts and behaviour all come from there; an
/// entry's action is a QSC expression stored in a <c>VarString</c> parameter such as
/// <c>MenuManager_PushScreen(914)</c>.
/// </para>
/// <para>
/// Reproducing that needs those task types registered with their <c>[ScriptParam]</c> binders plus a
/// runtime evaluator for the expression parameters, which is the remaining M5 work tracked in
/// <c>TODO.md</c>. Until then this class exists for one reason: to exercise the rendering, texture,
/// font and input stack end to end against real retail data. It is <b>replaced</b> by the script-driven
/// tree, not extended into it — the labels below are transcribed from the script's string table so the
/// screen looks right, and nothing else about it is authentic.
/// </para>
/// <para>
/// The menu is authored against the original's 640×480 back buffer and scaled to whatever the window
/// is, which is how the original's windowed present behaves: it stretch-blits the back buffer into the
/// client rect rather than rendering at the window's size.
/// </para>
/// </remarks>
public sealed class PlaceholderMainMenu
{
    /// <summary>The resolution the original's menus are authored against.</summary>
    public const int AuthoredWidth = 640;

    /// <summary>The vertical resolution the original's menus are authored against.</summary>
    public const int AuthoredHeight = 480;

    private static readonly string[] Entries =
    [
        "SINGLE PLAYER",
        "CONFIGURATION",
        "READ ME",
        "CREDITS",
        "QUIT",
    ];

    private readonly MenuAssets _assets;
    private readonly BitmapTextRenderer _title;
    private readonly BitmapTextRenderer _body;

    /// <summary>Initializes a new instance of the <see cref="PlaceholderMainMenu"/> class.</summary>
    /// <param name="assets">The menu art to draw with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <see langword="null"/>.</exception>
    public PlaceholderMainMenu(MenuAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
        _title = new BitmapTextRenderer(assets.TitleFont);
        _body = new BitmapTextRenderer(assets.BodyFont);
    }

    /// <summary>Gets the index of the highlighted entry.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Gets the highlighted entry's label.</summary>
    public string SelectedEntry => Entries[SelectedIndex];

    /// <summary>Gets a value indicating whether the user has chosen to quit.</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>Advances the menu by one simulation tick.</summary>
    /// <param name="input">This frame's input.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public void Update(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        KeyboardState keys = input.Keyboard;

        // Edge-triggered, so holding a key moves one entry — the behaviour the original's menus have.
        if (keys.WasPressed(KeyCode.Up) || keys.WasPressed(KeyCode.Numpad8))
        {
            SelectedIndex = (SelectedIndex + Entries.Length - 1) % Entries.Length;
        }

        if (keys.WasPressed(KeyCode.Down) || keys.WasPressed(KeyCode.Numpad2))
        {
            SelectedIndex = (SelectedIndex + 1) % Entries.Length;
        }

        if (keys.WasPressed(KeyCode.Escape))
        {
            SelectedIndex = Entries.Length - 1;
        }

        if (keys.WasPressed(KeyCode.Return) || keys.WasPressed(KeyCode.NumpadEnter))
        {
            if (SelectedIndex == Entries.Length - 1)
            {
                QuitRequested = true;
            }
        }
    }

    /// <summary>Draws the menu.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        batch.Begin();

        // mainmenu.pic is 150 tiles of 64x32 laid out ten across and fifteen down — exactly 640x480.
        _assets.Background.DrawTiled(
            batch,
            new RectF(0, 0, AuthoredWidth, AuthoredHeight),
            Bgra32.White,
            BlendMode.Opaque);

        _assets.Logo.DrawTiled(
            batch,
            new RectF(
                (AuthoredWidth - _assets.LogoWidth) / 2.0f,
                24.0f,
                _assets.LogoWidth,
                _assets.LogoHeight),
            Bgra32.White,
            BlendMode.AlphaBlend);

        // The main-menu screen declares font3.fnt for its entries; font1.fnt is the small body font.
        float y = 220.0f;
        for (int i = 0; i < Entries.Length; i++)
        {
            Bgra32 color = i == SelectedIndex
                ? new Bgra32(96, 220, 255, 255)
                : BitmapTextRenderer.NeutralColor;

            _title.DrawCentered(batch, Entries[i], AuthoredWidth / 2.0f, y, color);
            y += _title.LineHeight + 6.0f;
        }

        _body.Draw(
            batch,
            "arrow keys to move, enter to select, escape to quit",
            16.0f,
            AuthoredHeight - _body.LineHeight - 12.0f,
            new Bgra32(70, 70, 70, 255));

        batch.End();
    }
}
