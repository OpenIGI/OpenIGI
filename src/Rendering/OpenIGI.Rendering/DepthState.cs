namespace OpenIGI.Rendering;

/// <summary>How a draw interacts with the depth buffer.</summary>
/// <remarks>
/// The original toggles the two D3D render states <c>ZENABLE</c> and <c>ZWRITEENABLE</c> independently;
/// these are the three combinations it actually uses. The 2D pipeline runs with
/// <see cref="Disabled"/>, opaque world geometry with <see cref="TestAndWrite"/>, and translucent
/// geometry with <see cref="TestOnly"/> so that it sorts against the world without occluding itself.
/// </remarks>
public enum DepthState
{
    /// <summary>No depth test and no depth write.</summary>
    Disabled,

    /// <summary>Depth is tested and written — the default for opaque geometry.</summary>
    TestAndWrite,

    /// <summary>Depth is tested but not written.</summary>
    TestOnly,
}
