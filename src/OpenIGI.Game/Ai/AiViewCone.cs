namespace OpenIGI.Game.Ai;

/// <summary>
/// One of an AI's two view cones — an elliptical cone with a vertical half-angle, a horizontal half-angle and
/// two lengths, the longer of which applies once the AI is alerted.
/// </summary>
/// <param name="Alpha">The <b>vertical</b> half-angle in radians. <c>AIFunction_SetViewAlpha</c> writes it.</param>
/// <param name="Gamma">The <b>horizontal</b> half-angle in radians. <c>AIFunction_SetViewGamma</c> writes it.</param>
/// <param name="Length">The range in engine units while unalerted.</param>
/// <param name="AlarmLength">The range in engine units once alerted.</param>
/// <remarks>
/// <para>
/// <b><see cref="Alpha"/> is the vertical angle and <see cref="Gamma"/> the horizontal</b>, which is the
/// opposite of what the names suggest and of what <c>gameplay-systems.md</c> §5 recorded. Three independent
/// arguments agree on it:
/// </para>
/// <list type="number">
/// <item>
/// <c>0x4502F0</c> compares <see cref="Alpha"/> against an angle measured with the local <b>X</b> component
/// zeroed, and <see cref="Gamma"/> against one with <b>Z</b> zeroed.
/// </item>
/// <item>
/// The eye orientation is built by <c>0x4B38E0</c> — the <c>Real32x9</c> Euler composer M6 pinned as
/// <c>Rz(γ)·Ry(β)·Rx(α)</c> — and <c>0x464BD0</c> passes <b>zero for the middle angle</b>. So the frame is
/// yaw about world Z over pitch about X, whose columns work out to X = right (always horizontal), Y = forward
/// (which the <c>local.y &lt; 0</c> early-out independently confirms) and Z = up. Zeroing X therefore leaves
/// the vertical plane and zeroing Z the horizontal one.
/// </item>
/// <item>
/// The constructor's <b>secondary</b> cone is wider in <see cref="Gamma"/> than the primary and barely half
/// as long — peripheral vision, which is wider <em>horizontally</em>.
/// </item>
/// </list>
/// <para>
/// <c>AIFunction_SetViewLength</c> takes <b>metres</b> and stores <c>× 4096</c>; the angle setters take
/// <b>degrees</b> and store <c>× 0.017453292</c>, the same single-precision constant M9 found the weapon zoom
/// using. Both length setters also <b>raise the alarm length to match</b> if the normal length is set above
/// it, so an alarmed AI can never see less far than a calm one.
/// </para>
/// </remarks>
public readonly record struct AiViewCone(double Alpha, double Gamma, double Length, double AlarmLength)
{
    /// <summary>Engine units per metre, for the length setters.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The degrees-to-radians factor the angle setters use, in single precision as the original has it.</summary>
    public const float DegreesToRadians = 0.017453292f;

    /// <summary>
    /// The primary cone <c>0x44F060</c> starts an AI with — about ±57.3° vertically, ±59.5° horizontally and
    /// 26 m, with no extra reach when alerted.
    /// </summary>
    public static AiViewCone DefaultPrimary => new(
        BitConverter.Int64BitsToDouble(0x3FEFFF7B60000000L),
        BitConverter.Int64BitsToDouble(0x3FF0A0DE60000000L),
        BitConverter.Int64BitsToDouble(0x40FA000000000000L),
        BitConverter.Int64BitsToDouble(0x40FA000000000000L));

    /// <summary>
    /// The secondary cone <c>0x44F060</c> starts an AI with — <b>wider</b> horizontally at about ±63° but only
    /// 12.5 m. Peripheral vision, and <b>no shipped script ever changes it</b>.
    /// </summary>
    public static AiViewCone DefaultSecondary => new(
        BitConverter.Int64BitsToDouble(0x3FEFFF7B60000000L),
        BitConverter.Int64BitsToDouble(0x3FF1969860000000L),
        BitConverter.Int64BitsToDouble(0x40E9000000000000L),
        BitConverter.Int64BitsToDouble(0x40E9000000000000L));

    /// <summary>Gets the range that applies at a given alert state.</summary>
    /// <param name="alerted">Whether the AI's alert latch is set.</param>
    /// <returns>The range in engine units.</returns>
    public double RangeAt(bool alerted) => alerted ? AlarmLength : Length;

    /// <summary>Applies <c>AIFunction_SetViewLength</c>'s conversion and its clamp.</summary>
    /// <param name="metres">The range in metres.</param>
    /// <returns>The cone with its length set, and its alarm length raised if it was shorter.</returns>
    public AiViewCone WithLengthInMetres(double metres)
    {
        double length = metres * UnitsPerMetre;
        return this with { Length = length, AlarmLength = Math.Max(length, AlarmLength) };
    }

    /// <summary>Applies <c>AIFunction_SetAlarmViewLength</c>'s conversion.</summary>
    /// <param name="metres">The alerted range in metres.</param>
    /// <returns>The cone with its alarm length set.</returns>
    /// <remarks>
    /// This setter has <b>no clamp</b> — only the normal-length setter does. So a script that sets the alarm
    /// length below the normal one gets exactly that, and an alerted AI sees less far than a calm one.
    /// </remarks>
    public AiViewCone WithAlarmLengthInMetres(double metres) =>
        this with { AlarmLength = metres * UnitsPerMetre };

    /// <summary>Applies <c>AIFunction_SetViewAlpha</c>'s conversion.</summary>
    /// <param name="degrees">The vertical half-angle in degrees.</param>
    /// <returns>The cone with its vertical half-angle set.</returns>
    public AiViewCone WithAlphaInDegrees(double degrees) =>
        this with { Alpha = degrees * DegreesToRadians };

    /// <summary>Applies <c>AIFunction_SetViewGamma</c>'s conversion.</summary>
    /// <param name="degrees">The horizontal half-angle in degrees.</param>
    /// <returns>The cone with its horizontal half-angle set.</returns>
    public AiViewCone WithGammaInDegrees(double degrees) =>
        this with { Gamma = degrees * DegreesToRadians };
}
