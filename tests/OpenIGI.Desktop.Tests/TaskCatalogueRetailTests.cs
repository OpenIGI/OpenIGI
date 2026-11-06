using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The task catalogue against every retail mission: its reference pins point where retail data points.</summary>
public sealed class TaskCatalogueRetailTests
{
    // Five squad references I.G.I. 2's shipped levels get wrong: two name no task at all and two name a task of another
    // type, so the game finds no alarm, trigger or gun there. The editor reads them verbatim and warns about them.
    private static readonly string[] KnownRetailMistakes =
    [
        "LEVEL5 AISquad.AlarmControlID: 100 names no task",
        "LEVEL5 AISquad.AlarmTriggerID: 102 names no task",
        "level1 AISquad.AlarmTriggerID: 102 names no task",
        "level1 AISquad.StationaryGunID: 3815 is a DefineComputerLimit, not a StationaryGun",
        "level4 AISquad.AlarmControlID: 1 is a AIGraph, not a AlarmControl",
    ];

    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_references_name_tasks_of_their_type() => AssertReferences(new Igi1Runtime(), GameDataSource.Igi1);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_references_name_tasks_of_their_type() => AssertReferences(new Igi2Runtime(), GameDataSource.Igi2);

    private static void AssertReferences(IGameTitleRuntime title, GameDataSource source)
    {
        ScriptVocabulary vocabulary = title.CreateScriptVocabulary();
        SortedSet<string> problems = new(StringComparer.Ordinal);
        int references = 0;
        int logicTypes = 0;
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            TaskCatalogue catalogue = TaskCatalogue.Build(document.Declarations.Values, vocabulary);
            logicTypes = Math.Max(logicTypes, catalogue.Definitions.Values.Count(static d => d.IsLogic));
            Dictionary<int, string> types = document.EnumerateTasks().Where(static t => t.Id >= 0)
                .GroupBy(static t => t.Id).ToDictionary(static g => g.Key, static g => g.First().TypeName);

            foreach (MissionTask task in document.EnumerateTasks())
            {
                if (!catalogue.TryGet(task.TypeName, out TaskNodeDefinition? definition))
                {
                    continue;
                }

                foreach (MissionParameter parameter in task.Parameters)
                {
                    TaskPin? pin = definition.Inputs.FirstOrDefault(p => p.Name == parameter.Name);
                    if (pin is not { Type.Kind: ScriptTypeKind.TaskRef } || !parameter.TryGetNumbers(out double[] values))
                    {
                        continue;
                    }

                    foreach (int id in values.Select(static v => (int)v).Where(static v => v > 0))
                    {
                        references++;
                        string where = $"{Path.GetFileName(Path.GetDirectoryName(path))} {task.TypeName}.{parameter.Name}";
                        if (!types.TryGetValue(id, out string? target))
                        {
                            problems.Add($"{where}: {id} names no task");
                        }
                        else if (pin.Type.Family is { } wanted && target != wanted)
                        {
                            problems.Add($"{where}: {id} is a {target}, not a {wanted}");
                        }
                    }
                }
            }
        }

        problems.ExceptWith(KnownRetailMistakes);
        Assert.True(references > 100, $"only {references} references");
        Assert.True(logicTypes > 20, $"only {logicTypes} logic types in the richest mission");
        Assert.True(problems.Count == 0, $"{problems.Count} problems:\n" + string.Join('\n', problems.Take(80)));
    }
}
