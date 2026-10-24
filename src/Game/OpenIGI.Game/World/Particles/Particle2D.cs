using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Particles;

/// <summary>
/// One particle of a <see cref="Particle2DSystem"/>, a slot of <c>Particle2DDynCubeObj</c>'s particle table: 88 bytes in both
/// titles' engines (Project I.G.I. <c>0x4FD660</c> steps it and <c>0x4FD020</c> draws it; I.G.I. 2's
/// <c>Particle2DDynCubeObj_RunHandler</c> and <c>_EnterHandler</c> are the same code).
/// </summary>
public sealed class Particle2D
{
    /// <summary>Gets or sets where the particle is, in world units (offset 0, three doubles).</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its speed, in world units a tick (offset 24).</summary>
    public Vector3f Speed { get; set; }

    /// <summary>Gets or sets the ticks before its next collision check (offset 36).</summary>
    public short CollisionCountdown { get; set; }

    /// <summary>Gets or sets the ticks it lives before it starts to fade (offset 38).</summary>
    public short LifeTime { get; set; }

    /// <summary>Gets or sets the sprite frame it shows (offset 40).</summary>
    public short Frame { get; set; }

    /// <summary>Gets or sets the ticks it has shown its frame (offset 42).</summary>
    public short FrameTicks { get; set; }

    /// <summary>Gets or sets what its rotation turns by each tick, in radians (offset 44); a particle that does not spin is drawn unturned.</summary>
    public float RotationDelta { get; set; }

    /// <summary>Gets or sets its intensity (offset 48): its alpha, or what darkens its colour, as its system draws it; it dies at nought.</summary>
    public float Intensity { get; set; }

    /// <summary>Gets or sets what its intensity loses each tick once its life is spent (offset 52).</summary>
    public float Fade { get; set; }

    /// <summary>Gets or sets its rotation, in radians (offset 56).</summary>
    public float Rotation { get; set; }

    /// <summary>Gets or sets its size (offset 60): its system's drawn size is scaled by it when the system grows; it dies at nought.</summary>
    public float Size { get; set; }

    /// <summary>Gets or sets where it was at its last collision check (offset 64).</summary>
    public Vector3d LastCheckedPosition { get; set; }
}
