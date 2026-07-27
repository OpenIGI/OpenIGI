using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Menus;

/// <summary>The manager that owns the menu screen stack.</summary>
/// <remarks>
/// Registered by the original at <c>0x4170A0</c> with an instance size of 10392. Its parameters are
/// declared by the menu script, not by the engine — see the original.
/// </remarks>
public partial class MenuManagerTask : MenuWindowTask
{
    /// <summary>Gets or sets the screen the manager starts on.</summary>
    [ScriptParam(ScriptParamType.Int32, "Current menuscreen")]
    public int CurrentMenuScreen { get; set; }

    /// <summary>Gets or sets the archive the menu's art and text come from.</summary>
    [ScriptParam(ScriptParamType.String256, "Resource path")]
    public string ResourcePath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the manager owns the display.</summary>
    [ScriptParam(ScriptParamType.Bool8, "isOwnDisplay")]
    public bool IsOwnDisplay { get; set; }
}

/// <summary>A rectangular group of menu items, laid out by its own spacing rules.</summary>
public partial class MenuFrameTask : MenuWindowTask
{
    /// <summary>Gets or sets the sprite drawn as the frame's border.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string SpriteResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the left edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "X-Pos")]
    public int X { get => LayoutX; set => LayoutX = value; }

    /// <summary>Gets or sets the top edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Y-Pos")]
    public int Y { get => LayoutY; set => LayoutY = value; }

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }

    /// <summary>Gets or sets how the frame aligns its children.</summary>
    [ScriptParam(ScriptParamType.Int32, "LineUpMode")]
    public int LineUpMode { get => (int)LineUp; set => LineUp = (MenuLineUpMode)value; }

    /// <summary>Gets or sets the horizontal gap between children in pixels.</summary>
    [ScriptParam(ScriptParamType.Int16, "X-Spacing")]
    public short SpacingX { get => (short)LayoutSpacingX; set => LayoutSpacingX = value; }

    /// <summary>Gets or sets the vertical gap between children in pixels.</summary>
    [ScriptParam(ScriptParamType.Int16, "Y-Spacing")]
    public short SpacingY { get => (short)LayoutSpacingY; set => LayoutSpacingY = value; }

    /// <summary>Gets or sets a value indicating whether the frame itself can take the selection.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is Selectable")]
    public bool IsSelectable { get; set; }
}

/// <summary>One full-screen menu page.</summary>
/// <remarks>Registered at <c>0x421750</c> with an instance size of 420.</remarks>
public partial class MenuScreenTask : MenuWindowTask
{
    /// <summary>Gets or sets the background picture.</summary>
    [ScriptParam(ScriptParamType.String32, "Background")]
    public string Background { get; set; } = string.Empty;

    /// <summary>Gets or sets the frame's left edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame x0")]
    public int FrameX0 { get; set; }

    /// <summary>Gets or sets the frame's top edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame y0")]
    public int FrameY0 { get; set; }

    /// <summary>Gets or sets the frame's right edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame x1")]
    public int FrameX1 { get; set; }

    /// <summary>Gets or sets the frame's bottom edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Frame y1")]
    public int FrameY1 { get; set; }

    /// <summary>Gets or sets the expression run when the user presses escape.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Press Escape")]
    public string OnPressEscape { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run when the screen is first shown.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Init Data")]
    public string OnInitData { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run when the screen's data changes.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Change Data")]
    public string OnChangeData { get; set; } = string.Empty;
}

/// <summary>A selectable line of text that runs an expression when clicked.</summary>
/// <remarks>Registered at <c>0x420CC0</c> with an instance size of 368.</remarks>
public partial class MenuTextTask : MenuWindowTask
{
    /// <summary>Initializes a new instance of the <see cref="MenuTextTask"/> class.</summary>
    /// <remarks>
    /// <c>MenuText</c>'s constructor (<c>0x420E90</c>) starts the item at (300, 230) and sets both
    /// centre flags — which is why a column of entries comes out centred in its frame without the
    /// script saying so anywhere.
    /// </remarks>
    public MenuTextTask()
    {
        LayoutX = 300;
        LayoutY = 230;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>Gets or sets the key naming the localised string to draw.</summary>
    [ScriptParam(ScriptParamType.String32, "Text Resource")]
    public string TextResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the font to draw with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the colour index, one of the <c>TEXT_*</c> constants.</summary>
    [ScriptParam(ScriptParamType.Int32, "ColourIndex")]
    public int ColourIndex { get; set; }

    /// <summary>Gets or sets the expression run when the entry is chosen.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Click Script")]
    public string OnClickScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound played when the entry is chosen.</summary>
    [ScriptParam(ScriptParamType.String32, "Click Sound Resource")]
    public string ClickSoundResource { get; set; } = string.Empty;
}

/// <summary>A <see cref="MenuTextTask"/> whose enabled and visible states are expressions.</summary>
public partial class MenuTextConditionalTask : MenuWindowTask
{
    /// <summary>Gets or sets the key naming the localised string to draw.</summary>
    [ScriptParam(ScriptParamType.String32, "Text Resource")]
    public string TextResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the font to draw with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the colour index, one of the <c>TEXT_*</c> constants.</summary>
    [ScriptParam(ScriptParamType.Int32, "ColourIndex")]
    public int ColourIndex { get; set; }

    /// <summary>Gets or sets the expression deciding whether the entry can be chosen.</summary>
    [ScriptParam(ScriptParamType.VarString, "isEnabled Expression")]
    public string IsEnabledExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression deciding whether the entry is drawn at all.</summary>
    [ScriptParam(ScriptParamType.VarString, "isVisible Expression")]
    public string IsVisibleExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run when the entry is chosen.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Click Script")]
    public string OnClickScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound played when the entry is chosen.</summary>
    [ScriptParam(ScriptParamType.String32, "Click Sound Resource")]
    public string ClickSoundResource { get; set; } = string.Empty;
}

/// <summary>A slider bound to a value through get/set expressions.</summary>
public partial class SlideBarTask : MenuWindowTask
{
    /// <summary>Gets or sets the sprite drawn as the slider.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string SpriteResource { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the slider runs horizontally.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Horizontal")]
    public bool Horizontal { get; set; }

    /// <summary>Gets or sets the slider's length in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Size")]
    public int Size { get; set; }

    /// <summary>Gets or sets the expression that reads the bound value.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that writes the bound value.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run after the value changes.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression giving the carret's travel speed.</summary>
    [ScriptParam(ScriptParamType.VarString, "Carret Speed")]
    public string CarretSpeed { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression giving the number of discrete options.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Num Options")]
    public string SetNumOptions { get; set; } = string.Empty;
}

/// <summary>A drop-down list bound through get/set expressions.</summary>
public partial class ListBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the sprite drawn as the box.</summary>
    [ScriptParam(ScriptParamType.String32, "Sprite Resource")]
    public string SpriteResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the font to draw entries with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the expression that fills the list.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items Script")]
    public string CollectItemsScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that reads the selected index.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that writes the selected index.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run after the selection changes.</summary>
    [ScriptParam(ScriptParamType.VarString, "Modify Data Script")]
    public string ModifyDataScript { get; set; } = string.Empty;
}

/// <summary>An on/off switch bound through get/set expressions.</summary>
public partial class ToggleBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the expression that reads the current state.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that writes the new state.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;
}

/// <summary>A single-line text field bound through get/set expressions.</summary>
public partial class InputBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the field's width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the largest number of characters the field accepts.</summary>
    [ScriptParam(ScriptParamType.Int32, "String Length")]
    public int StringLength { get; set; }

    /// <summary>Gets or sets the expression that reads the current text.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that writes the new text.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression run on each edit.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Modify Script")]
    public string OnModifyScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether typed characters are masked.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Hide Input")]
    public bool HideInput { get; set; }
}

/// <summary>A scrolling view of a plain text file.</summary>
public partial class AsciiBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the font to draw with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the text file to display.</summary>
    [ScriptParam(ScriptParamType.String32, "Text File")]
    public string TextFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }
}

/// <summary>A scrolling list whose contents come from an expression.</summary>
public partial class ScrollListBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the font to draw entries with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that fills the list.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items Script")]
    public string CollectItemsScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that reads the selected index.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Data Script")]
    public string GetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that writes the selected index.</summary>
    [ScriptParam(ScriptParamType.VarString, "Set Data Script")]
    public string SetDataScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }

    /// <summary>Gets or sets the expression run when an entry is chosen.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Select Item")]
    public string OnSelectItem { get; set; } = string.Empty;
}

/// <summary>A picture chosen from a collected set by an expression.</summary>
public partial class PictureBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }

    /// <summary>Gets or sets the expression that fills the picture set.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items")]
    public string CollectItems { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that picks the picture to show.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Active Picture")]
    public string GetActivePicture { get; set; } = string.Empty;

}

/// <summary>A block of text chosen from a collected set by an expression.</summary>
/// <remarks>
/// Its last two parameters are named <c>Collect Items</c> and <c>Get Active Picture</c> in the retail
/// script, the same names <see cref="PictureBoxTask"/> uses — the type was evidently copied from it and
/// the names were never changed. Keeping them is required: parameters bind by position and the script
/// declares them under these names.
/// </remarks>
public partial class TextBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the font to draw with.</summary>
    [ScriptParam(ScriptParamType.String32, "Font Resource")]
    public string FontResource { get; set; } = string.Empty;

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }

    /// <summary>Gets or sets the expression that fills the text set.</summary>
    [ScriptParam(ScriptParamType.VarString, "Collect Items")]
    public string CollectItems { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that picks the entry to show.</summary>
    [ScriptParam(ScriptParamType.VarString, "Get Active Picture")]
    public string GetActivePicture { get; set; } = string.Empty;
}

/// <summary>A box that reveals its text one character at a time.</summary>
public partial class TypeWriterBoxTask : MenuWindowTask
{
    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }
}

/// <summary>The controls-configuration screen's key-binding list.</summary>
/// <remarks>
/// The one menu type <c>mainmenu.qvm</c> spawns without a <c>Task_DeclareParameters</c> block: it
/// takes no parameters, so the editor emits nothing for it and the engine is expected to know it —
/// the same arrangement as the structural types. It exports <c>nOffset</c>, which the controls screen
/// reads back through <c>ControlsMenu_ConvertFloatFromOffset(ControlsMenu_954.nOffset)</c>.
/// </remarks>
public partial class ControlsMenuTask : MenuWindowTask
{
    /// <summary>Gets or sets the list's scroll offset, exported to script as <c>nOffset</c>.</summary>
    public int Offset { get; set; }
}

/// <summary>A modal popup window.</summary>
/// <remarks>
/// <c>SwapKeys</c> is a parameter of this type, not a task type of its own — it appears in the script's
/// string table next to the type names, which is easy to misread.
/// </remarks>
public partial class DialogWindowTask : MenuWindowTask
{
    /// <summary>Gets or sets the left edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "X-Pos")]
    public int X { get => LayoutX; set => LayoutX = value; }

    /// <summary>Gets or sets the top edge in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Y-Pos")]
    public int Y { get => LayoutY; set => LayoutY = value; }

    /// <summary>Gets or sets the width in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Width")]
    public int Width { get => LayoutWidth; set => LayoutWidth = value; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [ScriptParam(ScriptParamType.Int32, "Height")]
    public int Height { get => LayoutHeight; set => LayoutHeight = value; }

    /// <summary>Gets or sets a value indicating whether the accept and cancel keys are swapped.</summary>
    [ScriptParam(ScriptParamType.Bool8, "SwapKeys")]
    public bool SwapKeys { get; set; }
}
