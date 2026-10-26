using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A human's mark on the ground, both games' <c>Shadow</c> (<c>Shadow_New</c>, <c>0x4E0CC0</c>; I.G.I. 2's <c>shadow.c</c>, made by
/// <c>Human_CreateHandler</c>): a square lying on the ground under the body, drawn where the ground was found, fainter the further
/// the body is above it. Project I.G.I. shows it under every body; I.G.I. 2, whose shadow volumes a standing
/// body casts instead (<c>Human_GetShadowInfoHandler</c>), only as the pool of blood a body leaves once it has come to rest.
/// </summary>
/// <param name="maxDistance">How far above the ground a body may be before its mark has faded as far as it goes, in world units.</param>
public sealed class HumanShadow(float maxDistance)
{
    /// <summary>The mark a body casts, <c>shadow1.tex</c>.</summary>
    public const string ShadowTexture = "shadow1.tex";

    /// <summary>The pool a resting body leaves, <c>bloodpool.tex</c>.</summary>
    public const string PoolTexture = "bloodpool.tex";

    /// <summary>The half-width a mark grows to, in world units: <c>Shadow_New</c>'s radius.</summary>
    public const float Radius = 1843.199951171875f;

    /// <summary>Project I.G.I.'s fade height: the 4,096,000 units <c>Human_Create</c> hands <c>Shadow_New</c>.</summary>
    public const float Igi1MaxDistance = 4096000f;

    /// <summary>I.G.I. 2's fade height.</summary>
    public const float Igi2MaxDistance = 40960f;

    /// <summary>Gets how far above the ground a body may be before its mark has faded as far as it goes, in world units.</summary>
    public float MaxDistance { get; } = maxDistance;

    /// <summary>How far above the ground the square is drawn, in world units.</summary>
    public const float Hover = 10f;

    /// <summary>What a pool's size grows by each tick, a hundred-and-fiftieth of its radius: five seconds to spread.</summary>
    public const float PoolGrowth = Radius * 0.006666666828095913f;

    /// <summary>Gets the texture the mark draws with.</summary>
    public string Texture { get; private set; } = ShadowTexture;

    /// <summary>Gets a value indicating whether the mark is drawn.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets where the ground was found under the body.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the ground's normal there, of unit length.</summary>
    public Vector3f Normal { get; private set; } = new(0f, 0f, 1f);

    /// <summary>Gets how far the body is above that ground, in world units.</summary>
    public float Distance { get; private set; }

    /// <summary>Gets a value indicating whether the ground was found this tick.</summary>
    public bool IsPositionValid { get; private set; }

    /// <summary>Gets the mark's half-width now, in world units.</summary>
    public float Size { get; private set; } = Radius;

    /// <summary>Gets the light the mark is coloured by, the body's light where it lay; <see langword="null"/> for the plain mark.</summary>
    public float? Light { get; private set; }

    /// <summary>
    /// Gets the colour's strength, <c>Shadow_MagicObjEnterHandler</c>: four fifths of how near the body is to the ground over
    /// <see cref="MaxDistance"/>, held between three tenths and one.
    /// </summary>
    public float Strength => Math.Clamp((1f - (Distance / MaxDistance)) * 0.8f, 0.3f, 1f);

    /// <summary>Notes where the ground was found under the body, the end of <c>Human_GroundCollision</c>.</summary>
    /// <param name="position">Where the ground was struck.</param>
    /// <param name="normal">The ground's normal there.</param>
    /// <param name="distance">How far the body is above it.</param>
    public void Place(Vector3d position, Vector3f normal, float distance)
    {
        Position = position;
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        Normal = length > 0f ? new Vector3f(normal.X / length, normal.Y / length, normal.Z / length) : normal;
        Distance = distance;
        IsPositionValid = true;
    }

    /// <summary>Notes that no ground was found under the body.</summary>
    public void Lose()
    {
        Position = default;
        IsPositionValid = false;
    }

    /// <summary>One tick, <c>Shadow_RunHandler</c>: a mark short of its radius grows toward it.</summary>
    public void Run()
    {
        if (Size < Radius)
        {
            Size += Growth;
        }
    }

    /// <summary>Gets what a growing mark gains each tick.</summary>
    public float Growth { get; private set; }

    /// <summary>
    /// Turns the mark into the body's pool of blood and shows it: the pool texture, coloured by the body's light; spreading from
    /// nothing over five seconds, or already spread for a body read back dead from a save.
    /// </summary>
    /// <param name="light">The body's light.</param>
    /// <param name="spread">Whether it spreads from a point rather than lying already spread.</param>
    public void BecomePool(float light, bool spread)
    {
        Texture = PoolTexture;
        Light = light;
        IsEnabled = true;
        if (spread)
        {
            Size = 0f;
            Growth = PoolGrowth;
        }
    }
}
