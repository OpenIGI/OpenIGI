using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>How the computer measures its text: the fonts the presentation loaded.</summary>
public interface IIgi2ComputerFonts
{
    /// <summary>The width of a string in a font, in the screen's pixels, <c>Font_GetStringWidth</c>.</summary>
    /// <param name="font">The font's file name.</param>
    /// <param name="text">The string.</param>
    /// <returns>Its width.</returns>
    int StringWidth(string font, string text);

    /// <summary>The height of a line of a font.</summary>
    /// <param name="font">The font's file name.</param>
    /// <returns>Its height.</returns>
    int LineHeight(string font);
}

/// <summary>The keys and buttons the open computer reads, as the player's tick passes them.</summary>
/// <param name="LookX">The look channel across, which moves the pointer.</param>
/// <param name="LookY">The look channel down.</param>
/// <param name="InvertMouse">Whether the profile inverts the mouse, which the menu applies again.</param>
/// <param name="MouseLeft">Whether the left button is down.</param>
/// <param name="MouseRight">Whether the right button is down.</param>
/// <param name="WheelUp">Whether the wheel turned away since the last tick.</param>
/// <param name="WheelDown">Whether it turned toward the user.</param>
/// <param name="Up">Whether the up arrow is down.</param>
/// <param name="Down">Whether the down arrow is down.</param>
/// <param name="Left">Whether the left arrow is down.</param>
/// <param name="Right">Whether the right arrow is down.</param>
/// <param name="Enter">Whether Enter is down.</param>
/// <param name="Home">Whether Home is down.</param>
/// <param name="Control">Whether either Ctrl is down.</param>
public readonly record struct Igi2ComputerInput(
    float LookX,
    float LookY,
    bool InvertMouse = false,
    bool MouseLeft = false,
    bool MouseRight = false,
    bool WheelUp = false,
    bool WheelDown = false,
    bool Up = false,
    bool Down = false,
    bool Left = false,
    bool Right = false,
    bool Enter = false,
    bool Home = false,
    bool Control = false);

/// <summary>
/// The open computer as the player sees it, <c>ComputerMenu</c> (<c>computermenu.c</c>) and the screen it shows: a pointer the mouse
/// moves inside the casing's glass, the buttons the screens read, and the menu down the casing's left that turns to the other
/// screens; the map (<see cref="Igi2ComputerMap"/>) or the objectives behind it.
/// </summary>
/// <remarks>
/// The original lays the screens out in the display's own pixels. Here they are laid out in a screen of <see cref="Width"/> by
/// <see cref="Height"/> the presentation chooses, the casing's 480 lines high, so the menu's fonts sit on it as they sat on a 640 by
/// 480 display.
/// </remarks>
public sealed class Igi2ComputerDisplay
{
    /// <summary>The font of the menu's items and the objectives' labels, <c>g_zFontName_1</c>.</summary>
    public const string LabelFont = "font3.fnt";

    /// <summary>The font of the objectives' text, <c>g_zFontName_0</c>'s second.</summary>
    public const string TextFont = "font4.fnt";

    /// <summary>The objectives screen's map buttons' first id, <c>ComputerObjectives_ButtonItemActivateCB</c>'s case 5.</summary>
    public const int ObjectiveButtonId = 5;

    /// <summary>The objectives a set holds, <c>DefineComputerObjective</c>'s eight.</summary>
    public const int ObjectiveCount = 8;

    /// <summary>How deep the objectives' text sits, <c>522.48</c>.</summary>
    public const float TextDepth = 522.48f;

    private readonly Igi2Computer _computer;
    private Igi2ComputerButtons _previousButtons;
    private bool _enterWasDown;
    private bool _upWasDown;
    private bool _downWasDown;
    private bool _leftWasDown;
    private bool _rightWasDown;

    /// <summary>Initializes a new instance of the <see cref="Igi2ComputerDisplay"/> class.</summary>
    /// <param name="computer">The level's computer.</param>
    public Igi2ComputerDisplay(Igi2Computer computer)
    {
        _computer = computer ?? throw new ArgumentNullException(nameof(computer));
    }

    /// <summary>Gets the level's computer.</summary>
    public Igi2Computer Computer => _computer;

    /// <summary>Gets or sets the screen's width, in its pixels.</summary>
    public float Width { get; set; } = 640f;

    /// <summary>Gets or sets the screen's height.</summary>
    public float Height { get; set; } = 480f;

    /// <summary>Gets or sets the fonts the text is measured in, or <see langword="null"/> to measure eight pixels a letter.</summary>
    public IIgi2ComputerFonts? Fonts { get; set; }

    /// <summary>Gets or sets the localised text of a key, or <see langword="null"/> to show keys as they are.</summary>
    public Func<string, string>? Strings { get; set; }

    /// <summary>Gets or sets where the computer's sounds are played, by their definitions' names.</summary>
    public Action<string>? PlaySound { get; set; }

    /// <summary>Gets or sets a value indicating whether the casing is the broken one, mission 17's (<c>ComputerMenu_isBroken</c>).</summary>
    public bool IsBroken { get; set; }

    /// <summary>Gets or sets the map, which keeps where it looked between openings; <see langword="null"/> before the first.</summary>
    public Igi2ComputerMap? Map { get; set; }

    /// <summary>Gets a value indicating whether the computer is open and shown.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets the screen built, <c>ComputerMenu_eActiveMenu</c> as its children stand.</summary>
    public Igi2ComputerMenu Screen { get; private set; }

    /// <summary>Gets a value indicating whether the map is the screen shown.</summary>
    public bool IsMapShown => IsOpen && Screen == Igi2ComputerMenu.Map && _computer.Plans.Count == 0;

    /// <summary>Gets the pointer's X, <c>tMousePos</c>.</summary>
    public float PointerX { get; private set; }

    /// <summary>Gets the pointer's Y.</summary>
    public float PointerY { get; private set; }

    /// <summary>Gets or sets a value indicating whether the pointer is held where it is, the map being dragged (<c>isMouseLocked</c>).</summary>
    public bool IsPointerLocked { get; set; }

    /// <summary>Gets the item the pointer last lit, which the arrows start from, <c>ePrevMenuItem</c>.</summary>
    public int PreviousMenuItem { get; private set; }

    /// <summary>Gets how far the mouse moved the pointer this tick across, a share of half the screen (<c>ComputerMenu_vMouseSpeedX</c>).</summary>
    public float MouseSpeedX { get; private set; }

    /// <summary>Gets how far down.</summary>
    public float MouseSpeedY { get; private set; }

    /// <summary>Gets the buttons down, <c>bCurrentButton</c>.</summary>
    public Igi2ComputerButtons Buttons { get; private set; }

    /// <summary>Gets the buttons that went down this tick, <c>bEdgeButton</c>.</summary>
    public Igi2ComputerButtons EdgeButtons { get; private set; }

    /// <summary>Gets the buttons that came up this tick, <c>bEdgeReleaseButton</c>.</summary>
    public Igi2ComputerButtons EdgeReleaseButtons { get; private set; }

    /// <summary>Gets the screen's menu, <c>ptMenuItemQTask</c>.</summary>
    public Igi2ComputerMenuItems Items { get; private set; } = new();

    /// <summary>Gets the objectives screen's laid-out lines, as its draw left them; empty on another screen.</summary>
    public IReadOnlyList<Igi2ComputerObjectiveLine> ObjectiveLines => _objectiveLines;

    private readonly List<Igi2ComputerObjectiveLine> _objectiveLines = [];

    /// <summary>Puts the pointer where the map holds it.</summary>
    /// <param name="x">Its X.</param>
    /// <param name="y">Its Y.</param>
    public void HoldPointer(float x, float y)
    {
        PointerX = x;
        PointerY = y;
    }

    /// <summary>Puts the pointer in the middle of the screen, as a zoom of the map does.</summary>
    public void CentrePointer()
    {
        PointerX = ((Fpu)Width * 0.5).F;
        PointerY = ((Fpu)Height * 0.5).F;
    }

    /// <summary>The text of a key in the game's language.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text.</returns>
    public string Text(string key) => Strings?.Invoke(key) ?? key;

    /// <summary>
    /// Opens the computer, <c>Computer_CreateHandler</c> and <c>ComputerMenu_CreateHandler</c>: the pointer in the middle, nothing
    /// pressed, the map to look first at the player, and the screen the computer opened on built.
    /// </summary>
    /// <param name="player">Where the player stands, which the map looks at first (<c>ComputerMap_ptPositionQTask</c>).</param>
    public void Open(Func<Vector3d> player)
    {
        ArgumentNullException.ThrowIfNull(player);
        IsOpen = true;
        PointerX = ((Fpu)Width * 0.5).F;
        PointerY = ((Fpu)Height * 0.5).F;
        IsPointerLocked = false;
        Buttons = EdgeButtons = EdgeReleaseButtons = Igi2ComputerButtons.None;
        _previousButtons = Igi2ComputerButtons.None;
        PreviousMenuItem = 0;
        MouseSpeedX = MouseSpeedY = 0f;
        Map ??= new Igi2ComputerMap();
        Map.PositionTask = _ => player();
        Build(_computer.ActiveMenu);
    }

    /// <summary>Closes the computer, <c>Computer_DeleteHandler</c>: the screen's map remembers where it looked.</summary>
    public void Close()
    {
        if (IsOpen && Screen == Igi2ComputerMenu.Map)
        {
            Map?.Leave();
        }

        IsOpen = false;
        Items = new Igi2ComputerMenuItems();
        _objectiveLines.Clear();
    }

    /// <summary>
    /// One tick of the open computer, <c>ComputerMenu_RunHandler</c>: a screen asked for replaces the one shown; the pointer moves by
    /// twice the look channels, held inside the glass unless the map holds it; the buttons are read; the menu runs on them; then the
    /// screen's own run.
    /// </summary>
    /// <param name="input">The keys and the mouse.</param>
    /// <param name="world">The map's world, or <see langword="null"/> where it has none.</param>
    /// <param name="random">The run stream, which the map's noise draws from.</param>
    public void Run(in Igi2ComputerInput input, IIgi2ComputerMapWorld? world, IgiRandom? random)
    {
        if (!IsOpen)
        {
            return;
        }

        if (_computer.ActiveMenu != Screen)
        {
            if (Screen == Igi2ComputerMenu.Map)
            {
                Map?.Leave();
            }

            Build(_computer.ActiveMenu);
        }

        float invert = input.InvertMouse ? -1f : 1f;
        MouseSpeedX = ((Fpu)input.LookX + input.LookX).F;
        MouseSpeedY = (((Fpu)invert * input.LookY) + ((Fpu)invert * input.LookY)).F;
        if (!IsPointerLocked)
        {
            float x = (((Fpu)MouseSpeedX * Width * 0.5) + PointerX).F;
            float y = (((Fpu)MouseSpeedY * Height * 0.5) + PointerY).F;
            float left = ((Fpu)Width * 0.125).F, right = ((Fpu)Width - ((Fpu)Width * 0.109375)).F;
            float top = ((Fpu)Height * 0.0833333358168602).F, bottom = ((Fpu)Height - ((Fpu)Height * 0.2083333283662796)).F;
            PointerX = left <= x ? (right >= x ? x : right) : left;
            PointerY = top <= y ? (bottom >= y ? y : bottom) : top;
            if ((MouseSpeedX != 0f || MouseSpeedY != 0f) && Items.Active is { } lit)
            {
                PreviousMenuItem = lit.Id;
            }
        }

        Igi2ComputerButtons buttons = Igi2ComputerButtons.None;
        bool arrow = false;
        if (input.MouseLeft || (input.Enter && !_enterWasDown))
        {
            buttons = Igi2ComputerButtons.Select;
        }

        if (input.MouseRight)
        {
            buttons |= Igi2ComputerButtons.Back;
        }

        if (input.Up && !_upWasDown) { buttons |= Igi2ComputerButtons.Up; arrow = true; }
        if (input.Down && !_downWasDown) { buttons |= Igi2ComputerButtons.Down; arrow = true; }
        if (input.Left && !_leftWasDown) { buttons |= Igi2ComputerButtons.Left; arrow = true; }
        if (input.Right && !_rightWasDown) { buttons |= Igi2ComputerButtons.Right; arrow = true; }
        _enterWasDown = input.Enter;
        _upWasDown = input.Up;
        _downWasDown = input.Down;
        _leftWasDown = input.Left;
        _rightWasDown = input.Right;

        // An arrow with nothing lit lights the item the pointer last left.
        if (arrow && PreviousMenuItem != 0 && Items.Active is null)
        {
            Items.Activate(PreviousMenuItem);
        }

        // The left button held feeds the menu a still, empty input first, so the pointer counts as moved and the click as new.
        if (input.MouseLeft)
        {
            Items.SetInput(0, 0, Igi2ComputerButtons.None);
        }

        // Ctrl keeps the arrows for the map.
        Igi2ComputerButtons menuButtons = input.Control ? buttons & ~Igi2ComputerButtons.Arrows : buttons;
        Items.SetInput((int)PointerX, (int)PointerY, menuButtons);
        EdgeReleaseButtons = _previousButtons & ~buttons;
        EdgeButtons = buttons & ~_previousButtons;
        Buttons = buttons;
        _previousButtons = buttons;

        Items.Run();
        if (IsMapShown && Map is { } map)
        {
            map.Run(this, input, world, random);
        }
    }

    // ComputerMenu_New: the screen's task and its menu; the plans' menu stands in for the map while the level has plans.
    private void Build(Igi2ComputerMenu screen)
    {
        Screen = screen;
        _objectiveLines.Clear();
        Items = new Igi2ComputerMenuItems();
        switch (screen)
        {
            case Igi2ComputerMenu.Map when _computer.Plans.Count == 0:
                Map ??= new Igi2ComputerMap();
                Map.Enter(this);
                CreateScreenMenuItems(screen);
                break;
            case Igi2ComputerMenu.Objectives:
                CreateObjectives();
                break;
            default:
                CreateScreenMenuItems(screen);
                break;
        }
    }

    /// <summary>
    /// The menu down the casing's left, <c>ComputerMenu_CreateScreenMenuItems</c>: from the bottom up, the save screen's (the IGI
    /// HQ), the map's while it is enabled, the objectives' and the log's, leaving out the screen's own; each leads up and left to
    /// the next, down and right to the last, the top wrapping to the bottom. The top item is lit.
    /// </summary>
    /// <param name="screen">The screen the menu is for.</param>
    public void CreateScreenMenuItems(Igi2ComputerMenu screen)
    {
        // The click that turned to the screen is spent: its items are lit without it.
        EdgeButtons = Igi2ComputerButtons.None;
        EdgeReleaseButtons = Igi2ComputerButtons.None;
        float y = 0.7583333253860474f;
        Igi2ComputerMenuItem? last = null, first = null;
        void Add(int id, string key)
        {
            if (last is not null)
            {
                last.LinkLeft = id;
                last.LinkUp = id;
            }

            int previous = last?.Id ?? 0;
            Igi2ComputerMenuItem item = Items.Add(TextItem(id, previous, ((Fpu)Width * 0.15625).F, ((Fpu)y * Height).F, Text(key)));
            y = ((Fpu)y - 0.03333333507180214).F;
            last = item;
            first ??= item;
        }

        if (screen != Igi2ComputerMenu.Save)
        {
            Add(4, "computer/IGI HQ");
        }

        if (screen != Igi2ComputerMenu.Map && _computer.IsEnabled)
        {
            Add(1, "computer/MAP");
        }

        if (screen != Igi2ComputerMenu.Objectives)
        {
            Add(2, "computer/OBJECTIVES");
        }

        if (screen != Igi2ComputerMenu.Log)
        {
            Add(3, "computer/LOG");
        }

        if (first is null || last is null)
        {
            return;
        }

        if (!ReferenceEquals(first, last))
        {
            first.LinkRight = last.Id;
            first.LinkDown = last.Id;
            last.LinkLeft = first.Id;
            last.LinkUp = first.Id;
        }

        PreviousMenuItem = first.Id;
        Items.Activate(last.Id);
    }

    // ComputerTextItem_Init: the string's box, its widest line across and its lines' height down.
    private Igi2ComputerMenuItem TextItem(int id, int previous, float x, float y, string text)
    {
        Igi2ComputerMenuItem item = new(id, TextItemActivated)
        {
            LinkDown = previous,
            LinkRight = previous,
            X = x,
            Y = y,
            Text = text,
            Font = LabelFont,
            Colour = Igi2ComputerColour.Green,
            HighlightColour = Igi2ComputerColour.LightGreen,
        };
        item.Width = StringWidth(LabelFont, text);
        item.Height = LineHeight(LabelFont);
        return item;
    }

    // ComputerMenu_TextItemActivateCB: newly lit, it clicks; while the map is dragged the menu is off; a click turns to its screen.
    private void TextItemActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (item.IsActivated && !item.IsActive)
        {
            PlaySound?.Invoke("mapcomp_05");
        }

        if (IsPointerLocked)
        {
            item.IsActive = false;
            item.IsDisabled = true;
            return;
        }

        item.IsDisabled = false;
        if ((EdgeButtons & Igi2ComputerButtons.Select) != 0)
        {
            PlaySound?.Invoke("mapcomp_01");
            if (activation.MenuItem is >= 1 and <= 4)
            {
                _computer.RequestMenu((Igi2ComputerMenu)activation.MenuItem);
            }
        }
    }

    /// <summary>
    /// The objectives screen, <c>ComputerObjectives_CreateHandler</c>: its title, the menu, and with the map enabled a button for each
    /// objective that shows its place on the map.
    /// </summary>
    private void CreateObjectives()
    {
        CreateScreenMenuItems(Igi2ComputerMenu.Objectives);
        if (!_computer.IsEnabled)
        {
            return;
        }

        for (int i = 0; i < ObjectiveCount; i++)
        {
            Igi2ComputerMenuItem button = new(ObjectiveButtonId + i, ObjectiveButtonActivated)
            {
                X = ((Fpu)Width * -34.0).F,
                Y = ((Fpu)Height + Height).F,
                Sprites = ["objective_1.spr", "objective_2.spr", "objective_2.spr"],
                Width = 32f,
                Height = 32f,
            };
            Items.Add(button);
        }
    }

    // ComputerObjectives_ButtonItemActivateCB: newly lit, it clicks; clicked, the map looks at what the objective links to.
    private void ObjectiveButtonActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (item.IsActivated && !item.IsActive)
        {
            PlaySound?.Invoke("mapcomp_05");
        }

        if (item.Id != activation.MenuItem)
        {
            return;
        }

        bool pressed = (EdgeButtons & Igi2ComputerButtons.Select) != 0;
        item.WasPressed = item.IsPressed;
        item.IsPressed = pressed;
        if (!pressed)
        {
            return;
        }

        PlaySound?.Invoke("mapcomp_01");
        int index = item.Id - ObjectiveButtonId;
        if (index is >= 0 and < ObjectiveCount && _computer.GetObjective(index) is { LinkToPosition: not -1 } objective)
        {
            Map ??= new Igi2ComputerMap();
            Map.PositionTaskId = objective.LinkToPosition;
            _computer.RequestMenu(Igi2ComputerMenu.Map);
        }
    }

    /// <summary>
    /// Lays the objectives out as their screen's draw does (<c>ComputerObjectives_DrawHandler</c>): from an eighth of the way down,
    /// each objective with text its number and state on one line and its text wrapped under it, eight pixels between; its map
    /// button moved to its left. Called by the presentation before it draws, the layout being in the fonts it measures.
    /// </summary>
    /// <returns>The lines.</returns>
    public IReadOnlyList<Igi2ComputerObjectiveLine> LayoutObjectives()
    {
        _objectiveLines.Clear();
        int x = (int)(long)(_computer.IsEnabled ? (Fpu)Width * 0.3125 : (Fpu)Width * 0.153125).Value;
        int y = (int)(long)((Fpu)Height * 0.12083333333333333).Value;
        for (int i = 0; i < ObjectiveCount; i++)
        {
            Igi2ComputerObjective? objective = _computer.GetObjective(i);
            if (objective is null || objective.TextResource.Length == 0)
            {
                continue;
            }

            if (_computer.IsEnabled && Items.Find(ObjectiveButtonId + i) is { } button)
            {
                button.X = x - 34f;
                button.Y = y + 2f;
            }

            string label = Text("OBJECTIVE") + " " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": ";
            string state = Text(objective.IsFailed ? "FAILED" : objective.IsComplete ? "COMPLETE" : "INCOMPLETE");
            Igi2ComputerColour stateColour = objective.IsFailed ? Igi2ComputerColour.DarkRed
                : objective.IsComplete ? Igi2ComputerColour.LightGreen : Igi2ComputerColour.LightGrey;
            Print(x, y, label, LabelFont, Igi2ComputerColour.White);
            int next = Print(x + StringWidth(LabelFont, label), y, state, LabelFont, stateColour);
            y = Print(x, next, Text(objective.TextResource), TextFont, Igi2ComputerColour.Green) + 8;
        }

        return _objectiveLines;
    }

    // ComputerObjectives_Print: word by word, each word with the space after it, a word that does not fit on what is left of
    // the line (400 pixels) starting the next; the Y under the last line.
    private int Print(int x, int y, string text, string font, Igi2ComputerColour colour)
    {
        const int lineWidth = 400;
        int lineHeight = LineHeight(font);
        int printX = x, left = lineWidth;
        int index = 0;
        while (true)
        {
            int space = text.IndexOf(' ', index);
            bool complete = space < 0;
            string word = complete ? text[index..] : text[index..(space + 1)];
            index = complete ? text.Length : space + 1;
            int width = StringWidth(font, word);
            if (left <= width)
            {
                printX = x;
                y += lineHeight;
                left = lineWidth - width;
            }
            else
            {
                left -= width;
            }

            _objectiveLines.Add(new Igi2ComputerObjectiveLine(word, font, printX, y, colour));
            printX += width;
            if (complete)
            {
                break;
            }
        }

        return y + lineHeight;
    }

    private int StringWidth(string font, string text) => Fonts?.StringWidth(font, text) ?? (text.Length * 8);

    private int LineHeight(string font) => Fonts?.LineHeight(font) ?? 16;
}

/// <summary>A word of the objectives screen, where its print put it.</summary>
/// <param name="Text">The word, with the space after it.</param>
/// <param name="Font">Its font.</param>
/// <param name="X">Its left, in the screen's pixels.</param>
/// <param name="Y">Its top.</param>
/// <param name="Colour">Its colour.</param>
public readonly record struct Igi2ComputerObjectiveLine(string Text, string Font, int X, int Y, Igi2ComputerColour Colour);
