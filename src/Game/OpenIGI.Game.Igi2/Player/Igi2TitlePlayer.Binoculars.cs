using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The overlay the player's zoom has up, and what the binoculars' overlay reads off the world: the range to what they are trained
/// on and the people they pick out (<c>binocularoverlay.c</c>).
/// </content>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>The binoculars' overlay task type's name.</summary>
    public const string BinocularOverlayType = "BinocularOverlay";

    /// <summary>How far the binoculars range, in world units: a thousand metres (<c>BinocularOverlay_Draw</c>).</summary>
    public const double BinocularReach = 4096000.0;

    /// <summary>The laser designator's overlay task type's name.</summary>
    public const string LaserDesignatorOverlayType = "LaserDesignatorOverlay";

    /// <summary>The query groups the binoculars' range is traced through.</summary>
    public const uint BinocularRangeGroups = 0xD8000000;

    /// <summary>
    /// The query groups a scope's and the laser designator's range is traced through, the forests' and <c>0x98000000</c>
    /// (<c>SniperOverlay_DrawRangeMeter</c>, <c>LaserDesignatorOverlay_Draw</c>).
    /// </summary>
    public const uint ScopeRangeGroups = Igi2CollisionWorld.ForestGroup | 0x98000000;

    /// <summary>The most people the binoculars pick out at once.</summary>
    public const int BinocularSlots = 10;

    /// <summary>How long a picked-out person's slot is kept once out of sight, in milliseconds.</summary>
    public const int BinocularForgetMilliseconds = 3000;

    private readonly Igi2BinocularMark?[] _marks = new Igi2BinocularMark?[BinocularSlots];

    /// <summary>Gets or sets the overlay task types' names by id, as the session's <c>TASKTYPE_*</c> constants give them.</summary>
    internal IReadOnlyDictionary<int, string>? OverlayTypeNames { get; set; }

    /// <summary>Gets or sets the level's localised strings, or <see langword="null"/>.</summary>
    public Flow.Igi2Strings? Strings { get; set; }

    /// <summary>
    /// Gets the name of the overlay the player's zoom has up, its <c>Overlay QTaskType</c> (<c>Weapon_GetOverlayQTask</c>), or
    /// <see langword="null"/> with none.
    /// </summary>
    public string? ZoomOverlay =>
        Weapons is { Firing.OverlayStatus: Igi2WeaponOverlayStatus.WithOverlay, Effects: Igi2LevelWeaponEffects { Overlay: { } zoom } }
        && OverlayTypeNames is { } names && names.TryGetValue(zoom.OverlayQTaskType, out string? name)
            ? name
            : null;

    /// <summary>
    /// Gets the distance to what the zoom's overlay is trained on, in world units, or <see langword="null"/> when nothing is within
    /// a thousand metres or no overlay is up.
    /// </summary>
    public double? ZoomRange { get; private set; }

    /// <summary>Gets the people the binoculars pick out, by slot; an empty slot is <see langword="null"/>.</summary>
    public IReadOnlyList<Igi2BinocularMark?> BinocularMarks => _marks;

    /// <summary>Gets the time the binoculars' marks are timed by, in milliseconds: the level's ticks at thirty a second.</summary>
    public long BinocularClock => _ticks * 1000 / 30;

    /// <summary>
    /// What the binoculars read this tick, while their overlay is up: the range along the view (<c>BinocularOverlay_Draw</c>), and
    /// each living, active person in front of the eye registered with whether a line to the head is blocked
    /// (<c>BinocularOverlay_TrackMovement</c>, <c>BinocularOverlay_RegisterObservedTask</c>).
    /// </summary>
    private void ReadBinoculars()
    {
        string? overlay = ZoomOverlay;
        if (overlay is null)
        {
            ZoomRange = null;
            return;
        }

        Vector3d eye = Human.View.Position;
        Matrix3f view = Human.View.Orientation;
        Vector3f forward = new(view.M12, view.M22, view.M32);
        bool binoculars = overlay == BinocularOverlayType;
        WorldLineTrace trace = new(1)
        {
            Includes = owner => !ReferenceEquals(owner, Human),
        };
        trace.Starts[0] = eye;
        trace.Ends[0] = new Vector3d(eye.X + (forward.X * BinocularReach), eye.Y + (forward.Y * BinocularReach), eye.Z + (forward.Z * BinocularReach));
        CollisionWorld.TraceLines(trace, binoculars ? BinocularRangeGroups : ScopeRangeGroups);
        WorldIntersection hit = trace.Intersections[0];
        ZoomRange = hit.IsHit ? hit.Distance : null;
        if (overlay == LaserDesignatorOverlayType)
        {
            ForgetFarTarget();
        }

        if (!binoculars)
        {
            return;
        }

        long now = BinocularClock;
        for (int i = 0; i < _marks.Length; i++)
        {
            _marks[i]?.IsActive = false;
        }

        if (Soldiers is null || Materials is not { } materials)
        {
            Forget(now);
            return;
        }

        float tangent = Human.View.FieldOfView;
        foreach (Soldiers.Igi2HumanSoldier body in Soldiers.Bodies)
        {
            if (body.IsDead || !body.IsActive)
            {
                continue;
            }

            Vector3d at = body.Position;
            double dx = at.X - eye.X, dy = at.Y - eye.Y, dz = at.Z - eye.Z;
            double ahead = (dx * view.M12) + (dy * view.M22) + (dz * view.M32);
            double across = (dx * view.M11) + (dy * view.M21) + (dz * view.M31);
            double up = (dx * view.M13) + (dy * view.M23) + (dz * view.M33);
            double radius = body.ModelRadius;
            double reach = (ahead * tangent) + radius;
            if (ahead + radius <= 0.0 || Math.Abs(across) > reach || Math.Abs(up) > reach)
            {
                continue;
            }

            WorldLineOfSight sight = new(eye, body.CheapHeadBonePosition(), materials)
            {
                Includes = owner => !ReferenceEquals(owner, Human) && !ReferenceEquals(owner, body),
            };
            CollisionWorld.LineOfSight(sight, uint.MaxValue);
            if (ahead > 0.0)
            {
                Register(body, ahead, sight.IsBlocked, body.Team != 1, now);
            }
        }

        Forget(now);
    }

    /// <summary>
    /// The designator's target is forgotten while what it is trained on lies past its range, whole metres against its
    /// <c>Range</c> (<c>LaserDesignatorOverlay_Draw</c>).
    /// </summary>
    private void ForgetFarTarget()
    {
        if (ZoomRange is not { } range || Weapons?.Firing.LaserDesignator is not { } designator
            || Inventory?.Current?.WeaponType.FireType(0) is not Igi2LaserDesignatorTask parameters)
        {
            return;
        }

        if (parameters.Range < Math.Floor(range * 0.000244140625))
        {
            designator.TickFirstHit = 0;
            designator.HitTask = null;
        }
    }

    private void Register(Igi2Human body, double distance, bool occluded, bool friendly, long now)
    {
        int slot = Array.FindIndex(_marks, m => m is not null && ReferenceEquals(m.Body, body));
        if (slot < 0)
        {
            slot = Array.FindIndex(_marks, static m => m is null);
        }

        if (slot < 0)
        {
            slot = Array.FindIndex(_marks, m => m!.Distance > distance);
            if (slot < 0)
            {
                return;
            }
        }

        Igi2BinocularMark mark = _marks[slot] is { } held && ReferenceEquals(held.Body, body) ? held : new Igi2BinocularMark(body, now);
        mark.IsActive = true;
        mark.IsOccluded = occluded;
        mark.IsFriendly = friendly;
        mark.Distance = distance;
        mark.LastSeen = now;
        _marks[slot] = mark;
    }

    private void Forget(long now)
    {
        for (int i = 0; i < _marks.Length; i++)
        {
            if (_marks[i] is { IsActive: false } mark && now - mark.LastSeen > BinocularForgetMilliseconds)
            {
                _marks[i] = null;
            }
        }
    }
}

/// <summary>A person the binoculars pick out, one of their observed slots.</summary>
/// <param name="body">The person.</param>
/// <param name="firstSeen">When the slot took them, in the binoculars' milliseconds: the mark closes in over the quarter second after.</param>
public sealed class Igi2BinocularMark(Igi2Human body, long firstSeen)
{
    /// <summary>Gets the person.</summary>
    public Igi2Human Body { get; } = body;

    /// <summary>Gets when the slot took them.</summary>
    public long FirstSeen { get; } = firstSeen;

    /// <summary>Gets or sets when they were last registered.</summary>
    public long LastSeen { get; set; }

    /// <summary>Gets or sets a value indicating whether they were registered this tick.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets a value indicating whether the line to their head is blocked.</summary>
    public bool IsOccluded { get; set; }

    /// <summary>Gets or sets a value indicating whether they are not of the enemy team.</summary>
    public bool IsFriendly { get; set; }

    /// <summary>Gets or sets their distance ahead of the eye, in world units.</summary>
    public double Distance { get; set; }
}
