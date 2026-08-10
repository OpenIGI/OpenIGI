using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Player;

/// <summary>Where a level starts its player.</summary>
/// <param name="Position">The spawn position, in the level's own metres.</param>
/// <param name="Gamma">The heading the player starts facing, in radians.</param>
/// <param name="Model">The model the level gives the player.</param>
/// <param name="Team">The team the player belongs to.</param>
public readonly record struct PlayerSpawnPoint(Vector3d Position, float Gamma, string Model, int Team);

/// <summary>
/// Finds where a level starts its player.
/// </summary>
/// <remarks>
/// <para>
/// The original does not compute a spawn: <b>the level script places one</b>. Every mission's
/// <c>objects.qvm</c> declares a <c>HumanPlayer</c> task with <c>Position</c>, <c>Gamma</c>,
/// <c>Model</c> and <c>Team</c>, and builds it with <c>Task_New</c> like any other object. So finding
/// the spawn is finding that task in the loaded tree — no heuristic, and the same answer the game
/// itself uses.
/// </para>
/// <para>
/// A level may place more than one; the original takes the first, and so does this.
/// </para>
/// </remarks>
public static class PlayerSpawn
{
    /// <summary>Finds the player start a level declares.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="spawn">Receives the spawn when the level declares one.</param>
    /// <returns><see langword="false"/> when no <c>HumanPlayer</c> task was built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A level whose player task type was never registered has nothing to find, which is worth knowing:
    /// the tree loads perfectly well without it and simply has no player in it.
    /// </remarks>
    public static bool TryFind(GameTask root, out PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(root);

        HumanPlayerTask? task = FindFirst(root);
        if (task is null)
        {
            spawn = default;
            return false;
        }

        spawn = new PlayerSpawnPoint(task.SpawnPosition, task.SpawnGamma, task.PlayerModel, task.PlayerTeam);
        return true;
    }

    /// <summary>Finds the mission's gameplay player even while an intro container has it unregistered.</summary>
    /// <remarks>
    /// Retail gives the playable HumanPlayer script id zero. Cutscene containers can temporarily make
    /// that task dormant while the authored camera is running, but the desktop still needs its eventual
    /// spawn to build the player body before the hand-off.
    /// </remarks>
    public static bool TryFindGameplay(GameTask root, out PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(root);
        HumanPlayerTask? player = FindById(root, 0) ?? FindAny(root);
        if (player is null)
        {
            spawn = default;
            return false;
        }

        spawn = new PlayerSpawnPoint(player.SpawnPosition, player.SpawnGamma, player.PlayerModel, player.PlayerTeam);
        return true;
    }

    private static HumanPlayerTask? FindFirst(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            // Skip a cut-scene container the level is not running: the player sits under the gameplay
            // container (1495), whose condition is true, so this only prunes intro/outro subtrees.
            if (World.ConditionalContainerGate.IsDormant(child))
            {
                continue;
            }

            if (child is HumanPlayerTask player)
            {
                return player;
            }

            HumanPlayerTask? found = FindFirst(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static HumanPlayerTask? FindById(GameTask task, int id)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanPlayerTask player && player.Id == id)
            {
                return player;
            }
            if (FindById(child, id) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static HumanPlayerTask? FindAny(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanPlayerTask player)
            {
                return player;
            }
            if (FindAny(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
