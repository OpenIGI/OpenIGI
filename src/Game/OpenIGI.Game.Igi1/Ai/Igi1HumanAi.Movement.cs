using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>The body the AI's frame drives: the turn and pitch it writes, the recoil it carries, and the move on the clip's root motion at the graph's height.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Gets the view's pitch above the horizon, in radians — <c>view+292</c>.</summary>
    /// <remarks>
    /// Steered through float 1 of the frame and turned by the body exactly as the yaw is (<c>0x464800</c>,
    /// clamped to a right angle). A walk and an idle level it; an aim, a look at an event and a patrol's
    /// LookAtNode point it at their target's own height.
    /// </remarks>
    public float ViewPitch { get; private set; }

    /// <summary>Gets the view's orientation — the body's heading tipped by <see cref="ViewPitch"/>.</summary>
    /// <remarks>What the eye snapshot (<c>0x489960</c>) tests its cones against: the view matrix at <c>+120</c>.</remarks>
    public Matrix3f ViewOrientation => Matrix3f.FromEngineEulerAngles(ViewPitch + RecoilPitch, 0f, Facing + RecoilYaw);

    /// <summary>Gets the orientation matrix a renderer should draw it with.</summary>
    public Matrix3f Orientation => Matrix3f.FromEngineEulerAngles(0f, 0f, Facing + RecoilYaw);

    /// <summary>Gets the yaw the weapon's kick still holds the body off its heading by -- the view's <c>+308</c>.</summary>
    /// <remarks>
    /// 0x4610C0 builds the body's matrix from the heading plus this (<c>Human+1288 = +1292 + view+308</c>), so the
    /// kick turns the body, its movement and its eye, and the AI's next turn measures against it.
    /// </remarks>
    public float RecoilYaw { get; private set; }

    /// <summary>Gets the pitch the weapon's kick still holds the view off -- the view's <c>+296</c>, added in 0x464800.</summary>
    public float RecoilPitch { get; private set; }

    /// <summary>Folds the last round's kick into the recoil the view carries -- 0x462E50, every tick, after the turn and move.</summary>
    /// <remarks>
    /// Each offset becomes (kick + offset) x 0.9, so a burst climbs and a pause lets it settle. The kick is the one the
    /// weapon returned on the PREVIOUS tick: the gun fires after this, and its kick waits in the fire message for the
    /// next tick's pass. So a soldier's AI, which runs before its body, measures its turn against the recoil as it was
    /// when it last fired. Fitted on mission 4's squad member 405 to a millionth of a radian over a whole burst.
    /// </remarks>
    public void SettleRecoil()
    {
        RecoilPitch = (float)((_pendingKick.Pitch + (double)RecoilPitch) * 0.89999998);
        RecoilYaw = (float)((_pendingKick.Yaw + (double)RecoilYaw) * 0.89999998);
        _pendingKick = default;
    }

    /// <summary>Keeps the kick the weapon returned for a round, for the next tick's <see cref="SettleRecoil"/>.</summary>
    /// <param name="pitchKick">The round's pitch kick (the fire message's <c>+32</c>).</param>
    /// <param name="yawKick">Its yaw kick (<c>+36</c>).</param>
    public void TakeWeaponKick(float pitchKick, float yawKick) => _pendingKick = (pitchKick, yawKick);

    // The kick of the round fired last tick, waiting for this tick's SettleRecoil.
    private (float Pitch, float Yaw) _pendingKick;

    /// <summary>
    /// Gives the soldier a ground query, so it walks on the terrain instead of interpolating between graph nodes.
    /// </summary>
    /// <param name="query">
    /// The query, or <see langword="null"/> to fall back to linear interpolation along the leg.
    /// </param>
    /// <remarks>
    /// <para>
    /// In the original the AI steers the soldier through the player's own input frame (<c>0x451540</c>),
    /// which makes the <c>Human</c> task run the same movement integrator as the player (<c>0x4614B0</c>),
    /// including its ground query (<c>0x462390</c>). So a soldier's height comes from the terrain (or
    /// whatever placed object it stands on), not from the graph.
    /// </para>
    /// <para>
    /// Probing the retail game confirms soldiers run the integrator with <c>wallEnable=0 objEnable=1
    /// humanEnable=0</c>: object collision and ground query but <b>no wall sweep</b>. The nav graph
    /// itself keeps soldiers on walkable paths; wall collision is not needed.
    /// </para>
    /// </remarks>
    public void SetGroundQuery(GroundQuery? query) => _ground = query;

    /// <summary>A patrol's LookAtNode — <c>0x452580</c>, the look-at entry into the turn.</summary>
    /// <returns><see langword="true"/> once facing the node within 3 degrees.</returns>
    /// <remarks>
    /// The look-at entry sets the turn clamp to 0.0259 rad of input per tick (<c>0x3CD4629B</c>), so a
    /// patrol turns to look at a node at about 24 degrees a second rather than snapping to it; the walk and
    /// the combat turns set it back to pi.
    /// </remarks>
    private bool FaceNode(int node)
    {
        if (_graph?.NodeAt(node) is not { } target)
        {
            return true;
        }

        _steering.YawClamp = LookAtTurnClamp;
        _steering.PitchClamp = LookAtTurnClamp;
        return SteerTowards(target.Position, AiPatrolRoute.LookAtToleranceRadians);
    }

    // 0x452580's clamp on both angles, in radians -- 0x3CD4629B.
    private const float LookAtTurnClamp = 0.025925925f;

    private bool IsFacing(Vector3d target, float tolerance)
    {
        double dx = target.X - Position.X;
        double dy = target.Y - Position.Y;
        if ((dx * dx) + (dy * dy) < 1.0)
        {
            return true;
        }

        return Math.Abs(Igi1AiGeometry.Wrap((float)Math.Atan2(-dx, dy) - Facing)) <= tolerance;
    }

    /// <summary>How far above the soldier's feet the ground probe starts — 0.5 m.</summary>
    private const double GroundProbeAboveUnits = 2048.0;

    /// <summary>How far below the soldier's feet the ground probe reaches — 2 m (the sliding step-down budget).</summary>
    private const double GroundProbeBelowUnits = 8192.0;

    /// <summary>
    /// The part of the body's update the AI's frame drives — <c>0x45D080</c>: turn (<c>0x4610C0</c>), then
    /// move on the clip's own root motion (<c>0x4614B0</c>) at the graph's height (<c>0x462390</c>).
    /// </summary>
    /// <remarks>
    /// <para>The turn is the one the player's own mouse goes through: see <see cref="HumanBodyTurn"/>.</para>
    /// <para>
    /// An AI's height is not found by a ray at all: <c>0x462390</c> interpolates it along the leg between the
    /// last two waypoints, which by construction lie on the ground the graph was authored against.
    /// </para>
    /// </remarks>
    private void StepBodyOrWait()
    {
        if (StepsBodyAfterClip)
        {
            _bodyStepPending = true;
            return;
        }

        StepBody();
    }

    /// <summary>Turns and moves the body by the delta of the clip that is playing after this tick's machine.</summary>
    /// <param name="rootMotion">The clip's delta along the body's forward axis, in units.</param>
    /// <param name="rootMotionLateral">Its delta along the body's right axis, in units.</param>
    public void StepBodyAfterClip(double rootMotion, double rootMotionLateral)
    {
        if (!_bodyStepPending)
        {
            return;
        }

        _bodyStepPending = false;
        _rootMotionThisTick = rootMotion;
        _rootLateralThisTick = rootMotionLateral;
        StepBody();
    }

    private void StepBody()
    {
        float turn = _turnInput;
        float pitch = _pitchInput;
        _turnInput = 0f;
        _pitchInput = 0f;
        LastAdvanceRm = _rootMotionThisTick;
        if (IsMounted)
        {
            // 0x4610C0's attached branch takes the emplacement's frame instead, and the body rides the gun.
            _bodyTurn.Reset();
            _viewPitchTurn.Reset();
            return;
        }

        Facing = Igi1AiGeometry.Wrap((float)(Facing + _bodyTurn.Step(turn)));
        ViewPitch = Math.Clamp((float)(ViewPitch + _viewPitchTurn.Step(pitch)), -MathF.PI / 2f, MathF.PI / 2f);

        // The clip's delta, rotated by the turned body into the world, every tick and whatever the AI asked
        // for: the walk moves a walker, a roll carries a rolling soldier sideways, a step back steps back.
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(_rootLateralThisTick, _rootMotionThisTick, 0.0), Facing + RecoilYaw);
        double x = Position.X + world.X;
        double y = Position.Y + world.Y;
        if (world.X != 0.0 || world.Y != 0.0)
        {
            LastMovedTo = new Vector3d(x, y, Position.Z);
            LastFacingDeg = Facing * 180.0 / Math.PI;
        }

        // 0x462390 grounds an AI on its graph every tick: along the leg it is walking, or, with nothing to
        // walk, at its current node's height.
        Position = new Vector3d(x, y, GroundHeight(x, y));
        StoodOnGraph = _graph is not null;
    }

    /// <summary>An AI's feet height — <c>0x462390</c>'s graph branch.</summary>
    /// <param name="x">The new X.</param>
    /// <param name="y">The new Y.</param>
    /// <returns>The height along the leg to the head waypoint, or the current node's with nothing to walk.</returns>
    private double GroundHeight(double x, double y)
    {
        if (_route.Count == 0)
        {
            return _graph?.NodeAt(CurrentNode)?.Position.Z ?? Position.Z;
        }

        Vector3d head = _route[0].Point;
        if (!_groundLegSet)
        {
            _groundLegFrom = Position;
            _groundLegTo = Position;
            _groundLegSet = true;
        }

        if (Vector3d.Distance(head, _groundLegTo) > 100.0)
        {
            _groundLegFrom = _groundLegTo;
            _groundLegTo = head;
        }

        double length = Vector3d.Distance(_groundLegTo, _groundLegFrom);
        double remaining = length > 0.0
            ? Math.Clamp(Vector3d.Distance(_groundLegTo, new Vector3d(x, y, Position.Z)) / length, 0.0, 1.0)
            : 0.0;
        return ((1.0 - remaining) * (_groundLegTo.Z - _groundLegFrom.Z)) + _groundLegFrom.Z;
    }

    /// <summary>Gets or sets where the soldier's view stands (the view's <c>+224</c>, copied to <c>HumanAI+56</c>).</summary>
    public Func<Vector3d>? ViewPositionProvider { get; set; }

    /// <summary>The turn-to-point entry — <c>0x4525B0</c>, which sets the clamps to pi first.</summary>
    /// <param name="target">The point to turn to.</param>
    /// <param name="tolerance">How close counts as facing it.</param>
    /// <param name="level">
    /// Keep the view level rather than pitching it at the point's height — the entry's last argument, set
    /// by the door wait and by the walker, which levels the view as it goes.
    /// </param>
    /// <returns>Whether the view was already within the tolerance, in yaw and pitch, before this tick's turn.</returns>
    private bool TurnTowards(Vector3d target, float tolerance, bool level = false)
    {
        _steering.YawClamp = MathF.PI;
        _steering.PitchClamp = MathF.PI;
        return SteerTowards(target, tolerance, level);
    }

    /// <summary>
    /// Writes the turn toward a point into the frame — <c>0x4522D0</c>: the yaw error, clamped, over pi.
    /// </summary>
    /// <returns>Whether both errors, measured before this tick's turn, are within the tolerance.</returns>
    /// <remarks>
    /// It writes the frame whether or not the view is already good enough; the body does the turning. The
    /// pitch is the elevation from the eye (float 1), or back to level when <paramref name="level"/> is set.
    /// </remarks>
    private bool SteerTowards(Vector3d target, float tolerance, bool level = false)
    {
        double dx = target.X - Position.X;
        double dy = target.Y - Position.Y;
        float yawError = 0f;
        if ((dx * dx) + (dy * dy) >= 1.0)
        {
            yawError = Igi1AiGeometry.Wrap((float)Math.Atan2(-dx, dy) - (Facing + RecoilYaw));
        }

        float yawLimit = _steering.YawClamp;
        _turnInput = Math.Clamp(yawError, -yawLimit, yawLimit) / MathF.PI;

        float elevation = 0f;
        if (!level)
        {
            // 0x4522D0 measures from the AI's frame (+56), which is the view's position: the origin raised 0.7 m, or
            // 0.35 m with the crouch byte set -- not the eye bone.
            Vector3d eye = ViewPositionProvider?.Invoke() ?? EyePosition();
            double ex = target.X - eye.X;
            double ey = target.Y - eye.Y;
            elevation = (float)Math.Atan2(target.Z - eye.Z, Math.Sqrt((ex * ex) + (ey * ey)));
        }

        float pitchError = elevation - (ViewPitch + RecoilPitch);
        float pitchLimit = _steering.PitchClamp;
        _pitchInput = Math.Clamp(pitchError, -pitchLimit, pitchLimit) / MathF.PI;
        return Math.Abs(yawError) <= tolerance && Math.Abs(pitchError) <= tolerance;
    }
}
