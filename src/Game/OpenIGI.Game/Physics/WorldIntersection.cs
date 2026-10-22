using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>What a line struck, <c>QIntersection_s</c>: the nearest surface so far, which every handler a trace reaches may replace.</summary>
/// <remarks>
/// A trace hands each object's handler the same record for each of its lines; a handler only writes it when its
/// own hit is nearer than the one already there, so after the last object the record holds the nearest hit.
/// </remarks>
public sealed class WorldIntersection
{
    /// <summary>Gets or sets what was struck, <c>ptHitObj</c>.</summary>
    public IWorldCollider? HitObject { get; set; }

    /// <summary>Gets or sets where, in the world, <c>tPos</c>.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where, in the struck object's own frame, <c>tLocalPos</c>.</summary>
    public Vector3f LocalPosition { get; set; }

    /// <summary>Gets or sets the struck face's unit normal in the object's frame, <c>tLocalNormal</c>.</summary>
    public Vector3f LocalNormal { get; set; }

    /// <summary>Gets or sets the struck face's normal in the world, <c>tNormal</c>.</summary>
    public Vector3f Normal { get; set; }

    /// <summary>Gets or sets the sector struck, <c>iSector</c>, where the object has sectors.</summary>
    public int Sector { get; set; }

    /// <summary>Gets or sets the face struck, <c>iFace</c>.</summary>
    public int Face { get; set; }

    /// <summary>Gets or sets the portal the struck face closes, <c>iPortal</c>; <c>0xFFFF</c> for none.</summary>
    public int Portal { get; set; }

    /// <summary>Gets or sets the material the line went into, <c>ptEnterMaterial</c>, as a level material row.</summary>
    public int? EnterMaterial { get; set; }

    /// <summary>Gets or sets the material the line came out of, <c>ptLeaveMaterial</c>: a back face's.</summary>
    public int? LeaveMaterial { get; set; }

    /// <summary>Gets or sets a value indicating whether two faces were struck at all but the same distance, <c>isDoubleFace</c>.</summary>
    public bool IsDoubleFace { get; set; }

    /// <summary>Gets or sets a value indicating whether anything was struck, <c>isHit</c>.</summary>
    public bool IsHit { get; set; }

    /// <summary>Gets or sets how far along the line, <c>vDistance</c>.</summary>
    public double Distance { get; set; }

    /// <summary>Gets or sets the hit's barycentric position on the face, <c>tHitUV</c>.</summary>
    public (float U, float V) HitUV { get; set; }

    /// <summary>Gets or sets how bright the surface is where struck, <c>vLight</c>.</summary>
    public float Light { get; set; }

    /// <summary>Takes on every field of another record, as a structure copy does.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(WorldIntersection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        HitObject = other.HitObject;
        Position = other.Position;
        LocalPosition = other.LocalPosition;
        LocalNormal = other.LocalNormal;
        Normal = other.Normal;
        Sector = other.Sector;
        Face = other.Face;
        Portal = other.Portal;
        EnterMaterial = other.EnterMaterial;
        LeaveMaterial = other.LeaveMaterial;
        IsDoubleFace = other.IsDoubleFace;
        IsHit = other.IsHit;
        Distance = other.Distance;
        HitUV = other.HitUV;
        Light = other.Light;
    }

    /// <summary>Forgets any hit, as a trace does before it starts a line.</summary>
    public void Clear()
    {
        HitObject = null;
        Position = default;
        LocalPosition = default;
        LocalNormal = default;
        Normal = default;
        Sector = 0;
        Face = 0;
        Portal = 0;
        EnterMaterial = null;
        LeaveMaterial = null;
        IsDoubleFace = false;
        IsHit = false;
        Distance = 0.0;
        HitUV = default;
        Light = 0f;
    }
}
