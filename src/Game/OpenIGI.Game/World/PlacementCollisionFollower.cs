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

    // The placements the last Follow moved, each with where it stood before and stands now.
    private readonly List<(int Index, (Vector3d Position, Matrix3f Orientation) Before, (Vector3d Position, Matrix3f Orientation) Now)> _moved = [];
    private readonly Dictionary<WorldPlacement, int> _indexOf = [];

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
        for (int i = 0; i < placements.Count; i++)
        {
            _indexOf.TryAdd(placements[i], i);
        }
    }

    /// <summary>Gets how many placements the last <see cref="Follow"/> moved or took in or out of the world.</summary>
    public int Changed { get; private set; }

    /// <summary>Brings the collision to where the placements stand now.</summary>
    public void Follow()
    {
        int changed = 0;
        _moved.Clear();
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
                    _moved.Add((i, before, now));
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

    /// <summary>
    /// Carries a point that stands on something the last <see cref="Follow"/> moved: where the thing took it, and how far it
    /// turned it about the upright. A human standing in a lift, on a train or in a helicopter rides it by this
    /// (<c>Human_UpdatePlatformPos</c>).
    /// </summary>
    /// <param name="carrier">
    /// The placement the point rode last time, as this answered it, or -1 for none: it is kept while the point stays over it, so
    /// a carrier moving faster than the reach below the feet does not leave its rider behind.
    /// </param>
    /// <param name="feet">The point, in world units: a human's feet.</param>
    /// <param name="carried">Receives where the point is carried to.</param>
    /// <param name="turn">Receives the turn about the upright, in radians, clockwise from +Y as a heading is.</param>
    /// <returns><see langword="true"/> when the point was carried; <paramref name="carrier"/> is then what carried it.</returns>
    public bool TryCarry(ref int carrier, Vector3d feet, out Vector3d carried, out float turn)
    {
        carried = feet;
        turn = 0f;

        // What the point stands on now is what carries it; with nothing under it, what carried it last keeps it for as long as
        // it goes on moving, so a carrier that outruns the reach below the feet does not drop its rider.
        int under = Under(feet);
        if (under >= 0)
        {
            carrier = under;
        }

        foreach ((int index, (Vector3d Position, Matrix3f Orientation) before, (Vector3d Position, Matrix3f Orientation) now) in _moved)
        {
            if (index != carrier)
            {
                continue;
            }

            // The point in the placement's own frame as it stood, then out of that frame as it stands.
            Vector3d local = before.Orientation.Transform(feet - WorldScene.ToUnits(before.Position));
            carried = WorldScene.ToUnits(now.Position) + now.Orientation.Transposed().Transform(local);
            Vector3d ahead = now.Orientation.Transposed().Transform(before.Orientation.Transform(new Vector3d(0.0, 1.0, 0.0)));
            turn = (float)SimMath.Atan2(ahead.X, ahead.Y);
            return true;
        }

        if (under < 0)
        {
            carrier = -1;
        }

        return false;
    }

    /// <summary>What a point stands on within a step: from half a metre above the feet to a metre below.</summary>
    /// <param name="feet">The point, in world units: a human's feet.</param>
    /// <param name="placement">Receives the placement stood on.</param>
    /// <param name="footing">Receives the face stood on, its material among it.</param>
    /// <returns><see langword="true"/> when the point stands on one of the level's placements.</returns>
    public bool TryStandingOn(Vector3d feet, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorldPlacement? placement, out WorldRayHit footing) =>
        _solids.TryIdentify(
            new Vector3d(feet.X, feet.Y, feet.Z + 2048.0), new Vector3d(feet.X, feet.Y, feet.Z - 4096.0), out _, out placement, out footing)
        && placement is not null;

    // The placement under a point within a step, by its place in the list, or -1.
    private int Under(Vector3d feet) =>
        TryStandingOn(feet, out WorldPlacement? struck, out _) && _indexOf.TryGetValue(struck, out int index) ? index : -1;
}
