using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>
/// One line of sight, <c>LineOfSightContext_s</c> as <c>World_LineOfSight</c> (<c>igi2 0x551F60</c>) fills it: the line,
/// what may stop it, and how much of it gets through.
/// </summary>
/// <remarks>
/// Every face the line crosses marks it crossed. A face of a material sight cannot pass at all (transparency zero, or no
/// material) stops it outright; a face it enters from the front lets through the material's share of what is left. The
/// nearest material it crossed is kept.
/// </remarks>
public sealed class WorldLineOfSight
{
    /// <summary>Initializes a new instance of the <see cref="WorldLineOfSight"/> class.</summary>
    /// <param name="start">Where it starts, <c>ptStart</c>.</param>
    /// <param name="end">Where it ends, <c>ptEnd</c>.</param>
    /// <param name="materials">The session's materials, whose transparency it passes.</param>
    public WorldLineOfSight(Vector3d start, Vector3d end, IWorldMaterialTransparency materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        Start = start;
        End = end;
        Materials = materials;
    }

    /// <summary>Gets where it starts.</summary>
    public Vector3d Start { get; }

    /// <summary>Gets where it ends.</summary>
    public Vector3d End { get; }

    /// <summary>Gets the session's materials.</summary>
    public IWorldMaterialTransparency Materials { get; }

    /// <summary>Gets or sets which objects it may meet: its evaluate event, as each object type answers it; <see langword="null"/> meets all.</summary>
    public Func<object, bool>? Includes { get; set; }

    /// <summary>Gets or sets a value indicating whether it crossed any face, <c>isHit</c>.</summary>
    public bool IsHit { get; set; }

    /// <summary>Gets or sets how much of it gets through, <c>vTransparency</c>: one when nothing stops any of it, zero when blocked.</summary>
    public float Transparency { get; set; } = 1f;

    /// <summary>Gets or sets the nearest crossed face's level material row, <c>ptQMaterial</c>; <see langword="null"/> for none yet.</summary>
    public int? Material { get; set; }

    /// <summary>Gets or sets how far along the line the nearest crossed face is, <c>vT</c>: zero to one.</summary>
    public float T { get; set; } = 1f;

    /// <summary>Gets a value indicating whether the line is clear, as <c>World_LineOfSight</c> returns it: no face crossed.</summary>
    public bool IsClear => !IsHit;

    /// <summary>Gets a value indicating whether nothing of it gets through any more.</summary>
    public bool IsBlocked => IsHit && Transparency == 0f;
}
