using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Terrain;

/// <summary>
/// Adding a sculptable area as one undoable step: the grid laid over the ground, the task that saves it with the level, and
/// the task type's declaration when the mission did not declare it yet.
/// </summary>
/// <param name="document">The mission.</param>
/// <param name="terrain">The ground, which has already laid the grid when the command is made.</param>
/// <param name="area">The grid.</param>
/// <param name="task">The task that saves it.</param>
/// <param name="location">Where the task goes.</param>
/// <param name="declaration">The task type's declaration to add with it, or <see langword="null"/> when the mission has it.</param>
internal sealed class AddTerrainAreaCommand(
    MissionDocument document,
    IEditableTerrain terrain,
    TerrainArea area,
    MissionTask task,
    TaskLocation location,
    TaskDeclaration? declaration) : IEditorCommand
{
    private bool _laid = true;

    /// <summary>Gets the task that saves the grid.</summary>
    public MissionTask Task => task;

    /// <inheritdoc/>
    public string Name => "Add Sculptable Area";

    /// <summary>Makes the step for a grid the ground has just laid: its task, placed beside the mission's other grids.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="terrain">The ground that laid it.</param>
    /// <param name="area">The grid.</param>
    /// <returns>The step, ready to be executed.</returns>
    public static AddTerrainAreaCommand For(MissionDocument document, IEditableTerrain terrain, TerrainArea area)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(area);
        TaskDeclaration declaration = new(area.TaskType, [.. area.Declaration.Select(static p => new TaskParameterDeclaration(p.Key, p.Value))]);
        bool declared = document.Declarations.ContainsKey(area.TaskType);
        return new AddTerrainAreaCommand(
            document,
            terrain,
            area,
            NewTask(document, area, declared ? document.Declarations[area.TaskType] : declaration),
            Location(document, area.TaskType),
            declared ? null : declaration);
    }

    // The task with the declared parameters, the grid's values and its position.
    private static MissionTask NewTask(MissionDocument document, TerrainArea area, TaskDeclaration declaration)
    {
        MissionTask task = new(area.TaskType, string.Empty);
        bool declared = document.Declarations.ContainsKey(area.TaskType);
        document.Declarations[area.TaskType] = declaration;
        document.ApplyDeclaration(task);
        if (!declared)
        {
            document.Declarations.Remove(area.TaskType);
        }

        foreach ((string name, int value) in area.Values)
        {
            if (task.FindParameter(name) is { } parameter)
            {
                parameter.Arguments.Clear();
                parameter.Arguments.Add(new QscIntLiteral(value));
            }
        }

        TaskPresentation.Position(task)?.SetNumbers(
            [WorldUnits.FromMetres(area.PositionMetres.X), WorldUnits.FromMetres(area.PositionMetres.Y), WorldUnits.FromMetres(area.PositionMetres.Z)]);
        return task;
    }

    // Beside the mission's other grids, else in its heightmap or terrain container, else at the end of the script.
    private static TaskLocation Location(MissionDocument document, string type)
    {
        MissionTask? parent = document.EnumerateTasks().FirstOrDefault(t => t.TypeName == type)?.Parent
            ?? FindContainer(document, "heightmap", "height map")
            ?? FindContainer(document, "terrainmodifiers", "terrain modifiers");
        return parent is null ? new TaskLocation(null, document.Statements.Count) : new TaskLocation(parent, parent.Children.Count);
    }

    private static MissionTask? FindContainer(MissionDocument document, params string[] names) =>
        document.EnumerateTasks().FirstOrDefault(t => t.TypeName == "Container"
            && names.Any(n => t.Label.Contains(n, StringComparison.OrdinalIgnoreCase)));

    /// <inheritdoc/>
    public void Apply()
    {
        if (!_laid)
        {
            terrain.AddArea(area.PositionMetres, area.Id);
            _laid = true;
        }

        if (declaration is not null)
        {
            document.Declare(declaration);
        }

        document.Insert(task, location);
    }

    /// <inheritdoc/>
    public void Undo()
    {
        document.Remove(task);
        if (declaration is not null)
        {
            document.Undeclare(declaration.TypeName);
        }

        terrain.RemoveArea(area);
        _laid = false;
    }
}
