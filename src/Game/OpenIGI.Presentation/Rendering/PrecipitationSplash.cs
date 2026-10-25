using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One splash of the storm: a standing crown or a thrown droplet, in world metres.</summary>
internal struct PrecipitationSplash
{
    public double X;
    public double Y;
    public double Z;
    public Vector3f Velocity;
    public float Age;
    public float Life;
    public float Radius;
    public int Variant;
    public bool Small;

    /// <summary>Whether this splash's cell is read right to left. See SplashColumnUv.</summary>
    public bool FlipU;
}
