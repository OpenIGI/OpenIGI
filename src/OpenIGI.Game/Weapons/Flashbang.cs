namespace OpenIGI.Game.Weapons;

/// <summary>
/// What a flashbang does, from <c>0x47E9A0</c> and the player's handler for it (<c>0x413090</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A flashbang only affects the player.</b> The blast broadcasts to every human, but of the classes
/// that register for the message only <c>HumanPlayer</c> supplies a handler — the four AI soldier
/// classes all register the empty one. So a flashbang is a hazard to the player and nothing else, and
/// throwing one into a room full of guards does not blind them.
/// </para>
/// <para>
/// The effect is two independent tests against two different distances, and both can apply at once.
/// </para>
/// </remarks>
public static class Flashbang
{
    /// <summary>How far the flash itself is seen, regardless of the grenade's radius.</summary>
    /// <remarks>
    /// <c>102400</c> world units — <b>25 metres</b>, and hardcoded. It has nothing to do with the
    /// grenade's own explosion radius, so the flash reaches five times further than the blast does at
    /// the default tuning.
    /// </remarks>
    public const float VisibleRangeUnits = 102400f;

    /// <summary>The strength written when the player is inside <see cref="VisibleRangeUnits"/>.</summary>
    public const float VisibleStrength = 1f;

    /// <summary>The strength written when the player is inside the flash's own radius.</summary>
    /// <remarks>Twice <see cref="VisibleStrength"/>, and written to a different field.</remarks>
    public const float CloseStrength = 2f;

    /// <summary>Whether the player sees the flash at all.</summary>
    /// <param name="distanceInUnits">
    /// Distance from the flash to the player's <i>eye</i>, not to its origin — the handler asks for the
    /// view position rather than the body's.
    /// </param>
    /// <returns><see langword="true"/> when the flash registers.</returns>
    /// <remarks>The test is strictly less than, so a player at exactly 25 metres is unaffected.</remarks>
    public static bool IsVisible(float distanceInUnits) => distanceInUnits < VisibleRangeUnits;

    /// <summary>Whether the player is inside the flash's own radius.</summary>
    /// <param name="distanceInUnits">Distance from the flash to the player's eye.</param>
    /// <param name="radiusInUnits">The radius the grenade passed, in world units.</param>
    /// <returns><see langword="true"/> when the stronger effect applies.</returns>
    /// <remarks>
    /// The original writes this as <c>distance − radius &lt; 0</c> rather than <c>distance &lt;
    /// radius</c>. The two agree for every finite input, so this keeps the readable form.
    /// </remarks>
    public static bool IsClose(float distanceInUnits, float radiusInUnits) =>
        distanceInUnits - radiusInUnits < 0f;

    /// <summary>Both effects a flash has on the player at a distance.</summary>
    /// <param name="distanceInUnits">Distance from the flash to the player's eye.</param>
    /// <param name="radiusInUnits">The radius the grenade passed.</param>
    /// <returns>
    /// The two strengths, either of which is zero when its test fails. They are independent: at the
    /// default tuning a player between five and twenty-five metres gets the first and not the second.
    /// </returns>
    public static (float Visible, float Close) Effect(float distanceInUnits, float radiusInUnits) =>
        (IsVisible(distanceInUnits) ? VisibleStrength : 0f,
         IsClose(distanceInUnits, radiusInUnits) ? CloseStrength : 0f);
}
