using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Who an authored <c>AreaActivate</c> volume counts as the player, and the level script's transient-object cleanup.</summary>
public static class AreaOccupancy
{
    /// <summary>Chooses the engine-unit occupant used by retail AreaActivate volumes.</summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <param name="freeViewMetres">
    /// The diagnostic free camera in renderer metres. It is only a fallback when no player body exists;
    /// moving a debug camera must never satisfy gameplay gates such as Mission 4's extraction volume.
    /// </param>
    public static Vector3d? AreaActivationOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres = null)
    {
        if (cutsceneViewMetres is { } cutscene)
        {
            return cutscene * WorldScene.UnitsPerMetre;
        }

        if (playerUnits is { } player)
        {
            return player;
        }

        return freeViewMetres is { } free
            ? free * WorldScene.UnitsPerMetre
            : null;
    }

    /// <summary>
    /// Gets the player-body projection that preserves occupancy while a cutscene camera drives activation.
    /// </summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <remarks>
    /// Retail queries humans rather than cameras, so the stationary body never stops occupying its
    /// volume when a cinematic starts. Without this, Mission 4's latch-less outro gate
    /// (<c>AreaActivate_156.nActive</c>) drops as soon as the <c>CutScene_2222</c> camera leaves the
    /// extraction volume, aborting the cutscene and refiring it every other tick. The camera remains
    /// the primary projection (Mission 14 streams tunnel sections off it), while the body is an
    /// alternate projection of that same logical player rather than another occupant.
    /// </remarks>
    public static Vector3d? AreaActivationExtraOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres) =>
        cutsceneViewMetres is not null && playerUnits is { } player ? player : null;

    /// <summary>Feeds the player's projections and liveness into authored areas.</summary>
    public static void TickAreaActivations(
        MissionObjectiveRuntime? objectives,
        Vector3d? playerUnits,
        int playerTeam,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres,
        bool playerAlive) =>
        objectives?.TickLevelState(
            AreaActivationOccupant(playerUnits, cutsceneViewMetres, freeViewMetres),
            playerTeam,
            AreaActivationExtraOccupant(playerUnits, cutsceneViewMetres),
            playerAlive);

    /// <summary>
    /// Handles the retail transient-object cleanup native without giving it ownership of cinematic state.
    /// </summary>
    /// <param name="name">The native the script called.</param>
    /// <param name="clearTransientProjectiles">What to run when it is the cleanup one.</param>
    /// <returns>The script's answer, or null when this native is not the one.</returns>
    /// <remarks>
    /// <c>IGI.EXE!Game_CutsceneDelete</c> walks the mine/grenade/missile registry, not the QVM
    /// <c>CutScene</c> registry. Mission 3 calls it while scene 700 is starting, so completing scene 700
    /// here would immediately satisfy the authored mission-complete expression.
    /// </remarks>
    public static QvmValue? TryHandleCutsceneCleanupNative(string name, Action clearTransientProjectiles)
    {
        ArgumentNullException.ThrowIfNull(clearTransientProjectiles);
        if (!string.Equals(name, "Game_CutsceneDelete", StringComparison.Ordinal))
        {
            return null;
        }

        clearTransientProjectiles();
        return QvmValue.FromInt32(1);
    }
}
