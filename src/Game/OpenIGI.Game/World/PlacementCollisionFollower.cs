using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Keeps a level's object collision (<see cref="ObjectCollisionIndex"/>) where the level's objects stand: each placement a
/// title moves (<see cref="IPlacementMotion"/>) is moved in the index as it moves, and one the title takes out of its world
/// (<see cref="IPlacementPresence"/>) is passed through until it is put back.
/// </summary>
/// <remarks>
/// <para>
/// A title whose own humans walk the index moves it itself, as its doors and lifts run. This is for an index nothing moves:
/// one built for a title that collides in a world of its own, which another game's humans then walk. They would otherwise
/// meet every door where it was hung and every lift where it was parked.
/// </para>
/// <para>
/// It asks for each placement where it stands at the tick (the whole of it, no blend), and writes only what changed since it
/// last asked.
/// </para>
/// </remarks>
public sealed class PlacementCollisionFollower
{
    private readonly ObjectCollisionIndex _solids;
    private readonly IReadOnlyList<WorldPlacement> _placements;
    private readonly IPlacementMotion? _motions;
    private readonly IPlacementPresence? _presence;
    private readonly (Vector3d Position, Matrix3f Orientation)?[] _stood;
    private readonly bool[] _away;

    /// <summary>Initializes a new instance of the <see cref="PlacementCollisionFollower"/> class.</summary>
    /// <param name="solids">The collision to keep.</param>
    /// <param name="placements">The level's placements, which the collision was built from.</param>
    /// <param name="motions">What moves the placements, or <see langword="null"/> where nothing does.</param>
    /// <param name="presence">What takes placements out of the world, or <see langword="null"/> where nothing does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="solids"/> or <paramref name="placements"/> is <see langword="null"/>.</exception>
    public PlacementCollisionFollower(
        ObjectCollisionIndex solids, IReadOnlyList<WorldPlacement> placements, IPlacementMotion? motions, IPlacementPresence? presence)
    {
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(placements);
        _solids = solids;
        _placements = placements;
        _motions = motions;
        _presence = presence;
        _stood = new (Vector3d, Matrix3f)?[placements.Count];
        _away = new bool[placements.Count];
    }

    /// <summary>Gets how many placements the last <see cref="Follow"/> moved or took in or out of the world.</summary>
    public int Changed { get; private set; }

    /// <summary>Brings the collision to where the placements stand now.</summary>
    public void Follow()
    {
        int changed = 0;
        for (int i = 0; i < _placements.Count; i++)
        {
            WorldPlacement placement = _placements[i];
            if (_motions is not null && _motions.TryGetTransform(placement, 1f, out Vector3d position, out Matrix3f orientation))
            {
                (Vector3d Position, Matrix3f Orientation) now = (position, orientation);

                // A placement not yet seen to move stands where it was built: the index already has it there.
                (Vector3d Position, Matrix3f Orientation) before = _stood[i] ?? (placement.Position, placement.Orientation);
                if (now != before && _solids.UpdateTransform(placement, position, orientation))
                {
                    changed++;
                }

                _stood[i] = now;
            }

            bool away = _presence is not null && !_presence.IsInWorld(placement);
            if (away != _away[i])
            {
                _away[i] = away;
                _solids.SetAway(placement, away);
                changed++;
            }
        }

        Changed = changed;
    }
}
