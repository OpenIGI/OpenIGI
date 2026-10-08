using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Walks a level's task tree.</summary>
internal static class Igi2TaskTree
{
    /// <summary>Every task under one, depth first, each before its own children: the order the level declares and creates them in.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Its descendants.</returns>
    public static IEnumerable<GameTask> Descendants(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;

            foreach (GameTask descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
