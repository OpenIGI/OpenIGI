using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>
/// Reinforcements: a guard turned into one the game sends in when something happens, in the shape every shipped one has
/// -- a <c>GuardGenerator</c> holding the soldier, his guns and his mind and nothing else -- with his patrol paths moved
/// beside it, as level 14 keeps its reinforcements' paths, and when he comes left to a rule ("Send in the guards").
/// </summary>
/// <remarks>
/// <c>GuardGenerator_Run</c> (<c>0x487EE0</c>) copies its first child with everything under it each time it sends the
/// guard in (<c>GuardTemplate_Spawn</c>, <c>0x488040</c>), again after he dies up to its spawns; all 82 shipped generators
/// hold one soldier with only his guns and his mind.
/// </remarks>
internal sealed partial class EditorSession
{
    private const string GeneratorType = "GuardGenerator";
    private const string SendParameter = "Generate guards";
    private const string SpawnsParameter = "Max spawns";

    // The declaration every shipped level that has generators gives them.
    private static readonly TaskDeclaration GeneratorDeclaration = new(GeneratorType, [new(SendParameter, "VarString"), new(SpawnsParameter, "Int32")]);

    private void RegisterReinforcementActions(ActionTable actions) =>
        actions.Add(new EditorAction("ai.reinforcement", "Make Reinforcement", MakeReinforcements)
        {
            Category = "Tools",
            CanExecute = () => _host.Title.Id == "igi1" && _workspace.Selection.Items.Any(IsPlainSoldier),
        });

    // A soldier not already sent in by a generator.
    private static bool IsPlainSoldier(MissionTask task) =>
        PatrolPaths.IsSoldier(task) && task.Parent?.TypeName != GeneratorType;

    private void MakeReinforcements()
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        MissionTask[] soldiers = [.. _workspace.Selection.Items.Where(IsPlainSoldier)];
        if (soldiers.Length == 0)
        {
            return;
        }

        List<IEditorCommand> steps = [];
        if (!document.Declarations.ContainsKey(GeneratorType))
        {
            steps.Add(new DelegateCommand("Declare GuardGenerator", () => document.Declare(GeneratorDeclaration), () => document.Undeclare(GeneratorType)));
        }

        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int NextId() => Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).First(used.Add);
        List<MissionTask> generators = [];
        foreach (MissionTask soldier in soldiers)
        {
            string name = TaskPresentation.Name(soldier);
            MissionTask generator = new(GeneratorType, $"{name} reinforcement", NextId());
            TaskLocation at = document.Locate(soldier);

            // The generator takes the soldier's place, the soldier goes in it, and what is not his guns or his mind goes
            // into a container beside it.
            steps.Add(new DelegateCommand("Make generator", () => Prepare(document, generator), static () => { }));
            steps.Add(new InsertTaskCommand(document, generator, at));
            steps.Add(new MoveTaskCommand(document, soldier, new TaskLocation(generator, 0)));
            MissionTask[] aside = [.. soldier.Children.Where(static c => !c.TypeName.StartsWith("Gun", StringComparison.Ordinal) && c.TypeName != "HumanAI")];
            if (aside.Length > 0)
            {
                MissionTask beside = new(TaskCatalogue.ContainerType, $"{name} paths");
                steps.Add(new InsertTaskCommand(document, beside, at with { Index = at.Index + 1 }));
                for (int i = 0; i < aside.Length; i++)
                {
                    steps.Add(new MoveTaskCommand(document, aside[i], new TaskLocation(beside, i)));
                }
            }

            generators.Add(generator);
        }

        _workspace.Execute(new CompositeCommand(soldiers.Length == 1 ? "Make Reinforcement" : string.Create(CultureInfo.InvariantCulture, $"Make {soldiers.Length} Reinforcements"), steps));
        _workspace.Selection.Set(generators);
        Notify(soldiers.Length == 1
            ? "Made a reinforcement: the game sends him in when a rule says \"Send in the guards\" (Rules, Do). He is not in the level until then."
            : string.Create(CultureInfo.InvariantCulture, $"Made {soldiers.Length} reinforcements: each is sent in by a rule's \"Send in the guards\"."));
    }

    // A generator's parameters as the shipped ones start: sent in by nothing yet, once.
    private static void Prepare(MissionDocument document, MissionTask generator)
    {
        if (generator.Parameters.Count > 0)
        {
            return;
        }

        document.ApplyDeclaration(generator);
        if (generator.FindParameter(SendParameter) is { } send)
        {
            send.Arguments.Clear();
            send.Arguments.Add(new QscStringLiteral(string.Empty));
        }

        generator.FindParameter(SpawnsParameter)?.SetNumbers([1]);
    }
}
