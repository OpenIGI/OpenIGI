using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A door's link to the sector portal it stands in.</summary>
public sealed partial class Igi2DoorTask
{
    /// <summary>The portal a door is linked to none of, <c>0xFFFF</c>.</summary>
    public const int NoPortal = 0xFFFF;

    // How far up and down its own Y from its model's middle the door looks for the portal it stands in, half a metre.
    private const float PortalReach = 2048f;

    private Igi2RigidObject? _sector1;
    private Igi2RigidObject? _sector2;
    private int _portal1 = NoPortal;
    private int _portal2 = NoPortal;
    private Vector3d _portalPos;
    private bool _isAttachedToPortal;
    private int _status;

    /// <summary>Gets or sets the level's portal state, which a linked door counts up and down.</summary>
    public Igi2SectorPortals? Portals { get; set; }

    /// <summary>Gets or sets where the game's camera is, which a shut linked door is drawn toward the portal from.</summary>
    public Func<Vector3d>? Camera { get; set; }

    /// <summary>Gets the portal it is linked to from its front, or <see cref="NoPortal"/>.</summary>
    public int Portal1 => _portal1;

    /// <summary>Gets the portal it is linked to from its back, or <see cref="NoPortal"/>.</summary>
    public int Portal2 => _portal2;

    /// <summary>Gets where it is drawn shut while the camera is nearer there than its own place, <c>tPortalPos</c>.</summary>
    public Vector3d PortalPosition => _portalPos;

    /// <summary>Gets a value indicating whether it stands at <see cref="PortalPosition"/> now, <c>isAttachedToPortal</c>.</summary>
    public bool IsAttachedToPortal => _isAttachedToPortal;

    /// <summary>
    /// Finds the portal it stands in, <c>Door_AttachToPortal</c>: a line along its own Y through its model's middle, half a
    /// metre each way, traced for portal faces one way and then the other.
    /// </summary>
    /// <param name="world">The collision world, whose stationary objects the lines are traced against.</param>
    /// <remarks>
    /// <para>
    /// Any portal it held before is let go open. A door that does not link up, has no model, or is see-through links to
    /// nothing. Both lines must strike a sector's portal below sixty-four; then each portal's byte is written open unless the
    /// door is shut, and the place it is drawn when shut and seen from the portal's side is its model's middle moved by its
    /// <see cref="DeltaScaleFactor"/> of the way to the middle of the two portal hits.
    /// </para>
    /// <para>
    /// See-through is <c>RigidDynCubeObj_IsTransparent</c>, which asks the render mesh's records for a blend flag; the port
    /// asks the model's draw records whether any is a transparent face group.
    /// </para>
    /// </remarks>
    public void AttachToPortal(Igi2CollisionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (Portals is { } portals)
        {
            if (_sector1 is { } held1 && _portal1 != NoPortal)
            {
                portals.Set(held1, _portal1, 1);
            }

            if (_sector2 is { } held2 && _portal2 != NoPortal)
            {
                portals.Set(held2, _portal2, 1);
            }
        }

        _portal1 = NoPortal;
        _portal2 = NoPortal;
        _sector1 = null;
        _sector2 = null;
        _isAttachedToPortal = false;

        if (!LinkUpToPortal || Portals is null || Models?.Invoke(Model) is not MefModel model
            || model.DrawRecords.Any(static r => r.IsTransparent))
        {
            return;
        }

        Vector3f c = model.RenderSphereCentre;
        Vector3d start = FromModel(new Vector3f(c.X, c.Y - PortalReach, c.Z));
        Vector3d end = FromModel(new Vector3f(c.X, c.Y + PortalReach, c.Z));

        (int portal1, Igi2RigidObject? sector1, Vector3d hit1) = TraceForPortal(world, start, end);
        if ((uint)portal1 >= Igi2SectorPortals.Capacity || sector1 is null)
        {
            return;
        }

        (int portal2, Igi2RigidObject? sector2, Vector3d hit2) = TraceForPortal(world, end, start);
        if ((uint)portal2 >= Igi2SectorPortals.Capacity || sector2 is null)
        {
            return;
        }

        _sector1 = sector1;
        _sector2 = sector2;
        _portal1 = portal1;
        _portal2 = portal2;
        _status = Status;
        byte open = _status != 1 ? (byte)1 : (byte)0;
        Portals.Set(sector1, portal1, open);
        Portals.Set(sector2, portal2, open);

        Vector3d middle = FromModel(c);
        double scale = DeltaScaleFactor;
        _portalPos = new Vector3d(
            (scale * (((hit1.X + hit2.X) * 0.5) - middle.X)) + _start.X,
            (scale * (((hit1.Y + hit2.Y) * 0.5) - middle.Y)) + _start.Y,
            (scale * (((hit1.Z + hit2.Z) * 0.5) - middle.Z)) + _start.Z);
    }

    // The rest of Door_RunHandler, for a door linked to a portal. Shut, with the camera nearer where it is drawn at the portal
    // than its own place, it stands at the portal; otherwise back at its own. Then, its state changed, the portals count it:
    // one more open as it is told to open, one fewer as it comes to rest shut.
    private void RunPortal()
    {
        if (_portal1 == NoPortal)
        {
            return;
        }

        int status = Status;
        if (Camera?.Invoke() is { } camera)
        {
            // Door_RunHandler: the squares summed z, y, x on the client's FPU, as Real64x3_Distance sums them.
            double toPortal = Igi2Real64x3.DistanceSquared(_portalPos, camera).Value, toPlace = Igi2Real64x3.DistanceSquared(Position, camera).Value;
            if (toPortal < toPlace && status == 1)
            {
                if (!_isAttachedToPortal)
                {
                    _isAttachedToPortal = true;
                    _start = _portalPos;
                    SetPosition();
                }
            }
            else if (_isAttachedToPortal)
            {
                _isAttachedToPortal = false;
                _start = Position;
                SetPosition();
            }
        }

        if (status == _status)
        {
            return;
        }

        if (Portals is { } portals && _sector1 is { } sector1 && _sector2 is { } sector2)
        {
            if (status == 2)
            {
                portals.Open(sector1, _portal1);
                portals.Open(sector2, _portal2);
            }
            else if (status == 1)
            {
                portals.Close(sector1, _portal1);
                portals.Close(sector2, _portal2);
            }
        }

        _status = status;
    }

    // Door_TraceForPortals: a line through the stationary world that portal faces stop, other doors not answering it.
    private static (int Portal, Igi2RigidObject? Sector, Vector3d Hit) TraceForPortal(Igi2CollisionWorld world, Vector3d from, Vector3d to)
    {
        Igi2LineTrace trace = new(1) { Mesh = 0, TracePortals = true };
        trace.Starts[0] = from;
        trace.Ends[0] = to;
        trace.Includes = static owner => owner is Igi2CollisionOwner solidOwner && !string.Equals(solidOwner.TypeName, TypeName, StringComparison.Ordinal);
        world.TraceLines(trace, ~Igi2CollisionWorld.TerrainGroup);

        Igi2Intersection hit = trace.Intersections[0];
        if (!hit.IsHit || hit.Portal == NoPortal || hit.HitObject is not Igi2RigidObject solid)
        {
            return (NoPortal, null, default);
        }

        return Igi2SectorPortals.IsSector(solid) ? (hit.Portal, solid, hit.Position) : (NoPortal, null, default);
    }

    // A point of its model placed by the frame and the start it was built with, tOriginalOrientation and tOriginalPosStart.
    private Vector3d FromModel(Vector3f v)
    {
        float[] m = _base;
        return new Vector3d(
            ((double)m[0] * v.X) + ((double)m[1] * v.Y) + ((double)m[2] * v.Z) + _start.X,
            ((double)m[3] * v.X) + ((double)m[4] * v.Y) + ((double)m[5] * v.Z) + _start.Y,
            ((double)m[6] * v.X) + ((double)m[7] * v.Y) + ((double)m[8] * v.Z) + _start.Z);
    }
}
