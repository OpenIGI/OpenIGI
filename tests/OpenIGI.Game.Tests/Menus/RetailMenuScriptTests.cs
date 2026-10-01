using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Menus;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// Runs the retail menu scripts against the real VM, task system and menu task types.
/// </summary>
/// <remarks>
/// This is the check that matters for the menu task types: their parameter lists are only right if the
/// script's own <c>Task_DeclareParameters</c> blocks agree with them, and <c>Task_New</c> parses
/// positionally — one wrong entry shifts every field after it and swallows the following child tasks.
/// So the test compares against the script, not against the C# declarations.
/// </remarks>
public sealed class RetailMenuScriptTests
{
    private static readonly string[] MenuScripts = ["menusystem/mainmenu.qvm", "menusystem/ingamemenu.qvm"];

    [RequiresGameDataFact]
    public void TheMainMenuScriptRunsAndBuildsItsTaskTree()
    {
        Loaded menu = Load("menusystem/mainmenu.qvm");

        Assert.NotEmpty(menu.Tasks);

        // The script's root is a MenuManager; everything else hangs under it.
        GameTask root = Assert.IsType<MenuManagerTask>(menu.Tree.Root.Children.First());
        Assert.NotEmpty(root.Children);
    }

    [RequiresGameDataFact]
    public void EveryDeclaredMenuTypeIsOneWeRegistered()
    {
        foreach (string script in MenuScripts)
        {
            if (!File.Exists(GameData.Resolve(script)))
            {
                continue;
            }

            Loaded menu = Load(script);

            foreach (TaskType type in menu.Types.Types.Where(t => t.IsDeclared))
            {
                Assert.False(
                    type.IsDeclarationOnly,
                    $"{script}: '{type.Name}' is declared by the script but has no registered implementation.");
            }
        }
    }

    [RequiresGameDataFact]
    public void DeclaredParameterListsMatchTheRegisteredOnes()
    {
        // TaskTypeRegistry.Declare reconciles the script's list with the registered one, so a
        // mismatch would already have thrown inside Load. Asserting the counts here says *which*
        // type disagreed rather than just that something did.
        Loaded menu = Load("menusystem/mainmenu.qvm");

        (string Name, int Count)[] expected =
        [
            ("MenuManager", 3),
            ("MenuFrame", 9),
            ("MenuScreen", 8),
            ("MenuText", 5),
            ("MenuTextConditional", 7),
            ("SlideBar", 8),
            ("ListBox", 7),
            ("ToggleBox", 2),
            ("InputBox", 6),
            ("AsciiBox", 4),
            ("ScrollListBox", 7),
            ("PictureBox", 4),
            ("TextBox", 5),
            ("TypeWriterBox", 2),
            ("DialogWindow", 5),
        ];

        foreach ((string name, int count) in expected)
        {
            Assert.True(menu.Types.TryGet(name, out TaskType? type), $"'{name}' was never declared.");
            Assert.Equal(count, type!.Parameters.Count);
        }
    }

    [RequiresGameDataFact]
    public void EveryActionAndConditionInTheMenuParses()
    {
        // Every VarString parameter of every spawned task holds expression source. If the language
        // front-end cannot read all of it, the menu cannot be driven.
        Loaded menu = Load("menusystem/mainmenu.qvm");

        List<string> failures = [];
        int parsed = 0;

        // An unset VarString parameter is an empty string, which is not an expression -- nor, to the original, is
        // one holding only characters its lexer stops at (the menu carries a lone backtick).
        foreach (string source in menu.Tasks.SelectMany(CollectExpressions).Where(s => !ScriptExpressionParser.IsBlank(s)))
        {
            try
            {
                ScriptExpressionParser.Parse(source, Constants);
                parsed++;
            }
            catch (ScriptExpressionException ex)
            {
                failures.Add($"{source}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(parsed > 20, $"Expected the main menu to carry more than 20 expressions, found {parsed}.");
    }

    [RequiresGameDataFact]
    public void TheMainMenuEntriesAreTheOnesTheScriptDeclares()
    {
        Loaded menu = Load("menusystem/mainmenu.qvm");

        string[] actions = menu.Tasks
            .OfType<MenuTextTask>()
            .Select(t => t.OnClickScript)
            .Where(s => s.Length > 0)
            .ToArray();

        // The four entries the top-level screen leads to, plus the quit popup.
        Assert.Contains(actions, a => a.Contains("MenuManager_PushScreen(914)", StringComparison.Ordinal));
        Assert.Contains(actions, a => a.Contains("MenuManager_PushScreen(904)", StringComparison.Ordinal));
        Assert.Contains(actions, a => a.Contains("MenuManager_ActivatePopupScreen(934)", StringComparison.Ordinal));
    }

    [RequiresGameDataFact]
    public void MenuTextTasksCarryAFontAndAColour()
    {
        Loaded menu = Load("menusystem/mainmenu.qvm");

        MenuTextTask[] entries = [.. menu.Tasks.OfType<MenuTextTask>()];
        Assert.NotEmpty(entries);

        // A parameter list read at the wrong offset shows up here first: the font name would land in
        // the text slot, or the colour index would be a pointer-sized number.
        foreach (MenuTextTask entry in entries.Where(e => e.FontResource.Length > 0))
        {
            Assert.EndsWith(".fnt", entry.FontResource, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(entry.ColourIndex, 0, MenuTaskTypes.TextColours.Count - 1);
        }
    }

    private static int? Constants(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        _ => MenuTaskTypes.TextColours
            .Where(c => c.Name == name)
            .Select(c => (int?)c.Value)
            .FirstOrDefault(),
    };

    private static IEnumerable<string> CollectExpressions(GameTask task) => task switch
    {
        MenuScreenTask s => new[] { s.OnPressEscape, s.OnInitData, s.OnChangeData },
        MenuTextTask t => [t.OnClickScript],
        MenuTextConditionalTask c => [c.IsEnabledExpression, c.IsVisibleExpression, c.OnClickScript],
        SlideBarTask b => [b.GetDataScript, b.SetDataScript, b.ModifyDataScript, b.CarretSpeed, b.SetNumOptions],
        ListBoxTask l => [l.CollectItemsScript, l.GetDataScript, l.SetDataScript, l.ModifyDataScript],
        ToggleBoxTask g => [g.GetDataScript, g.SetDataScript],
        InputBoxTask i => [i.GetDataScript, i.SetDataScript, i.OnModifyScript],
        ScrollListBoxTask r => [r.CollectItemsScript, r.GetDataScript, r.SetDataScript, r.OnSelectItem],
        PictureBoxTask p => [p.CollectItems, p.GetActivePicture],
        TextBoxTask x => [x.CollectItems, x.GetActivePicture],
        _ => [],
    };

    private static Loaded Load(string relativePath)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();

        EngineTaskTypes.Register(types);
        MenuTaskTypes.RegisterAll(types);
        MenuTaskTypes.RegisterConstants(natives);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(GameData.Resolve(relativePath)));
        QvmInterpreter interpreter = new(program, natives);

        QvmError error = interpreter.Run();
        Assert.True(
            error == QvmError.None,
            $"{relativePath} faulted with {error}: {interpreter.ErrorMessage} ({interpreter.ErrorIdentifier})");

        List<GameTask> visited = [];
        Collect(tree.Root, visited);
        return new Loaded(tree, types, visited);
    }

    private static void Collect(GameTask task, List<GameTask> into)
    {
        foreach (GameTask child in task.Children)
        {
            into.Add(child);
            Collect(child, into);
        }
    }

    private sealed class Loaded(TaskTree tree, TaskTypeRegistry types, IReadOnlyList<GameTask> tasks)
    {
        public TaskTree Tree => tree;

        public TaskTypeRegistry Types => types;

        public IReadOnlyList<GameTask> Tasks => tasks;
    }
}
