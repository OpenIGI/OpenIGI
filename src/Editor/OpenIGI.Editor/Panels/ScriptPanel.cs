using OpenIGI.Engine.Level;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Any of the open mission's scripts as QSC source: read it, change it, compile it into the project. A compile that
/// fails marks the line and says why; one that succeeds is what the game runs from the next load.
/// </summary>
internal sealed class ScriptPanel : EditorElement
{
    private const string LocalDevice = "LOCAL:";

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Dropdown _scripts;
    private readonly Button _compile;
    private readonly CodeEditor _code;
    private readonly List<string> _paths = [];
    private string? _listedFor;
    private string? _openPath;
    private int _openVersion = QvmProgram.IgiVersion;
    private string _message = string.Empty;
    private bool _messageIsError;

    public ScriptPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _scripts = new Dropdown(theme) { Placeholder = "No mission open" };
        _scripts.SelectionChanged += index => Open(_paths[index]);
        _compile = new Button(theme, "Compile and Save", KitIcon.Save) { Style = ButtonStyle.Primary, IsEnabled = false };
        _compile.Clicked += Compile;
        _code = new CodeEditor(theme) { Highlight = line => QscHighlighter.Line(theme, line) };
        _code.TextChanged += () => _compile.IsEnabled = _openPath is not null;
        AddChild(_scripts);
        AddChild(_compile);
        AddChild(_code);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(6f);
        _compile.Measure(context, new UiVec2(float.PositiveInfinity, Theme.ControlHeight));
        float compileWidth = _compile.DesiredSize.X;
        UiRect bar = new(contentRect.X + pad, contentRect.Y + pad, contentRect.Width - (pad * 2f), Theme.ControlHeight);
        UiRect list = new(bar.X, bar.Y, MathF.Min(Theme.Px(320f), bar.Width - compileWidth - pad), bar.Height);
        _scripts.Measure(context, list.Size);
        _scripts.Arrange(list);
        _compile.Arrange(new UiRect(list.Right + pad, bar.Y, compileWidth, bar.Height));
        UiRect code = UiRect.FromEdges(contentRect.X, bar.Bottom + pad, contentRect.Right, contentRect.Bottom - Theme.StatusBarHeight);
        _code.Measure(context, code.Size);
        _code.Arrange(code);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        // The list follows the open mission.
        string? directory = _workspace.Mission?.MissionDirectory;
        if (directory == _listedFor)
        {
            return;
        }

        _listedFor = directory;
        _paths.Clear();
        _openPath = null;
        _code.Text = string.Empty;
        _message = string.Empty;
        if (directory is not null)
        {
            _paths.AddRange(ListScripts(directory));
        }

        _scripts.Options = [.. _paths.Select(static path => path.Contains("/ai/", StringComparison.OrdinalIgnoreCase) ? "ai/" + Path.GetFileName(path) : Path.GetFileName(path))];
        _scripts.SelectedIndex = -1;
        int mission = _paths.FindIndex(static p => p.EndsWith("/mission.qvm", StringComparison.OrdinalIgnoreCase));
        if (mission >= 0)
        {
            _scripts.SelectedIndex = mission;
            Open(_paths[mission]);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, Theme.Panel);

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        UiRect strip = new(Bounds.X, Bounds.Bottom - Theme.StatusBarHeight, Bounds.Width, Theme.StatusBarHeight);
        painter.FillRect(strip, Theme.Chrome);
        string where = _openPath is null ? string.Empty : FormattableString.Invariant($"Ln {_code.CaretLine + 1}, Col {_code.CaretColumn + 1}");
        float width = Theme.SmallFont.MeasureWidth(where, Theme.SmallTextSize);
        painter.DrawText(Theme.SmallFont, where, new UiVec2(strip.Right - width - Theme.Padding, MathF.Round(strip.Y + ((strip.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize, Theme.TextMuted);
        EditorDraw.Label(painter, Theme, _message, new UiRect(strip.X + Theme.Padding, strip.Y, strip.Width - width - (Theme.Padding * 3f), strip.Height), _messageIsError ? Theme.Error : Theme.TextMuted);
    }

    private List<string> ListScripts(string directory)
    {
        string root = LocalDevice + directory.TrimEnd('/', '\\');
        List<string> found = [];
        foreach (string folder in new[] { root, root + "/ai" })
        {
            try
            {
                found.AddRange(_host.Files.EnumerateFiles(folder, "*.qvm").Select(static p => p.Replace('\\', '/')));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return [.. found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static p => p.Contains("/ai/", StringComparison.OrdinalIgnoreCase)).ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)];
    }

    private void Open(string path)
    {
        _openPath = null;
        _compile.IsEnabled = false;
        if (!_host.TryReadFile(path, out ReadOnlyMemory<byte> image))
        {
            Say($"{path} could not be read.", error: true);
            return;
        }

        try
        {
            QscScript script = QscDecompiler.Decompile(image);
            _openVersion = script.Version;
            _code.Text = QscPrinter.Print(script);
            _openPath = path;
            Say(IsObjects(path)
                ? "The object script: the Outliner and Inspector edit it too; compiling here replaces their document."
                : $"{_code.LineCount} lines.", error: false);
        }
        catch (Exception error) when (error is QvmFormatException or QscDecompileException)
        {
            _code.Text = string.Empty;
            Say($"Not a script the decompiler can read: {error.Message}", error: true);
        }
    }

    private void Compile()
    {
        if (_openPath is not { } path)
        {
            return;
        }

        if (IsObjects(path) && _workspace.History.IsDirty)
        {
            Say("Save or undo the object edits first: compiling the object script replaces the document they are in.", error: true);
            return;
        }

        byte[] image;
        try
        {
            image = QscCompiler.Compile(QscParser.ParseScript(_code.Text, _openVersion));
        }
        catch (QscParseException error)
        {
            _code.ErrorLine = error.Position.Line - 1;
            _code.GoTo(error.Position.Line - 1, error.Position.Column - 1);
            Say($"Line {error.Position.Line}: {error.Problem}.", error: true);
            return;
        }

        try
        {
            _workspace.WriteToProject(path[LocalDevice.Length..], image);
        }
        catch (IOException error)
        {
            Say($"Could not save: {error.Message}", error: true);
            return;
        }

        _compile.IsEnabled = false;
        if (IsObjects(path))
        {
            _workspace.Reload();
        }

        Say($"Compiled and saved into {_workspace.Project.Name}. Save and Reload (Ctrl+R) runs it.", error: false);
    }

    private static bool IsObjects(string path) => path.EndsWith("/" + LevelLoader.ObjectsScriptName, StringComparison.OrdinalIgnoreCase);

    private void Say(string message, bool error)
    {
        _message = message;
        _messageIsError = error;
    }
}
