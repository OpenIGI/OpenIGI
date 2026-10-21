using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The dip the camera makes on landing — installed by the fall, played out by the camera.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two functions own this between them.</b> The airborne integrator at <c>0x408460</c> ends a fall
/// by calling <c>0x4659B0(camera, amplitude)</c>, which is the whole of the arming — one store of the
/// amplitude to <c>camera+452</c> and one zeroing of the phase at <c>camera+456</c>. The camera update
/// at <c>0x464BD0</c> then plays it:
/// </para>
/// <code>
/// if (camera[452] != 0.0) {
///     s = sin(camera[456]);
///     z          += camera[452] * s;          // the camera's world-space height
///     rigOffset   = camera[452] * 0.075 * s;  // the first-person rig follows at 7.5%
///     camera[456] += 0.20943952;
///     if (camera[456] >= 3.1415927) camera[452] = 0.0;
/// }
/// </code>
/// <para>
/// So it is <b>not</b> a damped spring, and it does not oscillate: it is a single half period of a
/// sine. The phase walks 0 to π in steps of <c>0.20943952</c> — π/15, so fifteen ticks, half a second
/// at the 30 Hz tick. The amplitude is negative (<see cref="HumanFallDamage.MaximumViewKickUnits"/> at
/// the clamp, a quarter of a metre), which puts the trough at the midpoint and brings the view back to
/// where it started without ever rising above it.
/// </para>
/// <para>
/// The sine is sampled <em>before</em> the phase steps, so a landing's first tick shows <c>sin(0)</c>
/// — nothing — and its last shows <c>sin(14π/15)</c> rather than <c>sin(π)</c>; the amplitude is
/// dropped on the step that reaches π. Landing again mid-dip restarts the phase from zero, because
/// that is all the two stores in <c>0x4659B0</c> can do.
/// </para>
/// <para>
/// The camera offset is applied in <b>world</b> Z, not along the view direction: <c>0x464BD0</c> adds
/// it to the same accumulator that carries <c>camera+420</c>, which lands on the position's Z
/// component directly while the X and Y offsets beside it are rotated into the camera's basis first.
/// </para>
/// </remarks>
public sealed class HumanLandingViewKick
{
    /// <summary>How far the phase advances each tick — <c>0.20943952</c>, which is π/15.</summary>
    public const float PhaseStepRadians = 0.20943952f;

    /// <summary>The phase at which the dip is over and the amplitude is dropped.</summary>
    public const float EndPhaseRadians = 3.1415927f;

    /// <summary>The share of the camera's drop the first-person rig takes — <c>0.075</c>.</summary>
    public const float ViewModelShare = 0.075f;

    /// <summary>How many ticks a dip runs for: fifteen, which is half a second at 30 Hz.</summary>
    public const int DurationTicks = 15;

    private float _amplitudeUnits;
    private float _phaseRadians;

    /// <summary>Gets this tick's camera displacement, in world units. Negative is downward.</summary>
    public float OffsetUnits { get; private set; }

    /// <summary>Gets the share of that displacement the first-person rig takes, in world units.</summary>
    public float ViewModelOffsetUnits => OffsetUnits * ViewModelShare;

    /// <summary>Gets whether a dip is still playing.</summary>
    public bool IsActive => _amplitudeUnits != 0f;

    /// <summary>Arms the dip, as <c>0x4659B0</c> does — store the amplitude, restart the phase.</summary>
    /// <param name="amplitudeUnits">
    /// <see cref="HumanFallImpact.ViewKickUnits"/>, which is negative so that the view drops. Zero
    /// disarms, matching the original's own <c>!= 0.0</c> gate.
    /// </param>
    public void Install(float amplitudeUnits)
    {
        _amplitudeUnits = amplitudeUnits;
        _phaseRadians = 0f;
        OffsetUnits = 0f;
    }

    /// <summary>Steps the dip one tick, where <c>0x464BD0</c> steps it.</summary>
    public void Advance()
    {
        if (_amplitudeUnits == 0f)
        {
            OffsetUnits = 0f;
            return;
        }

        // Sampled before the step, so the dip opens at zero and the arming tick shows no jump.
        OffsetUnits = _amplitudeUnits * MathF.Sin(_phaseRadians);

        _phaseRadians += PhaseStepRadians;
        if (_phaseRadians >= EndPhaseRadians)
        {
            _amplitudeUnits = 0f;
        }
    }

    /// <summary>Drops a dip in progress, for a respawn or a teleport that starts the view level.</summary>
    public void Reset()
    {
        _amplitudeUnits = 0f;
        _phaseRadians = 0f;
        OffsetUnits = 0f;
    }
}
