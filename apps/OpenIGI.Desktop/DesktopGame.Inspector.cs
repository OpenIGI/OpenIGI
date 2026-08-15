using System.Globalization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Desktop;

/// <summary>
/// The game's answer to the developer inspector: what is in the world, and how to go and look at it.
/// </summary>
/// <remarks>
/// <para>
/// A partial of the game rather than a type of its own, following the same split as the mission and
/// menu partials, because everything here needs the fields the loop already holds — the soldiers, the
/// player, the camera and the terrain.
/// </para>
/// <para>
/// This is the only place gameplay meets the developer tools. Everything crosses as a snapshot or a
/// string, so the panels never hold a soldier that a mission change could invalidate underneath them.
/// </para>
/// </remarks>
internal sealed partial class DesktopGame : IWorldInspector
{
    private readonly List<SoldierSnapshot> _inspectorSoldiers = [];

    /// <inheritdoc/>
    public void Describe(string filter, List<InspectableEntity> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        _inspectorSoldiers.Clear();
        _soldiers?.Snapshot(_inspectorSoldiers);

        string needle = filter ?? string.Empty;

        if (_player is not null && Matches("player", needle))
        {
            into.Add(new InspectableEntity(
                PlayerInspectorId,
                "player",
                "player",
                _player.Body.Position,
                _playerDead ? "dead" : "alive"));
        }

        for (int i = 0; i < _inspectorSoldiers.Count; i++)
        {
            SoldierSnapshot soldier = _inspectorSoldiers[i];
            if (!Matches(soldier.Name, needle) && !Matches("soldier", needle))
            {
                continue;
            }

            into.Add(new InspectableEntity(
                i,
                soldier.IsCutsceneActor ? "actor" : "soldier",
                soldier.Name.Length > 0 ? soldier.Name : $"#{i}",
                soldier.Position,
                DescribeState(soldier)));
        }
    }

    /// <inheritdoc/>
    public void Detail(int id, List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        if (id == PlayerInspectorId)
        {
            if (_player is null)
            {
                return;
            }

            Vector3d at = _player.Body.Position;
            into.Add(Position(at));
            into.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"yaw {_player.Body.Yaw:0.00}   pitch {_player.Body.Pitch:0.00}"));
            return;
        }

        if (!TryGetSoldier(id, out SoldierSnapshot soldier))
        {
            return;
        }

        into.Add(Position(soldier.Position));
        into.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"animation {soldier.Animation}   health {(soldier.HealthFraction < 0f ? "n/a" : soldier.HealthFraction.ToString("0.00", CultureInfo.InvariantCulture))}"));
    }

    /// <inheritdoc/>
    public bool TryTeleportPlayerTo(int id)
    {
        if (_player is null || !TryResolvePosition(id, out Vector3d position))
        {
            return false;
        }

        return _player.Place(_playerTerrain, position, _player.Body.Yaw);
    }

    /// <inheritdoc/>
    public bool TryMoveCameraTo(int id)
    {
        if (_camera is null || !TryResolvePosition(id, out Vector3d position))
        {
            return false;
        }

        // Stand back and a little above rather than inside the thing being looked at, which is what
        // "go and look at it" means and what putting the camera at its exact origin does not.
        _camera.MoveTo(position + new Vector3d(0.0, -6.0, 3.0), _camera.Yaw, _camera.Pitch);
        return true;
    }

    /// <inheritdoc/>
    public bool TryProject(int id, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        return false;
    }

    /// <summary>The handle the player answers to, chosen so it can never collide with a soldier index.</summary>
    private const int PlayerInspectorId = -1;

    private bool TryGetSoldier(int id, out SoldierSnapshot soldier)
    {
        if (id >= 0 && id < _inspectorSoldiers.Count)
        {
            soldier = _inspectorSoldiers[id];
            return true;
        }

        soldier = default;
        return false;
    }

    private bool TryResolvePosition(int id, out Vector3d position)
    {
        if (id == PlayerInspectorId && _player is not null)
        {
            position = _player.Body.Position;
            return true;
        }

        if (TryGetSoldier(id, out SoldierSnapshot soldier))
        {
            position = soldier.Position;
            return true;
        }

        position = default;
        return false;
    }

    private static bool Matches(string candidate, string needle) =>
        needle.Length == 0 || candidate.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string DescribeState(in SoldierSnapshot soldier)
    {
        if (soldier.IsDead)
        {
            return "dead";
        }

        return soldier.HealthFraction is >= 0f and < 1f
            ? string.Create(CultureInfo.InvariantCulture, $"hurt {soldier.HealthFraction:0.00}")
            : "alive";
    }

    private static string Position(Vector3d at) => string.Create(
        CultureInfo.InvariantCulture,
        $"position {at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}");
}
