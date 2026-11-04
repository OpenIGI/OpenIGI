using System.Globalization;
using OpenIGI.Editor.Core.Configs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A config script (a weapon, an ammunition type) as a form: each value named and grouped as the game's loader reads it,
/// edited with the field that suits it, saved into the project as the compiled script the game loads, and a click away from
/// the script itself.
/// </summary>
internal sealed class ConfigPanel : EditorElement
{
    // The game's text device, which a project path is relative to.
    private const string LocalDevice = "LOCAL:";

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Action<string> _openScript;
    private readonly PropertyGrid _grid;
    private readonly Button _save;
    private readonly Button _revert;
    private readonly Button _asScript;
    private ConfigDocument? _document;
    private string? _path;
    private string _message = string.Empty;
    private bool _messageIsError;

    public ConfigPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, Action<string> openScript)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _openScript = openScript;
        _grid = new PropertyGrid(theme) { EmptyText = "Double-click a weapon or ammo script in Content to edit it here." };
        _save = new Button(theme, "Save", KitIcon.Save) { Style = ButtonStyle.Primary, IsEnabled = false, Tooltip = "Compile and save into the project (Ctrl+S)" };
        _save.Clicked += Save;
        _revert = new Button(theme, "Revert", KitIcon.History) { IsEnabled = false, Tooltip = "Put every value back as the file has it" };
        _revert.Clicked += () =>
        {
            _document?.RevertAll();
            Say("Every value put back.", error: false);
        };
        _asScript = new Button(theme, "Open as Script", KitIcon.Script) { IsEnabled = false, Tooltip = "Edit the script itself in the Script panel" };
        _asScript.Clicked += () =>
        {
            if (_path is { } path)
            {
                _openScript(path);
            }
        };
        foreach (UiElement child in (UiElement[])[_grid, _save, _revert, _asScript])
        {
            AddChild(child);
        }
    }

    /// <summary>Gets the name of the editor for the open file (<c>Weapon Editor</c>), or a general one.</summary>
    public string EditorName => _document is { } document ? $"{document.Title} Editor" : "Config Editor";

    /// <summary>Gets whether a file is open with unsaved changes.</summary>
    public bool IsDirty => _document?.IsDirty ?? false;

    /// <summary>Gets the name of the editor a config script opens in (<c>Weapon Editor</c>), or null for a script that is not one.</summary>
    /// <param name="bytes">The compiled script.</param>
    /// <returns>The editor's name.</returns>
    public static string? EditorFor(ReadOnlyMemory<byte> bytes) => ConfigDocument.TryRead(bytes) is { } document ? $"{document.Title} Editor" : null;

    /// <summary>Opens a config script by its game path.</summary>
    /// <param name="path">The path (<c>LOCAL:weapons/AK47/weapon.qvm</c>).</param>
    /// <returns>Whether it is a config the form can edit.</returns>
    public bool Open(string path)
    {
        if (!_host.TryReadFile(path, out ReadOnlyMemory<byte> bytes) || ConfigDocument.TryRead(bytes) is not { } document)
        {
            return false;
        }

        _document = document;
        _path = path;
        Build();
        Say($"{Path.GetFileName(Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar)))}/{Path.GetFileName(path)}: changes are saved into the project; the game's own file is left as it is.", error: false);
        return true;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        bool dirty = IsDirty;
        _save.IsEnabled = dirty;
        _revert.IsEnabled = dirty;
        _asScript.IsEnabled = _path is not null;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(8f);
        float bar = Theme.ControlHeight + (pad * 2f);
        float x = contentRect.Right - pad;
        foreach (Button button in (Button[])[_asScript, _revert, _save])
        {
            float width = Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(44f);
            x -= width;
            button.Place(context, new UiRect(x, contentRect.Y + pad, width, Theme.ControlHeight));
            x -= Theme.Px(4f);
        }

        float footer = Theme.RowHeight + Theme.Px(4f);
        _grid.Place(context, UiRect.FromEdges(contentRect.X, contentRect.Y + bar, contentRect.Right, contentRect.Bottom - footer));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float pad = Theme.Px(8f);
        string title = _document is { } document && _path is { } path
            ? $"{document.Title}  •  {Path.GetFileName(Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar)))}{(IsDirty ? "  •  modified" : string.Empty)}"
            : "Config Editor";
        EditorDraw.Label(painter, Theme, title, new UiRect(Bounds.X + pad, Bounds.Y + pad, _save.Bounds.X - Bounds.X - (pad * 2f), Theme.ControlHeight), Theme.Text, strong: true);
        painter.FillRect(new UiRect(Bounds.X, _grid.Bounds.Y - Theme.Hairline, Bounds.Width, Theme.Hairline), Theme.Border);
        EditorDraw.Label(painter, Theme, _message, new UiRect(Bounds.X + pad, Bounds.Bottom - Theme.RowHeight - Theme.Px(2f), Bounds.Width - (pad * 2f), Theme.RowHeight), _messageIsError ? Theme.Error : Theme.TextMuted);
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(in KeyEvent e)
    {
        if (e.Control && e.Key == OpenIGI.Input.KeyCode.S && IsDirty)
        {
            Save();
            return true;
        }

        return base.OnKeyDown(e);
    }

    // The form: each call's values under their sections, in the order the schema lists the sections.
    private void Build()
    {
        _grid.Clear();
        if (_document is not { } document)
        {
            return;
        }

        for (int c = 0; c < document.Calls.Count; c++)
        {
            ConfigCall call = document.Calls[c];
            int index = c;
            List<(ConfigField Field, int Value)> fields = [.. call.Values.Select((_, i) => (ConfigSchemas.FieldAt(call.Schema, i), i))];
            foreach (IGrouping<string, (ConfigField Field, int Value)> section in fields.GroupBy(static f => f.Field.Section))
            {
                _grid.Section(document.Calls.Count > 1 ? $"{call.Name}  •  {section.Key}" : section.Key);
                foreach ((ConfigField field, int value) in section)
                {
                    AddField(document, index, value, field);
                }
            }
        }

        _grid.RevertAll = () =>
        {
            document.RevertAll();
        };
    }

    // One value's row, with the field that suits it; a value written as something other than its kind (a constant where a
    // number goes) is edited as written.
    private void AddField(ConfigDocument document, int call, int index, ConfigField field)
    {
        QscExpression current = document.Calls[call].Values[index];
        string? tip = field.Tip;
        switch (field.Kind)
        {
            case ConfigFieldKind.Integer when current is QscIntLiteral or QscUnary { Operand: QscIntLiteral }:
                NumberField whole = _grid.Number(field.Name, () => Number(document, call, index), (v, final) => SetIfFinal(document, call, index, ScriptValuesInteger(v), final), tip);
                whole.Decimals = 0;
                whole.Step = 1;
                break;
            case ConfigFieldKind.Number when current is QscRealLiteral or QscIntLiteral or QscUnary:
                NumberField number = _grid.Number(field.Name, () => Number(document, call, index), (v, final) => SetIfFinal(document, call, index, new QscRealLiteral((float)v), final), tip);
                number.Decimals = 3;
                number.Step = 0.01;
                break;
            case ConfigFieldKind.Text or ConfigFieldKind.TextKey when current is QscStringLiteral && ValuesOf(field) is { } kind:
                // A name of something the game has: its own offered in a dropdown, anything else still taken as typed.
                _grid.Combo(
                    field.Name,
                    () => ((QscStringLiteral)document.Calls[call].Values[index]).Value,
                    text => document.Set(call, index, new QscStringLiteral(text)),
                    () => _workspace.Values.For(kind),
                    tip);
                break;
            case ConfigFieldKind.Text or ConfigFieldKind.TextKey when current is QscStringLiteral:
                _grid.Text(field.Name, () => ((QscStringLiteral)document.Calls[call].Values[index]).Value, text => document.Set(call, index, new QscStringLiteral(text)), tip);
                break;
            case ConfigFieldKind.Choice when current is QscSymbol symbol && field.Choices.Contains(symbol.Name):
                _grid.Choice(
                    field.Name,
                    [.. field.Choices.Select(Friendly)],
                    () => document.Calls[call].Values[index] is QscSymbol s ? IndexOf(field.Choices, s.Name) : -1,
                    choice => document.Set(call, index, new QscSymbol(field.Choices[choice])),
                    tip);
                break;
            default:
                void SetText(string text)
                {
                    if (!document.TrySetText(call, index, text, out string? problem))
                    {
                        Say($"{field.Name}: {problem}", error: true);
                    }
                }

                if (field.ConstantPrefix is { } prefix)
                {
                    // Usually one of the game's constants of a family (TASKTYPE_GUNSHOT): offered, and anything the script can write taken.
                    _grid.Combo(field.Name, () => document.Text(call, index), SetText, () => _workspace.Values.For(ValueKind.Constant, prefix), tip);
                }
                else
                {
                    _grid.Text(field.Name, () => document.Text(call, index), SetText, tip ?? "As the script writes it: a number, a constant, or a sum of them");
                }

                break;
        }

        _grid.Revertible(() => document.IsModified(call, index), () => document.Revert(call, index));
    }

    // What list a text field offers: a model's, a sound's, or (for a text key) the language's keys.
    private static ValueKind? ValuesOf(ConfigField field) => field switch
    {
        { Values: ConfigValues.Model } => ValueKind.Model,
        { Values: ConfigValues.Sound } => ValueKind.Sound,
        { Kind: ConfigFieldKind.TextKey } => ValueKind.BracketedTextKey,
        _ => null,
    };

    private static double Number(ConfigDocument document, int call, int index) =>
        ScriptValues.TryGetNumber(document.Calls[call].Values[index], out double value) ? value : 0.0;

    private static QscExpression ScriptValuesInteger(double value) => ScriptValues.Integer((int)Math.Round(value));

    private static void SetIfFinal(ConfigDocument document, int call, int index, QscExpression value, bool final)
    {
        if (final)
        {
            document.Set(call, index, value);
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == item)
            {
                return i;
            }
        }

        return -1;
    }

    // WEAPONTYPE_PROXIMITYMINE as "Proximity mine": the constant's last word, lower case.
    private static string Friendly(string constant)
    {
        int split = constant.IndexOf('_', StringComparison.Ordinal);
        string word = (split < 0 ? constant : constant[(split + 1)..]).Replace('_', ' ').ToLowerInvariant();
        word = word.Replace("proximitymine", "proximity mine", StringComparison.Ordinal);
        return word.Length > 0 ? char.ToUpperInvariant(word[0]) + word[1..] : constant;
    }

    private void Save()
    {
        if (_document is not { } document || _path is not { } path)
        {
            return;
        }

        try
        {
            _workspace.WriteToProject(path.StartsWith(LocalDevice, StringComparison.OrdinalIgnoreCase) ? path[LocalDevice.Length..] : path, document.Compile());
            document.MarkSaved();
            Say(string.Create(CultureInfo.InvariantCulture, $"Saved into the project. The game reads it when the level is loaded again (Ctrl+R)."), error: false);
        }
        catch (IOException error)
        {
            Say($"Could not save: {error.Message}", error: true);
        }
    }

    private void Say(string message, bool error)
    {
        _message = message;
        _messageIsError = error;
    }
}
