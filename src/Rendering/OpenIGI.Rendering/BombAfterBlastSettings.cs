using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// A bomb's after-blast as one frame draws it over the lit scene (<c>Post/BombAfterBlast.slang</c>): its front sweeping out
/// over the world, streaking the picture where it passes, and the dust it leaves once it has passed the eye.
/// </summary>
/// <param name="BombView">Where the bomb is, in the frame's view space, metres.</param>
/// <param name="FrontMetres">How far its front has swelled from it, metres.</param>
/// <param name="Strength">How strongly the front shows where it passes, nought to one; it fades out over the pass's last seconds.</param>
/// <param name="Haze">How much of the picture the dust may take at most, nought to one.</param>
/// <param name="Taps">How many reads each streaked pixel takes.</param>
public readonly record struct BombAfterBlastSettings(
    Vector3f BombView = default,
    float FrontMetres = 0f,
    float Strength = 0f,
    float Haze = 0f,
    int Taps = 16)
{
    /// <summary>The leading edge's thickness, metres.</summary>
    public const float EdgeMetres = 1f;

    /// <summary>How far behind the front its wake trails, metres.</summary>
    public const float WakeMetres = 76f;

    /// <summary>How far, at most, a pixel is streaked, as a share of the screen.</summary>
    public const float StreakShare = 0.022f;

    /// <summary>Inside this distance from the eye the front shows nothing; it comes in by <see cref="NearFullMetres"/>.</summary>
    public const float NearStartMetres = 0.8f;

    /// <summary>Where the front shows fully, past its near fade, metres.</summary>
    public const float NearFullMetres = 2f;

    /// <summary>Where it starts to fade out with distance, metres; it is gone by <see cref="FarGoneMetres"/>.</summary>
    public const float FarFullMetres = 127f;

    /// <summary>Where it is gone with distance, metres; the dust is thickest once the front is this far past the eye.</summary>
    public const float FarGoneMetres = 152f;

    /// <summary>The dust's colour, as it is seen.</summary>
    public static readonly Vector3f HazeColour = new(0.31f, 0.30f, 0.29f);

    /// <summary>Gets a value indicating whether there is anything to draw.</summary>
    public bool IsActive => Strength > 0f || Haze > 0f;
}
