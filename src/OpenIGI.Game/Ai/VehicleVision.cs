using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>
/// A firing vehicle's eye: the detection record a Car keeps at <c>+2704</c> and a Heli at <c>+2240</c>, which its guns
/// and its CarAI read.
/// </summary>
/// <remarks>
/// <para>
/// It is the same record every AI viewer uses (<c>0x4896C0</c>), run by the same sweep (<c>0x489750</c>): one tick in
/// four on the record's phase, the cone test every second sweep and the sight ray every third while the target is in
/// the cone (<c>0x4898D0</c>, intervals 4/4 and 8/4). What a vehicle sees is therefore last sweep's answer, not a fresh
/// look every tick.
/// </para>
/// <para>
/// The cone comes from the vehicle's authored "ViewCone" parameters as <c>0x42F530</c>/<c>0x432950</c> store them:
/// alpha and gamma in radians and <b>not halved</b> -- <c>0x489960</c> compares each absolute angle against the whole
/// value -- and the length in metres times 4096. There is no secondary cone (<c>+112</c> = 0).
/// </para>
/// <para>
/// The phase comes from the global round-robin counter <c>0x5C8B08</c>, advanced by every record's initialisation. On
/// mission 7 the four camera lenses take 0 to 3 at construction and the vehicles then take theirs in tree order at
/// post-load (Heli 356 → 0, Car 355 → 1, 354 → 2, 357 → 3, 361 → 0, Heli 353 → 1, Car 352 → 2, 351 → 3), probed.
/// </para>
/// </remarks>
public sealed class VehicleVision
{
    /// <summary>A car's eye stands this far over its origin (<c>0x42E870</c> adds 8192 to Z).</summary>
    public const double CarEyeHeightUnits = 8192.0;

    private readonly AiDetectionSlot _slot = new();
    private AiDetectionSchedule _schedule;
    private bool _canFire;

    /// <summary>Initializes a new instance of the <see cref="VehicleVision"/> class.</summary>
    /// <param name="phase">Which of the four ticks the record sweeps on.</param>
    public VehicleVision(int phase) => _schedule = new AiDetectionSchedule(phase & 3, coneInterval: 1, rayInterval: 2);

    /// <summary>Gets the record's sweep phase.</summary>
    public int Phase => _schedule.Phase;

    /// <summary>Gets a value indicating whether the vehicle's "Can fire" held on its last update (<c>+2333</c>).</summary>
    public bool CanFire => _canFire;

    /// <summary>Gets a value indicating whether the target's slot is registered and marked visible (<c>+41</c>).</summary>
    public bool Sees => _canFire && _slot.IsRegistered && _slot.IsVisible;

    /// <summary>The vision half of the vehicle's update: <c>0x42E870</c> for a car, the <c>+1960</c> block of <c>0x4316D0</c>.</summary>
    /// <param name="tick">The flow tick the sweep phase is compared against.</param>
    /// <param name="canFire">This tick's "Can fire".</param>
    /// <param name="eye">The eye snapshot.</param>
    /// <param name="target">The target's detection point, or <see langword="null"/> when there is no live target.</param>
    /// <param name="lineOfSight">The sight ray from the eye to a point (<c>0x489B20</c>).</param>
    /// <param name="nextPhase">The global counter's next value, for a record reinitialised when "Can fire" drops.</param>
    public void Tick(
        int tick,
        bool canFire,
        in AiEyeSnapshot eye,
        Vector3d? target,
        Func<Vector3d, bool> lineOfSight,
        Func<int> nextPhase)
    {
        ArgumentNullException.ThrowIfNull(lineOfSight);
        ArgumentNullException.ThrowIfNull(nextPhase);
        if (!canFire)
        {
            // Dropping "Can fire" clears the record (0x489B90) and initialises it again, taking a new phase.
            if (_canFire)
            {
                _slot.Unregister();
                _schedule = new AiDetectionSchedule(nextPhase() & 3, coneInterval: 1, rayInterval: 2);
            }

            _canFire = false;
            return;
        }

        _canFire = true;
        if (!_schedule.SweepsOn(tick))
        {
            return;
        }

        if (target is not { } point)
        {
            // A human that stops qualifying -- dead, or gone from the world -- leaves its slot (0x4894E0).
            _slot.Unregister();
            return;
        }

        if (!_slot.IsRegistered)
        {
            _slot.Register(tick);
        }

        AiEyeSnapshot snapshot = eye;
        _schedule.Sweep(
            _slot,
            () => snapshot.IsVisible(point),
            () => lineOfSight(point),
            (point - snapshot.Position).Length);
    }
}
