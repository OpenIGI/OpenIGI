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

/// <summary>A screen of the menu, <c>MenuScreen</c> (menuscreen.c): the shared screen, with what I.G.I. 2 adds to it.</summary>
/// <remarks>
/// I.G.I. 2 declares its screen's parameters with its own between Project I.G.I.'s -- the logo and the frame switch before the
/// frame, the timeout and the justification after -- so it is set by name.
/// </remarks>
public sealed partial class Igi2MenuScreenTask : MenuScreenTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuScreen";

    private LevelExpression? _onInit;
    private LevelExpression? _onChange;

    /// <summary>Gets or sets its logo.</summary>
    [ScriptParam(ScriptParamType.String32, "Logo")]
    public string LogoPicture { get; set; } = string.Empty;

    /// <summary>Gets or sets the logo's centre across.</summary>
    [ScriptParam(ScriptParamType.Int32, "Center Logo X")]
    public int LogoCentreX { get; set; }

    /// <summary>Gets or sets the logo's centre down.</summary>
    [ScriptParam(ScriptParamType.Int32, "Center Logo Y")]
    public int LogoCentreY { get; set; }

    /// <summary>Gets or sets a value indicating whether it is framed.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use Frame?")]
    public bool UseFrame { get; set; } = true;

    /// <summary>Gets or sets the ticks it waits before it times out; -1 for never.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ticks to Timeout")]
    public int TicksToTimeout { get; set; } = -1;

    /// <summary>Gets or sets the screen it times out to.</summary>
    [ScriptParam(ScriptParamType.Int32, "Timeout Screen")]
    public int TimeoutScreen { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether its items are right-justified.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Right Justify?")]
    public bool RightJustify { get; set; }

    /// <inheritdoc/>
    /// <remarks><c>MenuScreen_InitDataHandler</c>: its init script, then its windows.</remarks>
    public void InitData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _onInit, OnInitData, this, out _);
        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    /// <remarks><c>MenuScreen_ChangeDataHandler</c>: its change script, then its windows.</remarks>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _onChange, OnChangeData, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <summary>Runs its Escape script, as Escape pressed on it does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    public void PressEscape(Igi2LevelRun level)
    {
        LevelExpression? escape = null;
        Igi2MenuData.TryRun(level, ref escape, OnPressEscape, this, out _);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nUserData", () => UserData, value => UserData = value);
    }
}

/// <summary>A frame that lines up the menu's items, <c>MenuFrame</c> (menuframe.c): the shared frame, with its alignment mode.</summary>
/// <remarks>I.G.I. 2 declares its alignment between Project I.G.I.'s line-up and spacing, so it is set by name.</remarks>
public sealed partial class Igi2MenuFrameTask : MenuFrameTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuFrame";

    /// <summary>Initializes a new instance of the <see cref="Igi2MenuFrameTask"/> class with <c>MenuFrame_CreateHandler</c>'s defaults.</summary>
    /// <remarks>Its rectangle is 220, 220, 200 wide and 40 high until the level's parameters say otherwise (menuframe.c).</remarks>
    public Igi2MenuFrameTask()
    {
        X = 220;
        Y = 220;
        Width = 200;
        Height = 40;
    }

    /// <summary>Gets or sets how it is placed across its parent: 0 as positioned, 1 centred, 2 at the parent's left (<c>eHorAlignmentMode</c>).</summary>
    [ScriptParam(ScriptParamType.Int32, "AlignmentMode")]
    public int AlignmentMode
    {
        get => _alignmentMode;
        set
        {
            _alignmentMode = value;
            CentreHorizontally = value == 1;
        }
    }

    private int _alignmentMode;

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level) => Igi2MenuData.InitChildren(this, level);

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);
}

/// <summary>
/// A line of the menu's text, <c>MenuText</c> (menutext.c): the shared one, with what I.G.I. 2 adds -- the enabled and visible
/// expressions Project I.G.I. kept on a separate <c>MenuTextConditional</c>, its alignment, dimming and collected text.
/// </summary>
/// <remarks>I.G.I. 2 declares its additions between Project I.G.I.'s colour and click script, so it is set by name.</remarks>
public sealed partial class Igi2MenuTextTask : MenuTextTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MenuText";

    private int _id;

    /// <summary>Gets or sets how it is placed across its parent (<c>eHorAlignmentMode</c>); it is centred unless the level says not.</summary>
    [ScriptParam(ScriptParamType.Int32, "AlignmentMode")]
    public int AlignmentMode { get; set; } = 1;

    /// <summary>Gets or sets the expression that says whether it can be clicked.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it shows.</summary>
    [ScriptParam(ScriptParamType.VarString, "isVisible Expression")]
    public string VisibleExpression { get; set; } = string.Empty;

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
    /// <returns>Whether it is enabled.</returns>
    public bool IsEnabled(Igi2LevelRun level)
    {
        LevelExpression? enabled = null;
        return !Igi2MenuData.TryRun(level, ref enabled, EnabledExpression, this, out double value) || value != 0.0;
    }

    /// <summary>Gets whether it shows: its visible expression, or with none, always.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <returns>Whether it is visible.</returns>
    public bool IsVisible(Igi2LevelRun level)
    {
        LevelExpression? visible = null;
        return !Igi2MenuData.TryRun(level, ref visible, VisibleExpression, this, out double value) || value != 0.0;
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
        Igi2MenuData.TryRun(level, ref click, OnClickScript, this, out _);
        return true;
    }

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level) => Igi2MenuData.InitChildren(this, level);

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);

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

/// <summary>A slider, <c>SlideBar</c> (slidebar.c): the shared one, with the enabled expression I.G.I. 2 adds.</summary>
public sealed partial class Igi2SlideBarTask : SlideBarTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SlideBar";

    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _modifyData;
    private LevelExpression? _numOptions;
    private int _intValue;
    private int _options;

    /// <summary>Gets or sets the expression that says whether it can be moved.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets how many options it steps between; 0 for a free value.</summary>
    public int Options => _options;

    /// <summary>Moves it to a value, and runs its modify script, as dragging or stepping it does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="value">The value, clamped between nought and one.</param>
    public void Modify(Igi2LevelRun level, double value)
    {
        Value = Math.Clamp(value, 0.0, 1.0);
        _intValue = _options > 0 ? Math.Min((int)(Value * _options), _options - 1) : 0;
        Igi2MenuData.TryRun(level, ref _modifyData, ModifyDataScript, this, out _);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SlideBar_InitDataHandler</c>: the number of options first, then the get script -- an option's index stored as the
    /// middle of its step, <c>(index + 0.5) / options</c>, or with no options the value itself.
    /// </remarks>
    public void InitData(Igi2LevelRun level)
    {
        if (Igi2MenuData.TryRun(level, ref _numOptions, SetNumOptions, this, out double options))
        {
            _options = (int)LevelExpression.Truncate(options);
        }

        if (Igi2MenuData.TryRun(level, ref _getData, GetDataScript, this, out double data))
        {
            if (_options != 0)
            {
                _intValue = (int)LevelExpression.Truncate(data);
                Value = (_intValue + 0.5) / _options;
            }
            else
            {
                Value = data;
            }
        }

        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _setData, SetDataScript, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindReal(this, "vValue", () => Value, value => Value = value);
        variables.BindInt(this, "nValue", () => _intValue, value => _intValue = value);
    }
}

/// <summary>A list stepped through one item at a time, <c>ListBox</c> (listbox.c): the shared one, with the enabled expression I.G.I. 2 adds.</summary>
public sealed partial class Igi2ListBoxTask : ListBoxTask, IIgi2MenuData, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ListBox";

    /// <summary>The most items it holds (listbox.c: 128 of 256 characters).</summary>
    public const int Capacity = 128;

    private readonly List<Igi2MenuItem> _entries = [];
    private LevelExpression? _collect;
    private LevelExpression? _getData;
    private LevelExpression? _setData;

    /// <summary>Gets or sets the expression that says whether it can be stepped.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives its collect script fills it through.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    IReadOnlyList<Igi2MenuItem> IIgi2MenuItemList.Items => _entries;

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item)
    {
        if (_entries.Count < Capacity && !_entries.Exists(i => i.Id == item.Id))
        {
            _entries.Add(item);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>ListBox_InitDataHandler</c>: emptied, filled by its collect script, then the get script names the active item --
    /// which stays as it was when the list does not hold the id named.
    /// </remarks>
    public void InitData(Igi2LevelRun level)
    {
        _entries.Clear();
        Menu?.Collect(this, () => Igi2MenuData.TryRun(level, ref _collect, CollectItemsScript, this, out _));
        if (Igi2MenuData.TryRun(level, ref _getData, GetDataScript, this, out double id))
        {
            int wanted = (int)LevelExpression.Truncate(id);
            if (_entries.Exists(i => i.Id == wanted))
            {
                ActiveId = wanted;
            }
        }

        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _setData, SetDataScript, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActiveID", () => ActiveId, value => ActiveId = value);
    }
}

/// <summary>A yes-or-no choice, <c>ToggleBox</c> (togglebox.c): the shared one, with the modify script and enabled expression I.G.I. 2 adds.</summary>
public sealed partial class Igi2ToggleBoxTask : ToggleBoxTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ToggleBox";

    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _modifyData;

    /// <summary>Gets or sets the script a flip runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it can be flipped.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string EnabledExpression { get; set; } = string.Empty;

    /// <summary>Flips it, and runs its modify script, as a click on its other half does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    public void Toggle(Igi2LevelRun level)
    {
        IsOn = !IsOn;
        Igi2MenuData.TryRun(level, ref _modifyData, ModifyDataScript, this, out _);
    }

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level)
    {
        if (Igi2MenuData.TryRun(level, ref _getData, GetDataScript, this, out double on))
        {
            IsOn = on != 0.0;
        }

        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _setData, SetDataScript, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isOn", () => IsOn ? 1 : 0, value => IsOn = value != 0);
    }
}

/// <summary>A line the player types into, <c>InputBox</c> (inputbox.c): the shared one, with the numeric switch I.G.I. 2 adds.</summary>
public sealed partial class Igi2InputBoxTask : InputBoxTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "InputBox";

    private LevelExpression? _getData;
    private LevelExpression? _setData;

    /// <summary>Gets or sets a value indicating whether it takes only digits and a point.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Numeric Input")]
    public bool NumericInput { get; set; }

    /// <summary>Gets or sets the menu whose natives its get script writes its text through.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <summary>Sets its text, cut to its length, as <c>InputBox_SetString</c> does.</summary>
    /// <param name="value">The text.</param>
    public void SetText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Data = value.Length > StringLength && StringLength > 0 ? value[..StringLength] : value;
    }

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level)
    {
        Menu?.Collect(this, () => Igi2MenuData.TryRun(level, ref _getData, GetDataScript, this, out _));
        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _setData, SetDataScript, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindText(this, "zData", () => Data, SetText, Math.Max(StringLength, 1) + 1);
    }
}

/// <summary>A scrolling list, <c>ScrollListBox</c> (scrolllistbox.c): the shared one, with the focus script I.G.I. 2 adds.</summary>
public sealed partial class Igi2ScrollListBoxTask : ScrollListBoxTask, IIgi2MenuData, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ScrollListBox";

    private readonly List<Igi2MenuItem> _entries = [];
    private LevelExpression? _collect;
    private LevelExpression? _getData;
    private LevelExpression? _setData;
    private LevelExpression? _onSelect;
    private int _activeEntry = -1;
    private int _isUpdate;

    /// <summary>Initializes a new instance of the <see cref="Igi2ScrollListBoxTask"/> class, with no item active.</summary>
    public Igi2ScrollListBoxTask() => ActiveId = -1;

    /// <summary>Gets or sets the script that runs as the selected row changes.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Focus Item")]
    public string OnFocusItem { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives its collect script fills it through.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    IReadOnlyList<Igi2MenuItem> IIgi2MenuItemList.Items => _entries;

    /// <summary>Gets the items, in the order added.</summary>
    public IReadOnlyList<Igi2MenuItem> Entries => _entries;

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item)
    {
        if (!_entries.Exists(i => i.Id == item.Id))
        {
            _entries.Add(item);
        }
    }

    /// <summary>Selects an item by its id and runs its select script, as a double click on its row does.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="id">The item's id.</param>
    public void Select(Igi2LevelRun level, int id)
    {
        int at = _entries.FindIndex(i => i.Id == id);
        if (at < 0)
        {
            return;
        }

        (_activeEntry, ActiveId) = (at, id);
        Igi2MenuData.TryRun(level, ref _onSelect, OnSelectItem, this, out _);
    }

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level)
    {
        _entries.Clear();
        Menu?.Collect(this, () => Igi2MenuData.TryRun(level, ref _collect, CollectItemsScript, this, out _));
        if (Igi2MenuData.TryRun(level, ref _getData, GetDataScript, this, out double id))
        {
            int wanted = (int)LevelExpression.Truncate(id);
            int at = _entries.FindIndex(i => i.Id == wanted);
            if (at >= 0)
            {
                (_activeEntry, ActiveId) = (at, wanted);
            }
        }

        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level)
    {
        Igi2MenuData.TryRun(level, ref _setData, SetDataScript, this, out _);
        Igi2MenuData.ChangeChildren(this, level);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActiveID", () => ActiveId, value => ActiveId = value);
        variables.BindInt(this, "isUpdate", () => _isUpdate, value => _isUpdate = value);
        variables.BindInt(this, "nActiveEntry", () => _activeEntry, value => _activeEntry = value);
    }
}

/// <summary>A picture chosen from a set, <c>PictureBox</c> (picturebox.c): the shared one, with the tint and click I.G.I. 2 adds.</summary>
public sealed partial class Igi2PictureBoxTask : PictureBoxTask, IIgi2MenuData, IIgi2MenuItemList
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "PictureBox";

    private readonly List<Igi2MenuItem> _entries = [];
    private LevelExpression? _collect;
    private LevelExpression? _active;
    private LevelExpression? _rgba;

    /// <summary>Gets or sets the expression that tints it, through <c>PictureBox_SetRGBA</c>, every tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get RGBA")]
    public string RgbaScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script a click runs; with one it can be selected.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Click Script")]
    public string OnClickScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound a click makes.</summary>
    [ScriptParam(ScriptParamType.String32, "Click Sound Resource")]
    public string ClickSoundResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the menu whose natives its collect script fills it through.</summary>
    public Igi2InGameMenu? Menu { get; set; }

    /// <inheritdoc/>
    IReadOnlyList<Igi2MenuItem> IIgi2MenuItemList.Items => _entries;

    /// <summary>Gets the id of the picture it shows.</summary>
    public int ActivePicture { get; private set; }

    /// <summary>Gets or sets its tint, as <c>PictureBox_SetRGBA</c> leaves it.</summary>
    public (float R, float G, float B, float A) Colour { get; set; } = (1f, 1f, 1f, 1f);

    /// <inheritdoc/>
    public void Add(Igi2MenuItem item) => _entries.Add(item);

    /// <inheritdoc/>
    /// <remarks><c>PictureBox_InitDataHandler</c>: emptied, filled by its collect script; its active picture and its colour read.</remarks>
    public void InitData(Igi2LevelRun level)
    {
        _entries.Clear();
        Menu?.Collect(this, () =>
        {
            Igi2MenuData.TryRun(level, ref _collect, CollectItems, this, out _);
            if (Igi2MenuData.TryRun(level, ref _active, GetActivePicture, this, out double active))
            {
                ActivePicture = (int)LevelExpression.Truncate(active);
            }

            Igi2MenuData.TryRun(level, ref _rgba, RgbaScript, this, out _);
        });
        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);
}

/// <summary>A popup's window, <c>DialogWindow</c> (dialogwindow.c): the shared one, its keys swapped unless the level says not.</summary>
public sealed partial class Igi2DialogWindowTask : DialogWindowTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DialogWindow";

    /// <summary>Initializes a new instance of the <see cref="Igi2DialogWindowTask"/> class.</summary>
    /// <remarks>I.G.I. 2's create handler sets <c>SwapKeys</c> (dialogwindow.c, byte <c>0x61</c>); Project I.G.I.'s leaves it clear.</remarks>
    public Igi2DialogWindowTask() => SwapKeys = true;

    /// <inheritdoc/>
    public void InitData(Igi2LevelRun level) => Igi2MenuData.InitChildren(this, level);

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);
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

/// <summary>The controls screen's list of bindings, <c>ControlsMenu</c> (controlsmenu.c): the shared one, put back to the top as it takes its data in.</summary>
public sealed partial class Igi2ControlsMenuTask : ControlsMenuTask, IIgi2MenuData
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ControlsMenu";

    /// <inheritdoc/>
    /// <remarks><c>ControlsMenu_InitData</c>: back to the top, nothing to reset.</remarks>
    public void InitData(Igi2LevelRun level)
    {
        Offset = 0;
        IsReset = false;
        Igi2MenuData.InitChildren(this, level);
    }

    /// <inheritdoc/>
    public void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);
}
