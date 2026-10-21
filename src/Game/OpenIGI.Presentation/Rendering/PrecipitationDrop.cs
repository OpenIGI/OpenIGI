using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One falling drop of the storm, in world metres, as <see cref="PrecipitationSimulation"/> carries it.</summary>
internal struct PrecipitationDrop
{
    public Vector3d Position;
    public Vector3d Previous;
    public Vector3f Velocity;
    public float WindX;
    public float WindY;

    /// <summary>The drop's own sideways acceleration, integrated over its age. See SpawnDrift.</summary>
    public float DriftX;
    public float DriftY;
    public float DriftZ;
    public float Gravity;
    public float Terminal;
    public float Life;
    public float Age;
    public float Seed;
    public float Scale;
    public int Cell;
}
