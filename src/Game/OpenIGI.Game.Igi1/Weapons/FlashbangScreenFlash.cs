using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Weapons;

/// <summary>The first-person whiteout written by the retail flashbang player handler.</summary>
/// <remarks>
/// <para>
/// <c>0x00413090</c> writes two independent player fields: one for the hardcoded 25 metre visible
/// range and one for the grenade's close radius. They fade independently in the player update; the
/// renderer presents the stronger remaining value as a uniform white quad.
/// </para>
/// <para>
/// This class deliberately has no world-trace input. The native handler receives the flash event and
/// compares its distance to the player's eye; it does not run a line-of-sight query.
/// </para>
/// </remarks>
public sealed class FlashbangScreenFlash
{
    /// <summary>The visible-field decay per 30 Hz tick from retail <c>0x411240</c>.</summary>
    /// <remarks><c>0x3BDA740E</c> in the native image, about 150 ticks for a full-strength field.</remarks>
    public const float VisibleFadePerTick = 0.006666667f;

    /// <summary>The close-field decay while its strength is above one, from retail <c>0x411350</c>.</summary>
    public const float CloseFadeAboveOnePerTick = 0.016666668f;

    /// <summary>The close-field decay at or below one, from retail <c>0x411350</c>.</summary>
    public const float CloseFadeAtOrBelowOnePerTick = 0.011111112f;

    private float _visible;
    private float _close;

    /// <summary>Gets the remaining visible-range strength.</summary>
    public float VisibleStrength => _visible;

    /// <summary>Gets the remaining close-range strength.</summary>
    public float CloseStrength => _close;

    /// <summary>Gets the stronger remaining whiteout strength, in the native 0–2 range.</summary>
    public float Strength => MathF.Max(_visible, _close);

    /// <summary>Gets whether a whiteout quad still has a non-zero alpha.</summary>
    public bool IsActive => Strength > 0f;

    /// <summary>Applies a flash event without allowing a weaker event to shorten an existing whiteout.</summary>
    /// <param name="visible">The 25 metre effect returned by <see cref="Flashbang.Effect(float, float)"/>.</param>
    /// <param name="close">The close-radius effect returned by <see cref="Flashbang.Effect(float, float)"/>.</param>
    public void Apply(float visible, float close)
    {
        _visible = MathF.Max(_visible, MathF.Max(0f, visible));
        _close = MathF.Max(_close, MathF.Max(0f, close));
    }

    /// <summary>Advances both native flash fields by one fixed simulation tick.</summary>
    public void Tick()
    {
        _visible = Fade(_visible, VisibleFadePerTick);
        _close = Fade(
            _close,
            _close > 1f ? CloseFadeAboveOnePerTick : CloseFadeAtOrBelowOnePerTick);
    }

    /// <summary>Clears the whiteout when a mission or player is reset.</summary>
    public void Clear()
    {
        _visible = 0f;
        _close = 0f;
    }

    private static float Fade(float strength, float amount) =>
        strength > amount + 0.000001f
            ? strength - amount
            : 0f;
}
