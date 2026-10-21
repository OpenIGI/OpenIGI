using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One live impact particle: a spark, a dust puff, a glass shard or a retail sprite.</summary>
/// <remarks>Owned by <see cref="ImpactParticleRenderer"/>'s pool and turned into a quad by <see cref="ParticleQuadBuilder"/>.</remarks>
internal struct ImpactParticle
{
    public Vector3d Position;
    public Vector3d PreviousPosition;
    public Vector3f Velocity;
    public int LifeTicks;
    public int AgeTicks;
    public float Size;
    public float Growth;
    public float GravityScale;
    public float Drag;
    public float Red;
    public float Green;
    public float Blue;
    public bool IsSpark;
    public bool IsGlassShard;
    public bool IsRetailSprite;
    public bool IsRetailSmoke;
    public float RetailU0;
    public float RetailV0;
    public float RetailU1;
    public float RetailV1;
    public Vector3d GlassA;
    public Vector3d GlassB;
    public Vector3d GlassC;
    public Vector3d GlassNormal;
}
