using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>What <see cref="ObjectCollisionIndex.WalkIndexed{TTest}"/> asks of each entry the ray's cells hold.</summary>
internal interface IObjectCollisionEntryTest
{
    /// <returns><see langword="true"/> when the entry is the nearest contact so far.</returns>
    bool Test(ObjectCollisionEntry entry, Vector3d origin, Vector3d direction, ref double nearest);
}

/// <summary>The legacy ray's test: <see cref="ObjectCollisionIndex.TestEntry"/>, accumulating a <see cref="WorldRayHit"/>.</summary>
internal struct ObjectCollisionRayTest(ObjectCollisionIndex index) : IObjectCollisionEntryTest
{
    public WorldRayHit Hit;

    /// <summary>Whether each model is tested with the original's own arithmetic (<see cref="RetailModelRay"/>).</summary>
    public bool RetailArithmetic;

    /// <summary>The ray's end, which the original's test takes rather than a direction.</summary>
    public Vector3d End;

    public bool Test(ObjectCollisionEntry entry, Vector3d origin, Vector3d direction, ref double nearest) =>
        index.TestEntry(entry, origin, direction, ref nearest, ref Hit, RetailArithmetic ? End : null);
}

/// <summary>The rich request's test: its ignore list and mask on top of the live filter.</summary>
internal struct ObjectCollisionTraceTest(ObjectCollisionIndex index, WorldTraceRequest request) : IObjectCollisionEntryTest
{
    public WorldTraceHit Hit;

    public bool Test(ObjectCollisionEntry entry, Vector3d origin, Vector3d direction, ref double nearest)
    {
        if ((request.IgnoredOwner is not null && ReferenceEquals(entry.Owner, request.IgnoredOwner))
            || (request.IgnoredSubject is not null && ReferenceEquals(entry.Owner, request.IgnoredSubject))
            || (request.IgnoredPlacement is not null && ReferenceEquals(entry.Placement, request.IgnoredPlacement))
            || index.GenericPickups?.IsCollected(entry.Owner) == true
            || index.IsRetired(entry)
            || index.IsPassable(entry.Owner)
            || (!request.IncludeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner)))
        {
            return false;
        }

        WorldTraceHitKind kind = entry.IsDynamic
            ? WorldTraceHitKind.DynamicObject
            : WorldTraceHitKind.StaticObject;
        WorldTraceMask category = entry.IsDynamic
            ? WorldTraceMask.DynamicObjects
            : WorldTraceMask.StaticObjects;
        bool includePortals = request.TracePortals;
        if ((request.Mask & category) == 0 || !ObjectCollisionIndex.MeetsSphere(origin, direction, entry.Centre, entry.Radius))
        {
            return false;
        }

        WorldRayHit candidate;
        if (request.RetailArithmetic && !includePortals && entry.SupportsRetailRay)
        {
            if (!entry.TryCastRetail(origin, request.End, ref nearest, out candidate))
            {
                return false;
            }
        }
        else if (!entry.TryCast(
            origin,
            direction,
            ref nearest,
            useClosedTransform: false,
            includePortals,
            portalIsOpening: includePortals ? index.PortalIsSightOpening : null,
            out candidate))
        {
            return false;
        }

        Hit = WorldTraceHit.FromRayHit(request, candidate, kind, entry.Owner, entry.Label, entry.Placement);
        return true;
    }
}
