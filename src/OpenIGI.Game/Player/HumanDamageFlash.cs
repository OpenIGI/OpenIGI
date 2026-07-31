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
/// <b>Both halves were read in I.G.I. 1</b> (<c>sub_411300</c> is the decay, byte-identical to I.G.I. 2's
/// <c>0x4a0590</c>); the draw's shape is taken from I.G.I. 2's named function, which shares the engine.
/// The one thing not yet pinned is the <b>set</b>: the damage-receipt path writes the four corners from
/// the direction the round came, and that mapping is not read — so this raises all four together, a
/// uniform flash, which is what the player perceives as "the screen goes red." The directional corner
/// weighting is recorded as the open refinement.
/// </para>
/// </remarks>
public sealed class HumanDamageFlash
{
    /// <summary>How much a corner fades each tick — one forty-fifth, from <c>sub_411300</c>.</summary>
    /// <remarks>The literal <c>0.022222223</c> the decay subtracts, giving a full flash a 45-tick life.</remarks>
    public const float FadePerTick = 0.022222223f;

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

    /// <summary>
    /// Raises the flash for a hit — every corner to <paramref name="intensity"/>, never dimming what a
    /// more recent hit already set.
    /// </summary>
    /// <param name="intensity">How red, in <c>[0, 1]</c>; <c>1</c> is a full wash. Values are clamped.</param>
    /// <remarks>
    /// The corners are raised together rather than by direction — see the type's remarks. Taking the
    /// larger of the current and the new value is what lets a burst hold the screen solid red instead of
    /// each round re-starting a fade from a dimmer point.
    /// </remarks>
    public void Hit(float intensity = 1f)
    {
        float clamped = Math.Clamp(intensity, 0f, 1f);
        _topLeft = Math.Max(_topLeft, clamped);
        _topRight = Math.Max(_topRight, clamped);
        _bottomRight = Math.Max(_bottomRight, clamped);
        _bottomLeft = Math.Max(_bottomLeft, clamped);
    }

    /// <summary>Fades every lit corner by one tick's worth, clamping at zero — <c>sub_411300</c>.</summary>
    public void Tick()
    {
        _topLeft = Fade(_topLeft);
        _topRight = Fade(_topRight);
        _bottomRight = Fade(_bottomRight);
        _bottomLeft = Fade(_bottomLeft);
    }

    private static float Fade(float corner) => corner > 0f ? Math.Max(0f, corner - FadePerTick) : 0f;
}
