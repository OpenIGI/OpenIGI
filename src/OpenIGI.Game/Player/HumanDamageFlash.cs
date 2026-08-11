using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The red wash the screen takes when the player is hit — the four corner alphas <c>sub_411300</c>
/// decays every tick and <c>HumanPlayer_DrawDamageFlash</c> paints as a full-screen quad.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps <b>four floats</b> on the player (<c>human+3532</c> in I.G.I. 1, the same block
/// I.G.I. 2 names <c>this[800..803]</c>), one per screen corner, and does exactly two things with them.
/// Each tick <c>sub_411300</c> walks the four and subtracts <c>0.022222223</c> — one forty-fifth — from
/// any that is non-zero, clamping at zero, so a corner set to full fades out over forty-five ticks, a
/// second and a half at thirty hertz. And each frame the draw (I.G.I. 2's
/// <c>HumanPlayer_DrawDamageFlash</c>, <c>0x4bb770</c>) paints a screen-filling red quad — RGB
/// <c>(255, 0, 0)</c> — whose four vertex alphas are those four floats, so the wash is brightest at the
/// corner the damage came from and fades corner-first.
/// </para>
/// <para>
/// <b>Both halves were read in I.G.I. 1.</b> The damage handler (<c>0x412440</c>) draws one value from
/// the engine random stream, maps it to <c>[0.4, 0.8)</c>, transforms the incoming round into view
/// space, and writes it straight into the four corners. The dominant view axis selects an edge; its
/// opposite edge receives half the alpha. A zero direction, which explosions use, writes all four
/// equally. This is deliberately an assignment rather than an accumulation: each hit replaces the
/// previous flash, exactly as the executable does.
/// </para>
/// </remarks>
public sealed class HumanDamageFlash
{
    /// <summary>How much a corner fades each tick — one forty-fifth, from <c>sub_411300</c>.</summary>
    /// <remarks>The literal <c>0.022222223</c> the decay subtracts, giving a full flash a 45-tick life.</remarks>
    public const float FadePerTick = 0.022222223f;

    /// <summary>The lowest alpha a retail damage hit chooses.</summary>
    public const float MinimumHitIntensity = 0.4f;

    /// <summary>The width of the retail hit-alpha range.</summary>
    public const float HitIntensityRange = 0.4f;

    /// <summary>The multiplier the retail direction mapper applies to the dimmer edge.</summary>
    public const float OppositeEdgeScale = 0.5f;

    // Top-left, top-right, bottom-right, bottom-left -- the order the draw feeds its quad's vertices.
    private float _topLeft;
    private float _topRight;
    private float _bottomRight;
    private float _bottomLeft;

    /// <summary>Gets the top-left corner's alpha, in <c>[0, 1]</c>.</summary>
    public float TopLeft => _topLeft;

    /// <summary>Gets the top-right corner's alpha, in <c>[0, 1]</c>.</summary>
    public float TopRight => _topRight;

    /// <summary>Gets the bottom-right corner's alpha, in <c>[0, 1]</c>.</summary>
    public float BottomRight => _bottomRight;

    /// <summary>Gets the bottom-left corner's alpha, in <c>[0, 1]</c>.</summary>
    public float BottomLeft => _bottomLeft;

    /// <summary>Gets a value indicating whether any corner is still lit, so the draw has something to paint.</summary>
    public bool IsActive => _topLeft > 0f || _topRight > 0f || _bottomRight > 0f || _bottomLeft > 0f;

    /// <summary>Applies the retail damage flash for an incoming hit.</summary>
    /// <param name="viewDirection">The incoming round's direction after the player's world-to-view transform.</param>
    /// <param name="randomUnit">One <see cref="IgiRandom.NextSingle"/> result from the engine's shared stream.</param>
    /// <remarks>
    /// <para>
    /// This is the directional write at <c>0x412622–0x412884</c>. It intentionally replaces all four
    /// alphas even when that makes a sustained burst dimmer; preserving a prior larger value is not what
    /// the retail executable does and is what turned automatic fire into an opaque strobe here.
    /// </para>
    /// <para>
    /// The caller must supply view-space coordinates. Keeping the world-to-view transform at the camera
    /// boundary means this gameplay type has no dependency on a desktop camera implementation.
    /// </para>
    /// </remarks>
    public void Hit(Vector3f viewDirection, float randomUnit)
    {
        float intensity = RetailIntensity(randomUnit);
        if (viewDirection == Vector3f.Zero)
        {
            SetCorners(intensity, intensity, intensity, intensity);
            return;
        }

        float dim = intensity * OppositeEdgeScale;
        float x = viewDirection.X;
        float y = viewDirection.Y;
        float z = viewDirection.Z;
        float absX = MathF.Abs(x);
        float absY = MathF.Abs(y);
        float absZ = MathF.Abs(z);

        // The branch order and ties mirror 0x4126BB–0x412884. The engine gives X priority only when it
        // is strictly dominant; otherwise Z wins whenever it exceeds either horizontal component, and
        // Y handles every remaining tie.
        if (absX > absY && absX > absZ)
        {
            SetCorners(x <= 0f ? dim : intensity, x <= 0f ? dim : intensity, x <= 0f ? intensity : dim, x <= 0f ? intensity : dim);
        }
        else if (absY < absX || absY < absZ)
        {
            SetCorners(z <= 0f ? intensity : dim, z <= 0f ? dim : intensity, z <= 0f ? dim : intensity, z <= 0f ? intensity : dim);
        }
        else if (y <= 0f)
        {
            SetCorners(intensity, intensity, intensity, intensity);
        }
        else
        {
            SetCorners(dim, intensity, intensity, dim);
        }
    }

    /// <summary>Converts a retail random draw into its damage-flash alpha.</summary>
    public static float RetailIntensity(float randomUnit) =>
        MinimumHitIntensity + (Math.Clamp(randomUnit, 0f, 1f) * HitIntensityRange);

    /// <summary>Fades every lit corner by one tick's worth, clamping at zero — <c>sub_411300</c>.</summary>
    public void Tick()
    {
        _topLeft = Fade(_topLeft);
        _topRight = Fade(_topRight);
        _bottomRight = Fade(_bottomRight);
        _bottomLeft = Fade(_bottomLeft);
    }

    private static float Fade(float corner) => corner > 0f ? Math.Max(0f, corner - FadePerTick) : 0f;

    private void SetCorners(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        _topLeft = topLeft;
        _topRight = topRight;
        _bottomRight = bottomRight;
        _bottomLeft = bottomLeft;
    }
}
