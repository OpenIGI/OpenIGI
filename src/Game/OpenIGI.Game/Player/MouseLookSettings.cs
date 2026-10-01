using OpenIGI.Game.Config;

namespace OpenIGI.Game.Player;

/// <summary>
/// How far a mouse movement turns the view, and which way its vertical axis runs — the look half of
/// <see cref="PlayerProfile"/>, resolved once into the numbers the cameras actually multiply by.
/// </summary>
/// <remarks>
/// <para>
/// The rate is per <b>mouse count</b>, not per screen pixel. The window locks the pointer with GLFW's
/// raw motion mode (<c>SilkGameWindow.ApplyCursorMode</c>), so what arrives is the sensor's own counts
/// with the desktop's pointer ballistics bypassed. One count is therefore the smallest aim adjustment
/// the player can physically make, and <see cref="RadiansPerCount"/> is the size of that step.
/// </para>
/// <para>
/// <b>Why the rate is not the one this project shipped with.</b> The look used to be a hardcoded
/// <c>0.0035</c> rad/count — 0.2 degrees, so about 1800 counts for a full turn, which is a 5.7 cm 360
/// on an 800 DPI mouse. At the engine's ninety-degree horizontal field of view
/// (<see cref="HumanView.DefaultTanX"/>) that makes the smallest possible crosshair
/// movement roughly three pixels on a 1080p display, and there is no way to aim between them. It reads
/// as small movements being dropped: below one count nothing happens at all, and at one count the view
/// jumps further than the thing being aimed at is wide.
/// </para>
/// <para>
/// <see cref="PlayerProfile.MouseSensitivity"/> — the retail 0-to-1 slider, which round-tripped through
/// the save file but which nothing in the look path read — now chooses the rate <em>geometrically</em>
/// between <see cref="MinRadiansPerCount"/> and <see cref="MaxRadiansPerCount"/>, so equal slider steps
/// are equal ratios rather than equal absolute rates and the low end stays usable. The top of the slider
/// is exactly the old hardcoded value, so nothing that liked the previous feel has lost it; the retail
/// default of <c>0.4</c> lands near a 34 cm full turn at 800 DPI, which is where modern shooters sit.
/// </para>
/// </remarks>
/// <param name="RadiansPerCount">Radians of view rotation per mouse count.</param>
/// <param name="InvertPitch">Whether pushing the mouse forward looks down rather than up.</param>
public readonly record struct MouseLookSettings(float RadiansPerCount, bool InvertPitch)
{
    /// <summary>The rate at the top of the slider — the value the look was hardcoded to.</summary>
    public const float MaxRadiansPerCount = 0.0035f;

    /// <summary>The rate at the bottom of the slider, a twentieth of the top.</summary>
    /// <remarks>
    /// About 0.01 degrees a count, or a metre of desk for a full turn at 800 DPI. Deliberately slower
    /// than anyone will hold it for long — the point of the bottom of a slider is that it is past what
    /// is comfortable, not at it.
    /// </remarks>
    public const float MinRadiansPerCount = 0.000175f;

    /// <summary>What <c>Config_ResetToDefaults</c> writes for a fresh profile.</summary>
    /// <remarks>See <see cref="PlayerProfile.MouseSensitivity"/>; kept here so the fallback used when
    /// there is no profile yet is the same number a profile would have carried.</remarks>
    public const float DefaultSensitivity = 0.4f;

    /// <summary>The settings a player with no profile loaded looks with.</summary>
    public static MouseLookSettings Default { get; } =
        FromSensitivity(DefaultSensitivity, invertPitch: false);

    /// <summary>Resolves a slider position into a look rate.</summary>
    /// <param name="sensitivity">The profile's 0-to-1 slider; values outside that are clamped.</param>
    /// <param name="invertPitch">Whether the vertical axis is inverted.</param>
    /// <returns>The settings.</returns>
    public static MouseLookSettings FromSensitivity(float sensitivity, bool invertPitch)
    {
        float t = Math.Clamp(sensitivity, 0f, 1f);
        float rate = MinRadiansPerCount
            * MathF.Pow(MaxRadiansPerCount / MinRadiansPerCount, t);
        return new MouseLookSettings(rate, invertPitch);
    }

    /// <summary>Resolves a profile's look settings.</summary>
    /// <param name="profile">The active profile, or <see langword="null"/> before one is loaded.</param>
    /// <returns>The settings, or <see cref="Default"/> when there is no profile.</returns>
    public static MouseLookSettings FromProfile(PlayerProfile? profile) => profile is null
        ? Default
        : FromSensitivity(profile.MouseSensitivity, profile.InvertMouse);

    /// <summary>How much desk a full turn takes, in centimetres.</summary>
    /// <param name="dotsPerInch">The mouse's resolution; 800 is the usual reference.</param>
    /// <returns>The distance in centimetres.</returns>
    /// <remarks>
    /// The number a player actually chooses when they move the slider, and the one every sensitivity
    /// guide is written in — a slider position on its own says nothing. The settings screen reads out
    /// this rather than the raw value, so what the control promises is what the hand has to do.
    /// </remarks>
    public double TurnDistanceCentimetres(int dotsPerInch = 800) =>
        (2.0 * Math.PI) / RadiansPerCount / dotsPerInch * 2.54;

    /// <summary>
    /// How much to slow the look while an optic has narrowed the field of view, so aiming stays as
    /// controllable zoomed in as it is unzoomed.
    /// </summary>
    /// <param name="currentTanX">The view's current horizontal half-FOV tangent.</param>
    /// <returns>A multiplier in <c>(0, 1]</c>: 1 unzoomed, and the zoom ratio while scoped.</returns>
    /// <remarks>
    /// <b>Divergence, deliberate.</b> Retail turns the head at one rate whatever the Dragunov's scope is
    /// doing, so a single mouse count that was a fine correction unzoomed sweeps the crosshair across a
    /// whole magnified field at the tightest zoom — the aim gets harder the more you zoom in. Scaling the
    /// look by the same factor the field of view narrowed by (<paramref name="currentTanX"/> over the
    /// unzoomed <see cref="HumanView.DefaultTanX"/>) holds the on-screen crosshair speed constant across
    /// the whole zoom range, which is what a modern scope does. Unzoomed the ratio is 1 and the look is
    /// untouched, so ordinary play is unchanged.
    /// </remarks>
    public static float ZoomSensitivityScale(float currentTanX) =>
        Math.Clamp(currentTanX / HumanView.DefaultTanX, 0f, 1f);

    /// <summary>The yaw a frame's horizontal motion asks for, in radians.</summary>
    /// <param name="mouseDeltaX">The horizontal motion, in mouse counts.</param>
    /// <returns>The change in yaw. Mouse right turns right.</returns>
    public float YawDelta(float mouseDeltaX) => mouseDeltaX * RadiansPerCount;

    /// <summary>The pitch a frame's vertical motion asks for, in radians.</summary>
    /// <param name="mouseDeltaY">The vertical motion, in mouse counts, positive downward.</param>
    /// <returns>
    /// The change in pitch, already signed for <see cref="InvertPitch"/>: it is <em>added</em> to the
    /// current pitch, so mouse up looks up unless the profile says otherwise.
    /// </returns>
    public float PitchDelta(float mouseDeltaY) =>
        (InvertPitch ? mouseDeltaY : -mouseDeltaY) * RadiansPerCount;
}
