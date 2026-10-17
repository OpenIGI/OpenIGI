using OpenIGI.Presentation;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Reads the retail <c>ComputerHilight</c> records that annotate the overhead map.</summary>
internal sealed class ComputerHighlightCatalog : IComputerHighlightCatalogue
{
    private readonly LevelExpressionRuntime _expressions;
    private readonly List<Entry> _entries = [];

    public ComputerHighlightCatalog(GameTask root, ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);
        _expressions = new LevelExpressionRuntime(variables);

        foreach (GameTask task in root.Descendants())
        {
            if (task is not DeclaredTask declared
                || !string.Equals(declared.Type.Name, "ComputerHilight", StringComparison.Ordinal))
            {
                continue;
            }

            string taskIdText = String(declared, "TaskID");
            if (!int.TryParse(taskIdText, out int taskId)
                || !TaskGeometry.TryFindPosition(root, taskId, out Vector3d position))
            {
                continue;
            }

            _entries.Add(new Entry(
                declared,
                position,
                String(declared, "Title text resource"),
                FileName(String(declared, "Click to select sprite")),
                String(declared, "Hilight")));
        }
    }

    public IEnumerable<ComputerMapHighlight> Visible()
    {
        foreach (Entry entry in _entries)
        {
            if (_expressions.EvaluateCondition(entry.Condition, emptyValue: true, entry.Task))
            {
                yield return new ComputerMapHighlight(entry.Position, entry.TitleResource, entry.Sprite);
            }
        }
    }

    private static string String(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static string FileName(string resource)
    {
        int colon = resource.LastIndexOf(':');
        return colon >= 0 ? resource[(colon + 1)..] : resource;
    }

    private sealed record Entry(
        DeclaredTask Task,
        Vector3d Position,
        string TitleResource,
        string Sprite,
        string Condition);
}
