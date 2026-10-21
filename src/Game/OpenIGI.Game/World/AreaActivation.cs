using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One <c>AreaActivate</c> task's oriented volume and the criteria naming whose occupants it counts.</summary>
/// <remarks>Counted by <see cref="MissionAreaActivation"/>, which owns the occupants it is tested against.</remarks>
internal sealed class AreaActivation
{
    public DeclaredTask Task { get; }

    private readonly Vector3d _minimum;
    private readonly Vector3d _maximum;
    private readonly OccupantFilter _filter;
    private readonly Vector3d _position;
    private readonly Matrix3f _orientation;
    private readonly Vector3f _half;

    public AreaActivation(DeclaredTask task, Vector3d position, Matrix3f orientation, Vector3f dimensions, string criteria)
    {
        Task = task;
        _position = position;
        _orientation = orientation;
        _half = dimensions * 0.5f;
        // AreaActivate::Prepare (0x5045F0) halves Dimensions, rotates all eight corners and
        // publishes their component-wise min/max as the query bounds.
        Vector3f half = dimensions * 0.5f;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3f local = new(
                (corner & 1) == 0 ? half.X : -half.X,
                (corner & 2) == 0 ? half.Y : -half.Y,
                (corner & 4) == 0 ? half.Z : -half.Z);
            Vector3f rotated = orientation.Transposed().Transform(local);
            Vector3d world = position + new Vector3d(rotated.X, rotated.Y, rotated.Z);
            minX = Math.Min(minX, world.X); minY = Math.Min(minY, world.Y); minZ = Math.Min(minZ, world.Z);
            maxX = Math.Max(maxX, world.X); maxY = Math.Max(maxY, world.Y); maxZ = Math.Max(maxZ, world.Z);
        }
        _minimum = new Vector3d(minX, minY, minZ);
        _maximum = new Vector3d(maxX, maxY, maxZ);

        // The criteria names which humans the volume counts. An empty or unknown criteria counts
        // every human; CRITERIA_HUMAN0/HUMAN1 count a team; the player-only spellings count just the
        // player object. Mission 4's gate volumes use CRITERIA_HUMAN0 so the FRIENDLY squad's advance
        // -- not just the player walking in -- registers an occupant, which is what turns the
        // objective-complete trigger once the squad reaches the internal plateau.
        string filter = criteria.Trim();
        if (filter.Equals("CRITERIA_HUMAN0", StringComparison.OrdinalIgnoreCase))
        {
            _filter = OccupantFilter.Team0;
        }
        else if (filter.Equals("CRITERIA_HUMAN1", StringComparison.OrdinalIgnoreCase))
        {
            _filter = OccupantFilter.Team1;
        }
        else if (filter.Contains("HumanPlayer", StringComparison.OrdinalIgnoreCase)
            || filter.Equals("PLAYER", StringComparison.OrdinalIgnoreCase))
        {
            _filter = OccupantFilter.PlayerOnly;
        }
        else if (filter.Equals("CRITERIA_VEHICLE", StringComparison.OrdinalIgnoreCase))
        {
            _filter = OccupantFilter.Vehicle;
        }
        else
        {
            _filter = OccupantFilter.All;
        }
    }

    /// <summary>Whether the player (on the given team) counts toward this volume.</summary>
    public bool AcceptsPlayer(int team) => _filter switch
    {
        OccupantFilter.Team0 => team == 0,
        OccupantFilter.Team1 => team == 1,
        OccupantFilter.Vehicle => false,
        _ => true, // All, or PlayerOnly (the player is the player)
    };

    /// <summary>Whether a soldier on the given team counts toward this volume.</summary>
    public bool AcceptsSoldier(int team) => _filter switch
    {
        OccupantFilter.Team0 => team == 0,
        OccupantFilter.Team1 => team == 1,
        OccupantFilter.PlayerOnly => false,
        OccupantFilter.Vehicle => false,
        _ => true, // All
    };

    /// <summary>Whether a vehicle counts toward this volume.</summary>
    public bool AcceptsVehicle => _filter is OccupantFilter.Vehicle or OccupantFilter.All;

    public int ActiveCount { get; set; }

    /// <summary>Whether a point lies in the volume: the box test <c>0x5042B0</c>, in the area's own frame.</summary>
    /// <remarks>
    /// The rotated corners' min/max (<c>+32</c>/<c>+56</c>) are only the query's bounds; the test itself takes the
    /// offset into the area's axes (<c>Mᵀ·(p − pos)</c>, single precision) and compares each against the half
    /// extent. For an area turned off the world axes -- mission 7's gate volumes are turned 126° -- the bounds
    /// hold far more ground than the box does.
    /// </remarks>
    public bool Contains(Vector3d point)
    {
        if (point.X < _minimum.X || point.X > _maximum.X
            || point.Y < _minimum.Y || point.Y > _maximum.Y
            || point.Z < _minimum.Z || point.Z > _maximum.Z)
        {
            return false;
        }

        double dx = point.X - _position.X, dy = point.Y - _position.Y, dz = point.Z - _position.Z;
        Matrix3f m = _orientation;
        float x = (float)((dz * m.M31) + (dy * m.M21) + (dx * m.M11));
        float y = (float)((dz * m.M32) + (dy * m.M22) + (dx * m.M12));
        float z = (float)((dz * m.M33) + (dy * m.M23) + (dx * m.M13));
        return !(x > _half.X) && !(y > _half.Y) && !(z > _half.Z)
            && !(-_half.X > x) && -_half.Y <= y && -_half.Z <= z;
    }

    private enum OccupantFilter
    {
        All,
        Team0,
        Team1,
        PlayerOnly,
        Vehicle,
    }
}
