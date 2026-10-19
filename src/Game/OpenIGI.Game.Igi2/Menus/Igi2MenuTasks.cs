using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Menus;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Menus;

/// <summary>An item a list of the menu holds: its text, its id, and whether the text is a key into the strings, <c>ListBoxItem_t</c>.</summary>
/// <param name="Text">The text, or the key it is looked up by.</param>
/// <param name="Id">The id the list's scripts know it by.</param>
/// <param name="IsTranslated">Whether the text is a key into the localized strings.</param>
public readonly record struct Igi2MenuItem(string Text, int Id, bool IsTranslated = false);

/// <summary>A list of the menu that its collect script fills through the natives, the active list as the script runs.</summary>
public interface IIgi2MenuItemList
{
    /// <summary>Gets the items, in the order added.</summary>
    IReadOnlyList<Igi2MenuItem> Items { get; }

    /// <summary>Adds an item, unless one with its id is there: <c>ListBox_AddItem</c>, <c>ScrollListBox_AddItemEx</c>.</summary>
    /// <param name="item">The item.</param>
    void Add(Igi2MenuItem item);
}

/// <summary>The in-game menu's manager, I.G.I. 2's <c>MenuManager</c> (menumanager.c); its state is the <see cref="Igi2InGameMenu"/>'s.</summary>
public sealed partial class Igi2MenuManagerTask : Igi2MenuWindowTask, IMenuManager
{
    private readonly Dictionary<(string Source, GameTask? Self), LevelExpression> _compiled = [];
    private Igi2LevelRun? _level;

    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuManager";

    /// <summary>Gets or sets the expression whose value is the screen the menu opens on, <c>MenuManager_GetDefaultMenuID</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Default menuscreen expression")]
    public string DefaultScreenExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the screen it starts on.</summary>
    [ScriptParam(ScriptParamType.Int32, "Current menuscreen")]
    public int CurrentScreen { get; set; } = -1;

    /// <summary>Gets or sets the resource file its sprites, pictures and fonts are in.</summary>
    [ScriptParam(ScriptParamType.String256, "Resource path")]
    public string ResourcePath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it sets the display itself.</summary>
    [ScriptParam(ScriptParamType.Bool8, "isOwnDisplay")]
    public bool IsOwnDisplay { get; set; }

    /// <summary>Gets or sets the expression that allows the attract demo.</summary>
    [ScriptParam(ScriptParamType.VarString, "Enable demo expression")]
    public string EnableDemoExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu it runs.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    public MenuPointer Pointer { get; } = new();

    /// <summary>Gets or sets where the menu's sounds are played; <see langword="null"/> plays none.</summary>
    /// <remarks>Whatever presents the menu connects it, as it connects the pointer.</remarks>
    public Action<string>? Sounds { get; set; }

    /// <inheritdoc/>
    /// <remarks><c>MenuManager_RunHandler</c>: the menu's state takes its step.</remarks>
    public override void Run(Game.Level.LevelRun level)
    {
        if (level is Igi2LevelRun own)
        {
            _level = own;
            Menu?.Run(own);
        }
    }

    /// <inheritdoc/>
    /// <remarks>In this game's dialect, against the level the manager last ran in, compiled once per control.</remarks>
    public void RunAction(string source, GameTask? self = null) => _ = EvaluateReal(source, self);

    /// <inheritdoc/>
    public double EvaluateReal(string source, GameTask? self = null)
    {
        if (_level is not { } level || string.IsNullOrWhiteSpace(source))
        {
            return 0.0;
        }

        if (!_compiled.TryGetValue((source, self), out LevelExpression? compiled))
        {
            compiled = Igi2LevelExpressions.Create(source, (self ?? this).Name, level.Natives);
            _compiled[(source, self)] = compiled;
        }

        return compiled.IsEmpty ? 0.0 : level.Evaluate(compiled);
    }

    /// <inheritdoc/>
    public void PlaySound(string name) => Sounds?.Invoke(name);

    /// <summary>The screen the menu opens on, <c>MenuManager_GetDefaultMenuID</c>: the default expression's value, or -1 for none.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <returns>The screen's id, or -1.</returns>
    public int DefaultScreen(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        LevelExpression? expression = null;
        return TryRun(level, ref expression, DefaultScreenExpression, out double value) ? (int)LevelExpression.Truncate(value) : -1;
    }
}

/// <summary>A screen of the menu, <c>MenuScreen</c> (menuscreen.c): its init and change scripts run before its windows'.</summary>
public sealed partial class Igi2MenuScreenTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuScreen";

    private LevelExpression? _onInit;
    private LevelExpression? _onChange;
    private int _userData;

    /// <summary>Gets or sets its background picture.</summary>
    [ScriptParam(ScriptParamType.String32, "Background")]
    public string Background { get; set; } = string.Empty;

    /// <summary>Gets or sets its logo.</summary>
    [ScriptParam(ScriptParamType.String32, "Logo")]
    public string Logo { get; set; } = string.Empty;

    /// <summary>Gets or sets the logo's centre across.</summary>
    [ScriptParam(ScriptParamType.Int32, "Center Logo X")]
    public int LogoX { get; set; }

    /// <summary>Gets or sets the logo's centre down.</summary>
    [ScriptParam(ScriptParamType.Int32, "Center Logo Y")]
    public int LogoY { get; set; }

    /// <summary>Gets or sets a value indicating whether it is framed.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use Frame?")]
    public bool UseFrame { get; set; }

    /// <summary>Gets or sets its frame's left.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame x0")]
    public int FrameX0 { get; set; }

    /// <summary>Gets or sets its frame's top.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame y0")]
    public int FrameY0 { get; set; }

    /// <summary>Gets or sets its frame's right.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame x1")]
    public int FrameX1 { get; set; }

    /// <summary>Gets or sets its frame's bottom.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame y1")]
    public int FrameY1 { get; set; }

    /// <summary>Gets or sets the script Escape runs on it.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Press Escape")]
    public string OnPressEscape { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as it takes its data in.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Init Data")]
    public string OnInitData { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as it gives its data back.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Change Data")]
    public string OnChangeData { get; set; } = string.Empty;

    /// <summary>Gets or sets the ticks it waits before it times out; -1 for never.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ticks to Timeout")]
    public int TicksToTimeout { get; set; } = -1;

    /// <summary>Gets or sets the screen it times out to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Timeout Screen")]
    public int TimeoutScreen { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether its items are right-justified.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Right Justify?")]
    public bool RightJustify { get; set; }

    /// <summary>Gets the script value its scripts keep on it, <c>nUserData</c>.</summary>
    public int UserData => _userData;

    /// <inheritdoc/>
    /// <remarks><c>MenuScreen_InitDataHandler</c>: its init script, then its windows.</remarks>
    public override void InitData(Igi2LevelRun level)
    {
        TryRun(level, ref _onInit, OnInitData, out _);
        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>MenuScreen_ChangeDataHandler</c>: its change script, then its windows.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _onChange, OnChangeData, out _);
        base.ChangeData(level);
    }

    /// <summary>Runs its Escape script, as Escape pressed on it does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    public void PressEscape(Igi2LevelRun level)
    {
        LevelExpression? escape = null;
        TryRun(level, ref escape, OnPressEscape, out _);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nUserData", () => _userData, value => _userData = value);
    }
}

/// <summary>A frame that lines up the menu's items, <c>MenuFrame</c> (menuframe.c).</summary>
public sealed partial class Igi2MenuFrameTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuFrame";

    /// <summary>Gets or sets its sprite.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets its left.</summary>
    [ScriptParam(ScriptParamType.Int32, "X-Pos")]
    public int X { get; set; }

    /// <summary>Gets or sets its top.</summary>
    [ScriptParam(ScriptParamType.Int32, "Y-Pos")]
    public int Y { get; set; }

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets its height.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get; set; }

    /// <summary>Gets or sets how it lines its items up.</summary>
    [ScriptParam(ScriptParamType.Int32, "LineUpMode")]
    public int LineUpMode { get; set; }

    /// <summary>Gets or sets how it aligns its items.</summary>
    [ScriptParam(ScriptParamType.Int32, "AlignmentMode")]
    public int AlignmentMode { get; set; }

    /// <summary>Gets or sets the space between its items across.</summary>
    [ScriptParam(ScriptParamType.Int16, "X-Spacing")]
    public short XSpacing { get; set; }

    /// <summary>Gets or sets the space between its items down.</summary>
    [ScriptParam(ScriptParamType.Int16, "Y-Spacing")]
    public short YSpacing { get; set; }

    /// <summary>Gets or sets a value indicating whether it is selected as one item.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is Selectable")]
    public bool IsSelectable { get; set; }
}

/// <summary>A line of the menu's text, <c>MenuText</c> (menutext.c): clicked, it runs its script.</summary>
public sealed partial class Igi2MenuTextTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuText";

    private int _id;

    /// <summary>Gets or sets the key its text is looked up by.</summary>
    [ScriptParam(ScriptParamType.String32, "Text Resource")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets its font.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string Font { get; set; } = string.Empty;

    /// <summary>Gets or sets its colour, a <c>TEXT_*</c> value.</summary>
    [ScriptParam(ScriptParamType.Int32, "ColourIndex")]
    public int ColourIndex { get; set; }

    /// <summary>Gets or sets how it is aligned.</summary>
    [ScriptParam(ScriptParamType.Int32, "AlignmentMode")]
    public int AlignmentMode { get; set; }

    /// <summary>Gets or sets the expression that says whether it can be clicked.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it shows.</summary>
    [ScriptParam(ScriptParamType.VarString, "isVisible Expression")]
    public string VisibleExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the script a click runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Click Script")]
    public string OnClick { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound a click makes.</summary>
    [ScriptParam(ScriptParamType.String32, "Click Sound Resource")]
    public string ClickSound { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it is dimmed while it cannot be clicked.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is Item Dimmed When Disabled")]
    public bool IsDimmedWhenDisabled { get; set; }

    /// <summary>Gets or sets the expression that collects its text.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect text Expression")]
    public string CollectTextExpression { get; set; } = string.Empty;

    /// <summary>Gets the id its scripts give it, <c>nID</c>.</summary>
    public int TextId => _id;

    /// <summary>Gets whether it can be clicked: its enabled expression, or with none, always.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <returns>Whether it can be clicked.</returns>
    public bool IsEnabled(Igi2LevelRun level)
    {
        LevelExpression? enabled = null;
        return !TryRun(level, ref enabled, EnabledExpression, out double value) || value != 0.0;
    }

    /// <summary>Gets whether it shows: its visible expression, or with none, always.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <returns>Whether it shows.</returns>
    public bool IsVisible(Igi2LevelRun level)
    {
        LevelExpression? visible = null;
        return !TryRun(level, ref visible, VisibleExpression, out double value) || value != 0.0;
    }

    /// <summary>Clicks it: its click script runs, when it can be clicked.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <returns>Whether it could be clicked.</returns>
    public bool Click(Igi2LevelRun level)
    {
        if (!IsEnabled(level))
        {
            return false;
        }

        LevelExpression? click = null;
        TryRun(level, ref click, OnClick, out _);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nID", () => _id, value => _id = value);
    }
}

/// <summary>A line of text that shows which of several is selected, <c>MenuTextSelection</c> (menutext.c); it declares no parameters.</summary>
public sealed partial class Igi2MenuTextSelectionTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuTextSelection";
}

/// <summary>A slider, <c>SlideBar</c> (slidebar.c): a value between nought and one, or one of a number of options.</summary>
public sealed partial class Igi2SlideBarTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SlideBar";

    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _modifyData;
    private LevelExpression? _numOptions;
    private double _value;
    private int _intValue;
    private int _options;

    /// <summary>Gets or sets its sprite.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it lies across.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Horizontal")]
    public bool Horizontal { get; set; }

    /// <summary>Gets or sets its length.</summary>
    [ScriptParam(ScriptParamType.Int32, "Size")]
    public int Size { get; set; }

    /// <summary>Gets or sets the script whose value it takes in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its value back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as its value is moved.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is how fast its caret moves.</summary>
    [ScriptParam(ScriptParamType.VarString, "Carret Speed")]
    public string CaretSpeed { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is how many options it has; none for a continuous value.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Num Options")]
    public string NumOptionsScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it can be moved.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets its value, nought to one, <c>vValue</c>.</summary>
    public double Value => _value;

    /// <summary>Gets how many options it has; nought for a continuous value.</summary>
    public int Options => _options;

    /// <summary>Moves it to a value, its modify script running as a drag does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="value">The value, nought to one.</param>
    public void Modify(Igi2LevelRun level, double value)
    {
        _value = Math.Clamp(value, 0.0, 1.0);
        _intValue = _options > 0 ? Math.Min((int)(_value * _options), _options - 1) : 0;
        TryRun(level, ref _modifyData, ModifyDataScript, out _);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SlideBar_InitDataHandler</c>: its options counted; its get script read, as an option, the middle of that option's share, or as
    /// the value itself.
    /// </remarks>
    public override void InitData(Igi2LevelRun level)
    {
        if (TryRun(level, ref _numOptions, NumOptionsScript, out double options))
        {
            _options = (int)LevelExpression.Truncate(options);
        }

        if (TryRun(level, ref _getData, GetDataScript, out double data))
        {
            if (_options != 0)
            {
                _intValue = (int)LevelExpression.Truncate(data);
                _value = (_intValue + 0.5) / _options;
            }
            else
            {
                _value = data;
            }
        }

        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>SlideBar_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindReal(this, "vValue", () => _value, value => _value = value);
        variables.BindInt(this, "nValue", () => _intValue, value => _intValue = value);
    }
}

/// <summary>A list the menu picks one of, <c>ListBox</c> (listbox.c), filled by its collect script.</summary>
public sealed partial class Igi2ListBoxTask : Igi2MenuWindowTask, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ListBox";

    /// <summary>The most items it holds.</summary>
    public const int Capacity = 128;

    private readonly List<Igi2MenuItem> _items = [];
    private LevelExpression? _collect;
    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private int _activeId;

    /// <summary>Gets or sets its sprite.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets its font.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string Font { get; set; } = string.Empty;

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets the script that fills it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items Script")]
    public string CollectScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is the id it takes in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its id back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as another is picked.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it can be changed.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives fill it.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<Igi2MenuItem> Items => _items;

    /// <summary>Gets the id of the item picked, <c>nActiveID</c>.</summary>
    public int ActiveId => _activeId;

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item)
    {
        if (_items.Count < Capacity && !_items.Exists(i => i.Id == item.Id))
        {
            _items.Add(item);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>ListBox_InitDataHandler</c>: emptied, filled by its collect script while it is the active list, and set to the item its get
    /// script names, if it holds it.
    /// </remarks>
    public override void InitData(Igi2LevelRun level)
    {
        _items.Clear();
        Menu?.Collect(this, () => TryRun(level, ref _collect, CollectScript, out _));
        if (TryRun(level, ref _getData, GetDataScript, out double id))
        {
            int wanted = (int)LevelExpression.Truncate(id);
            if (_items.Exists(i => i.Id == wanted))
            {
                _activeId = wanted;
            }
        }

        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>ListBox_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActiveID", () => _activeId, value => _activeId = value);
    }
}

/// <summary>A yes-or-no box, <c>ToggleBox</c> (togglebox.c).</summary>
public sealed partial class Igi2ToggleBoxTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ToggleBox";

    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _modifyData;
    private int _isOn;

    /// <summary>Gets or sets the script whose value it takes in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its value back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as it is flipped.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it can be flipped.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it is on, <c>isOn</c>.</summary>
    public bool IsOn => _isOn != 0;

    /// <summary>Flips it, its modify script running as a click does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    public void Toggle(Igi2LevelRun level)
    {
        _isOn = _isOn != 0 ? 0 : 1;
        TryRun(level, ref _modifyData, ModifyDataScript, out _);
    }

    /// <inheritdoc/>
    /// <remarks><c>ToggleBox_InitDataHandler</c>: on while its get script is not nought.</remarks>
    public override void InitData(Igi2LevelRun level)
    {
        if (TryRun(level, ref _getData, GetDataScript, out double on))
        {
            _isOn = on != 0.0 ? 1 : 0;
        }

        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>ToggleBox_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isOn", () => _isOn, value => _isOn = value);
    }
}

/// <summary>A box text is typed into, <c>InputBox</c> (inputbox.c); its get script writes the text as the active input box.</summary>
public sealed partial class Igi2InputBoxTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "InputBox";

    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private string _data = string.Empty;

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets the most characters it takes.</summary>
    [ScriptParam(ScriptParamType.Int32, "String Length")]
    public int Length { get; set; }

    /// <summary>Gets or sets the script that writes its text in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its text back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs as its text changes.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Modify Script")]
    public string OnModifyScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether its text is hidden.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Hide Input")]
    public bool HideInput { get; set; }

    /// <summary>Gets or sets a value indicating whether it takes only digits.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Numeric Input")]
    public bool NumericInput { get; set; }

    /// <summary>Gets or sets the menu whose natives write its text.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <summary>Gets or sets its text, <c>zData</c>.</summary>
    public string Data
    {
        get => _data;
        set => _data = value.Length > Length && Length > 0 ? value[..Length] : value;
    }

    /// <inheritdoc/>
    /// <remarks><c>InputBox_InitDataHandler</c>: its get script, while it is the active input box.</remarks>
    public override void InitData(Igi2LevelRun level)
    {
        Menu?.Collect(this, () => TryRun(level, ref _getData, GetDataScript, out _));
        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>InputBox_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindText(this, "zData", () => _data, value => Data = value, Math.Max(Length, 1) + 1);
    }
}

/// <summary>A scrolling list, <c>ScrollListBox</c> (scrolllistbox.c), filled by its collect script.</summary>
public sealed partial class Igi2ScrollListBoxTask : Igi2MenuWindowTask, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ScrollListBox";

    private readonly List<Igi2MenuItem> _items = [];
    private LevelExpression? _collect;
    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _onSelect;
    private int _activeId = -1;
    private int _activeEntry = -1;
    private int _isUpdate;

    /// <summary>Gets or sets its font.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string Font { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that fills it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items Script")]
    public string CollectScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is the id it takes in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its id back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets its height.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get; set; }

    /// <summary>Gets or sets the script picking an item runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Select Item")]
    public string OnSelectItem { get; set; } = string.Empty;

    /// <summary>Gets or sets the script moving onto an item runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Focus Item")]
    public string OnFocusItem { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives fill it.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<Igi2MenuItem> Items => _items;

    /// <summary>Gets the id of the item picked, <c>nActiveID</c>.</summary>
    public int ActiveId => _activeId;

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item)
    {
        if (!_items.Exists(i => i.Id == item.Id))
        {
            _items.Add(item);
        }
    }

    /// <summary>Picks an item, its select script running as a double click does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="id">The item's id.</param>
    public void Select(Igi2LevelRun level, int id)
    {
        int at = _items.FindIndex(i => i.Id == id);
        if (at < 0)
        {
            return;
        }

        (_activeEntry, _activeId) = (at, id);
        TryRun(level, ref _onSelect, OnSelectItem, out _);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>ScrollListBox_InitDataHandler</c>: emptied, filled by its collect script while it is the active list, and set to the item its
    /// get script names, if it holds it.
    /// </remarks>
    public override void InitData(Igi2LevelRun level)
    {
        _items.Clear();
        Menu?.Collect(this, () => TryRun(level, ref _collect, CollectScript, out _));
        if (TryRun(level, ref _getData, GetDataScript, out double id))
        {
            int wanted = (int)LevelExpression.Truncate(id);
            int at = _items.FindIndex(i => i.Id == wanted);
            if (at >= 0)
            {
                (_activeEntry, _activeId) = (at, wanted);
            }
        }

        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>ScrollListBox_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActiveID", () => _activeId, value => _activeId = value);
        variables.BindInt(this, "isUpdate", () => _isUpdate, value => _isUpdate = value);
        variables.BindInt(this, "nActiveEntry", () => _activeEntry, value => _activeEntry = value);
    }
}

/// <summary>A row of pictures, one active, <c>PictureBox</c> (picturebox.c): the crosshair chooser.</summary>
public sealed partial class Igi2PictureBoxTask : Igi2MenuWindowTask, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "PictureBox";

    private readonly List<Igi2MenuItem> _items = [];
    private LevelExpression? _collect;
    private LevelExpression? _active;
    private LevelExpression? _rgba;

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets its height.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get; set; }

    /// <summary>Gets or sets the script that fills it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items")]
    public string CollectScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is the picture shown.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Active Picture")]
    public string ActivePictureScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that colours it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get RGBA")]
    public string RgbaScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script a click runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Click Script")]
    public string OnClick { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound a click makes.</summary>
    [ScriptParam(ScriptParamType.String32, "Click Sound Resource")]
    public string ClickSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives fill it.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<Igi2MenuItem> Items => _items;

    /// <summary>Gets the picture shown.</summary>
    public int ActivePicture { get; private set; }

    /// <summary>Gets or sets the colour it is drawn in, <c>vR</c>, <c>vG</c>, <c>vB</c>, <c>vA</c>.</summary>
    public (float R, float G, float B, float A) Colour { get; set; } = (1f, 1f, 1f, 1f);

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item) => _items.Add(item);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>PictureBox_InitDataHandler</c> and its run handler: emptied and filled by its collect script while it is the active picture
    /// box; its active picture and its colour read.
    /// </remarks>
    public override void InitData(Igi2LevelRun level)
    {
        _items.Clear();
        Menu?.Collect(this, () =>
        {
            TryRun(level, ref _collect, CollectScript, out _);
            if (TryRun(level, ref _active, ActivePictureScript, out double active))
            {
                ActivePicture = (int)LevelExpression.Truncate(active);
            }

            TryRun(level, ref _rgba, RgbaScript, out _);
        });
        base.InitData(level);
    }
}

/// <summary>A popup window, <c>DialogWindow</c> (dialogwindow.c).</summary>
public sealed partial class Igi2DialogWindowTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DialogWindow";

    /// <summary>Gets or sets its left.</summary>
    [ScriptParam(ScriptParamType.Int32, "X-Pos")]
    public int X { get; set; }

    /// <summary>Gets or sets its top.</summary>
    [ScriptParam(ScriptParamType.Int32, "Y-Pos")]
    public int Y { get; set; }

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets its height.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get; set; }

    /// <summary>Gets or sets a value indicating whether its keys are swapped.</summary>
    [ScriptParam(ScriptParamType.Bool8, "SwapKeys")]
    public bool SwapKeys { get; set; }
}

/// <summary>A table of rows in columns, <c>ColumnView</c> (columnview.c), filled by its collect script and sorted by a column.</summary>
public sealed partial class Igi2ColumnViewTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ColumnView";

    private readonly List<string[]> _rows = [];
    private LevelExpression? _collect;
    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private int _activeId = -1;
    private int _isUpdate;

    /// <summary>Gets or sets its font.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string Font { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that fills it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items Script")]
    public string CollectScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is the row it takes in.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that gives its row back.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets its height.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get; set; }

    /// <summary>Gets or sets the script picking a row runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Select Item")]
    public string OnSelectItem { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that runs each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Run")]
    public string OnRun { get; set; } = string.Empty;

    /// <summary>Gets or sets the script moving onto a row runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Focus")]
    public string OnFocus { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it can be used.</summary>
    [ScriptParam(ScriptParamType.VarString, "Is Enabled")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets its rows, a cell for each column.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

    /// <summary>Gets the row picked, <c>nActiveID</c>.</summary>
    public int ActiveId => _activeId;

    /// <summary>Adds a row, <c>ColumnView_AddEntry</c>.</summary>
    /// <param name="cells">A cell for each column.</param>
    public void AddRow(params string[] cells) => _rows.Add(cells);

    /// <summary>
    /// Sorts its rows by a column, <c>ColumnView_SortEntries</c>: the column's type, from its <c>ColumnViewColumn</c>, says whether
    /// cells compare as text or as numbers; descending when asked.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <param name="descending">Whether the largest come first.</param>
    public void Sort(int column, bool descending)
    {
        Igi2ColumnViewColumnTask[] columns = [.. Children.OfType<Igi2ColumnViewColumnTask>()];
        if (column < 0 || column >= columns.Length)
        {
            return;
        }

        bool numeric = columns[column].DataType != 0;
        Comparison<string[]> compare = numeric
            ? (a, b) => Number(a, column).CompareTo(Number(b, column))
            : (a, b) => string.Compare(Cell(a, column), Cell(b, column), StringComparison.OrdinalIgnoreCase);
        _rows.Sort(descending ? (a, b) => compare(b, a) : compare);

        static string Cell(string[] row, int at) => at < row.Length ? row[at] : string.Empty;
        static double Number(string[] row, int at) =>
            double.TryParse(Cell(row, at), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.0;
    }

    /// <inheritdoc/>
    /// <remarks><c>ColumnView_InitDataHandler</c>: emptied, filled by its collect script, and set to the row its get script names, or -1.</remarks>
    public override void InitData(Igi2LevelRun level)
    {
        _rows.Clear();
        TryRun(level, ref _collect, CollectScript, out _);
        _activeId = TryRun(level, ref _getData, GetDataScript, out double id) ? (int)LevelExpression.Truncate(id) : -1;
        base.InitData(level);
    }

    /// <inheritdoc/>
    /// <remarks><c>ColumnView_ChangeDataHandler</c>: its set script.</remarks>
    public override void ChangeData(Igi2LevelRun level)
    {
        TryRun(level, ref _setData, SetDataScript, out _);
        base.ChangeData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActiveID", () => _activeId, value => _activeId = value);
        variables.BindInt(this, "isUpdate", () => _isUpdate, value => _isUpdate = value);
    }
}

/// <summary>One column of a <see cref="Igi2ColumnViewTask"/>, <c>ColumnViewColumn</c> (columnviewcolumn.c).</summary>
public sealed partial class Igi2ColumnViewColumnTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ColumnViewColumn";

    /// <summary>Gets or sets its width.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get; set; }

    /// <summary>Gets or sets what its cells hold: nought text, otherwise numbers.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Column data type")]
    public int DataType { get; set; }
}

/// <summary>
/// The key bindings page, <c>ControlsMenu</c> (controlsmenu.c), which the engine owns and the script spawns with no parameters: its
/// offset is how far its rows are scrolled, which its scroll bar shows a forty-third at a time.
/// </summary>
/// <remarks>
/// The bindings themselves are the OpenIGI shell's, which presents the controls; the page keeps only its scroll and reset state, which
/// the menu's scripts read and write.
/// </remarks>
public sealed partial class Igi2ControlsMenuTask : Igi2MenuWindowTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ControlsMenu";

    private int _offset;
    private byte _isReset;

    /// <summary>Gets how far its rows are scrolled, <c>nOffset</c>.</summary>
    public int Offset => _offset;

    /// <summary>Gets a value indicating whether its bindings are to be reset, <c>isReset</c>.</summary>
    public bool IsReset => _isReset != 0;

    /// <inheritdoc/>
    /// <remarks><c>ControlsMenu_InitData</c>: back to the top, nothing to reset.</remarks>
    public override void InitData(Igi2LevelRun level)
    {
        _offset = 0;
        _isReset = 0;
        base.InitData(level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nOffset", () => _offset, value => _offset = value);
        variables.BindInt(this, "isReset", () => _isReset, value => _isReset = (byte)value, sizeof(byte));
    }
}
