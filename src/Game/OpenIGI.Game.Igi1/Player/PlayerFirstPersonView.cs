using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's first-person view: the mouse look, the rig bound to the body, the camera the scene is
/// drawn from, the landing dip and hit shake folded into both, and the tick pair the presentation draws
/// between.
/// </summary>
/// <remarks>
/// Looking is per frame and movement is per tick. Everything here that the tick steps — the dip, the
/// shake, the rig's pose — is captured into a pair, so a frame drawn between two ticks blends them while
/// the look itself stays the frame's own.
/// </remarks>
internal sealed class PlayerFirstPersonView
{
    private readonly PlayerBody _body;
    private readonly HumanLandingViewKick _landingKick = new();
    private readonly HumanViewShake _viewShake = new();
    private readonly PlayerViewInterpolator _pair;
    private bool _wasCapturing;

    /// <summary>Initializes the view over one body.</summary>
    /// <param name="body">The body whose eye the view is.</param>
    internal PlayerFirstPersonView(PlayerBody body)
    {
        _body = body;
        _pair = new PlayerViewInterpolator(_viewShake);
    }

    /// <summary>Gets the first-person rig the held weapon hangs off, once one has been given.</summary>
    internal HumanView? Rig { get; private set; }

    /// <summary>Gets the tick pair the presentation interpolates between.</summary>
    internal PlayerViewInterpolator Pair => _pair;

    /// <summary>Gets whether the cursor should be held while the view is being turned.</summary>
    internal bool IsCapturingMouse { get; private set; }

    /// <summary>Gets or sets how far the mouse turns the view, and which way its vertical axis runs.</summary>
    /// <remarks>
    /// The host resolves this from the active profile every frame, so a change made in the options menu
    /// takes effect on the next one. See <see cref="MouseLookSettings"/> for why the rate is what it is.
    /// </remarks>
    internal MouseLookSettings LookSettings { get; set; } = MouseLookSettings.Default;

    /// <summary>Gets where the held weapon's model goes, in engine units, or <see langword="null"/>.</summary>
    internal BoneAttachment? HeldWeapon => Rig is null ? null : Rig.HeldWeapon;

    /// <summary>Gets where the held weapon's magazine goes, in engine units, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Retail's <c>TASKTYPE_GUNCLIP</c>, <c>TASKTYPE_CABARREL</c> and <c>TASKTYPE_GUNX2</c> children all
    /// ask the first-person human for attachment 2, which is palette bone 52 (<c>0x4601A0</c>), rather
    /// than reusing the gun attachment at bone 3.
    /// </remarks>
    internal BoneAttachment? HeldWeaponSecondary => Rig is null ? null : Rig.HeldWeaponSecondary;

    /// <summary>Gets where the held weapon's slide or bolt goes, in engine units, or <see langword="null"/>.</summary>
    /// <remarks>
    /// <c>TASKTYPE_GUNMOVINGPART</c> and <c>TASKTYPE_CACLIP</c> ask for attachment 3 — palette bone 55
    /// (<c>0x460210</c>).
    /// </remarks>
    internal BoneAttachment? HeldWeaponTertiary => Rig is null ? null : Rig.HeldWeaponTertiary;

    /// <summary>Gets the dip the last landing installed in the view.</summary>
    /// <remarks>
    /// Armed by <see cref="InstallLandingViewKick"/> off the body's fall impact and stepped beside the
    /// weapon sway, which is where <c>0x464BD0</c> steps both.
    /// </remarks>
    internal HumanLandingViewKick LandingKick => _landingKick;

    /// <summary>Gets the shake the last hit put in the view.</summary>
    internal HumanViewShake ViewShake => _viewShake;

    /// <summary>The landing dip as a world-space displacement, in units — <c>0x464BD0</c> adds it to Z.</summary>
    private Vector3d LandingKickOffsetUnits => new(0.0, 0.0, _landingKick.OffsetUnits);

    /// <summary>The shake's eye displacement in world units: along the view's horizontal right, and up.</summary>
    private Vector3d ViewShakeOffsetUnits => new(
        _viewShake.RightUnits * Math.Cos(_body.Yaw),
        -_viewShake.RightUnits * Math.Sin(_body.Yaw),
        _viewShake.UpUnits);

    /// <summary>Attaches a new first-person rig built over skeleton 5's set.</summary>
    /// <param name="set">The set for skeleton 5.</param>
    internal void UseRig(BoneAnimationSet set) => Rig = new HumanView(set);

    /// <summary>Arms the view shake, as the player's hit handlers do through <c>0x465970</c>.</summary>
    /// <param name="positionUnits">The full eye displacement, in units.</param>
    /// <param name="angleRadians">The full angle, in radians.</param>
    /// <param name="ticks">How many ticks it fades over.</param>
    internal void InstallViewShake(float positionUnits, float angleRadians, int ticks) =>
        _viewShake.Install(positionUnits, angleRadians, ticks);

    /// <summary>Arms the landing dip, as <c>0x408460</c> does through <c>0x4659B0</c>.</summary>
    /// <param name="amplitudeUnits">
    /// <see cref="HumanFallImpact.ViewKickUnits"/> from the landing the body just resolved.
    /// </param>
    internal void InstallLandingViewKick(float amplitudeUnits) => _landingKick.Install(amplitudeUnits);

    /// <summary>Steps the hit shake, then the landing dip, as <c>0x464BD0</c> does.</summary>
    /// <param name="random">The draw the shake jitters with.</param>
    internal void AdvanceKicks(Func<double> random)
    {
        _viewShake.Advance(random);
        _landingKick.Advance();
    }

    /// <summary>Puts the dip and the shake down, for a teleport that leaves the ground they belong to.</summary>
    internal void ResetKicks()
    {
        _landingKick.Reset();
        _viewShake.Reset();
    }

    /// <summary>Restores both first-person FOV tangents and drops the pair's zoom.</summary>
    internal void ResetZoom()
    {
        if (Rig is { } view)
        {
            view.TanX = HumanView.DefaultTanX;
            view.ViewModelTanX = HumanView.DefaultViewModelTanX;
        }

        _pair.ResetZoom();
    }

    /// <summary>Turns the look by this frame's mouse motion.</summary>
    /// <param name="mouse">This frame's mouse.</param>
    /// <param name="mounted">The stationary gun the player is on, or <see langword="null"/>.</param>
    /// <returns>The gun the look steered, or <see langword="null"/> when it turned the head or settled.</returns>
    internal AiStationaryGun? Turn(MouseState mouse, AiStationaryGun? mounted)
    {
        // A first-person view looks wherever the mouse goes - no button held. That is the difference
        // between standing in a level and inspecting one, and holding right mouse to turn is a scene
        // view's idiom that FreeCamera keeps and this must not.
        IsCapturingMouse = true;

        // The first frame is still skipped: capturing the cursor warps it, and that warp arrives as a
        // movement the player never made.
        bool settling = !_wasCapturing;
        _wasCapturing = true;

        if (!settling)
        {
            // Mouse right turns right. The camera's forward is (sin yaw, cos yaw), so turning right -
            // from +Y toward +X - means yaw increases. Both deltas are in mouse COUNTS, not pixels: the
            // cursor is locked in raw motion mode, so no pointer ballistics stand between the sensor and
            // this line. PitchDelta carries the profile's invert, so it is added rather than subtracted.
            //
            // The look is slowed in step with any optical zoom: at the Dragunov's tightest scope a mouse
            // count moves the crosshair across a magnified field, so without this the aim gets harder the
            // more you zoom in. The scale is the zoom ratio and is 1 (no change) whenever the view is not
            // zoomed. See MouseLookSettings.ZoomSensitivityScale.
            // On a stand the look turns the gun, not the head: 0x474190 adds the occupant's two look
            // deltas to the gun's own elevation and traverse, and state 33 locks the view's pitch
            // (0x464700). Mouse right turns right, which is a clockwise traverse.
            if (mounted is { } gun)
            {
                gun.Steer(
                    LookSettings.PitchDelta(mouse.DeltaY),
                    -LookSettings.YawDelta(mouse.DeltaX));
                return gun;
            }

            float zoomLook = Rig is { } zoomView
                ? MouseLookSettings.ZoomSensitivityScale(zoomView.TanX)
                : 1f;
            _body.Yaw += LookSettings.YawDelta(mouse.DeltaX) * zoomLook;
            _body.Pitch = Math.Clamp(
                _body.Pitch + (LookSettings.PitchDelta(mouse.DeltaY) * zoomLook),
                -PlayerBody.PitchLimit,
                PlayerBody.PitchLimit);
        }

        return null;
    }

    /// <summary>Lets the pointer go and drops the presentation's tick pairs, for a frozen frame.</summary>
    /// <remarks>
    /// <para>
    /// What the pause menu needs of the player, and only that: the body is not moved, nothing is placed
    /// again, and the mission is left exactly where it stood.
    /// </para>
    /// <para>
    /// The mouse is released because <see cref="PlayerCamera.Look"/> claims the cursor unconditionally — a first-person
    /// view looks wherever the mouse goes — so a menu drawn over it would be unreachable. Clearing the
    /// capture also re-arms the settling frame, which is what stops the warp that re-capturing produces on
    /// resume from arriving as a movement the player never made.
    /// </para>
    /// <para>
    /// The tick pairs go with it, because a pair kept across the menu is a pair whose two halves are on
    /// either side of it: the first frame after resuming would draw the view somewhere between the tick
    /// before the pause and the tick after it. Dropping them makes the next tick build a fresh pair from
    /// where the player still is, at the cost of one tick drawn unsmoothed.
    /// </para>
    /// </remarks>
    internal void HoldStill()
    {
        ReleaseLook();
        _pair.Forget();
    }

    /// <summary>Gives the pointer up without disturbing the presentation, and re-arms the settling frame.</summary>
    /// <remarks>
    /// <para>
    /// For everything that takes the mouse away from the look but leaves the player standing where they
    /// were: the death camera's orbit, the third-person interaction camera, the map computer. Each of
    /// those drops the cursor lock, and dropping the lock makes the backend re-centre its virtual pointer
    /// — so the first motion after the lock comes back is a jump that has nothing to do with the player's
    /// hand. <see cref="PlayerCamera.Look"/> skips exactly one frame after this, which is what absorbs it.
    /// </para>
    /// <para>
    /// Unlike <see cref="HoldStill"/> the rig's tick pair is kept, because the player is still being
    /// drawn: dropping it here would cost a visibly unsmoothed frame every time the map computer closed.
    /// </para>
    /// </remarks>
    internal void ReleaseLook()
    {
        IsCapturingMouse = false;
        _wasCapturing = false;
    }

    // Binds the rig to the body. The original does more here (0x464BD0 eases the aim and pushes the eye
    // out of walls with a multi-ray cast); the view-model's separate forward clearance probe is updated
    // immediately before this final binding. What is transcribed here is the attachment's eye transform,
    // built from the aim angles with 0x4B38E0's own Euler order, and the fact that 0x4D38C0 poses the
    // rig with a zero root and the object's own 3x3 above it.
    internal void Sync(float aimPitch, PlayerWeapons? weapons)
    {
        if (Rig is not { } view)
        {
            return;
        }

        view.Eye = _body.Eye;
        view.EyeOrientation = Matrix3f.FromEngineEulerAngles(
            aimPitch + _viewShake.Pitch, _viewShake.Roll, -_body.Yaw + _viewShake.Yaw);

        // 0x464BD0 composes the object's orientation as the eye's TIMES the weapon sway, dotting the
        // eye's rows with the sway's columns. The sway is the two angles a weapon change tips the rig
        // by, and it is the identity whenever nothing is being put away. The object's POSITION is not
        // the eye -- HumanView.Position derives it, because the rig's own root-motion track is where
        // each weapon's hold offset lives.
        view.Orientation = weapons is null
            ? view.EyeOrientation
            : view.EyeOrientation * weapons.Sway.ToMatrix();
    }

    /// <summary>Gets the eye the scene is drawn from, in the renderer's metres.</summary>
    /// <param name="aimPitch">The look's pitch with the recoil on top.</param>
    /// <param name="mounted">The stationary gun the player is on, or <see langword="null"/>.</param>
    /// <remarks>On a stationary gun it is the gun's own sight, turned and kicked with the barrel.</remarks>
    internal Camera3D Camera(float aimPitch, AiStationaryGun? mounted) => mounted is { } gun
        ? GunSight(gun)
        : new(
            WorldScene.ToMetres(_body.Eye + LandingKickOffsetUnits + ViewShakeOffsetUnits),
            Shaken(_body.Yaw, aimPitch, _viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw));

    // The camera 0x45FCD0 builds in mode 4: the gun's point and orientation used outright. The eye's
    // right, forward and up are the orientation's columns 0, 1 and 2, as they are for the walking eye.
    private static Camera3D GunSight(AiStationaryGun gun)
    {
        Matrix3f m = gun.GunOrientation;
        return new Camera3D(
            WorldScene.ToMetres(gun.SightPosition),
            Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32)));
    }

    /// <summary>The camera's look with a shake folded in — the engine's gamma and the roll about forward.</summary>
    private static Matrix3f Shaken(float yaw, float pitch, float shakePitch, float shakeRoll, float shakeYaw)
    {
        // The engine's eye is Z(-yaw) Y(roll) X(pitch); the shake adds to its gamma, which is minus the yaw.
        Matrix3f look = Camera3D.LookRotation(yaw - shakeYaw, pitch + shakePitch);
        if (shakeRoll == 0f)
        {
            return look;
        }

        float c = MathF.Cos(shakeRoll);
        float sn = MathF.Sin(shakeRoll);
        Vector3f right = new(look.M11, look.M12, look.M13);
        Vector3f up = new(look.M21, look.M22, look.M23);
        Vector3f forward = new(look.M31, look.M32, look.M33);
        return Matrix3f.FromRows((right * c) + (up * sn), (up * c) - (right * sn), forward);
    }

    /// <summary>Records the rig's per-tick halves, keeping the tick before's.</summary>
    /// <param name="weapons">The weapon machine whose sway and kick the rig carries, or <see langword="null"/>.</param>
    internal void Capture(PlayerWeapons? weapons)
    {
        if (Rig is not { } view)
        {
            _pair.HasPair = false;
            return;
        }

        // The dip belongs to the camera the frame is drawn from, so it is folded in here rather than at
        // the eye the body reports: the blend between the two ticks then carries it for free, and the
        // listener and the rig stay on the same head they always were.
        _pair.Capture(
            view,
            view.Eye + LandingKickOffsetUnits + ViewShakeOffsetUnits,
            weapons?.Sway.Pitch ?? 0f,
            weapons?.Sway.Yaw ?? 0f,
            weapons?.RecoilPitch ?? 0f);
    }

    /// <summary>The camera's orientation for a frame between two ticks — the kick glides, the look is now.</summary>
    /// <param name="aimPitch">The look's un-smoothed pitch with the recoil on top.</param>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// The rig takes the same blended pitch through <see cref="PlayerViewInterpolator.SmoothedRig"/>, so the weapon never
    /// shears against the view mid-kick.
    /// </remarks>
    internal Matrix3f SmoothedLookRotation(float aimPitch, float alpha)
    {
        (float pitch, float roll, float yaw) = _pair.SmoothedShake(alpha);
        return Shaken(_body.Yaw, _pair.SmoothedAimPitch(_body.Pitch, aimPitch, alpha), pitch, roll, yaw);
    }

    /// <summary>Where the ear is for a frame between two ticks, in metres.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>The blended eye position in metres.</returns>
    /// <remarks>
    /// The eye rather than <see cref="PlayerCamera.Position"/>, which is the feet: the listener belongs in the head,
    /// and blended from the same tick pair the view uses so a source does not step across the stereo
    /// image at 30 Hz under a camera that glides.
    /// </remarks>
    internal Vector3d SmoothedEyeMetres(float alpha) => WorldScene.ToMetres(_pair.SmoothedEye(_body.Eye, alpha));

    /// <inheritdoc cref="PlayerViewInterpolator.SmoothedSightZoom"/>
    internal SightZoomFrame SmoothedSightZoom(float alpha) => _pair.SmoothedSightZoom(Rig, alpha);

    /// <inheritdoc cref="PlayerViewInterpolator.SmoothedRig"/>
    internal (
        Vector3d OriginUnits,
        Matrix3f Orientation,
        BoneAttachment? HeldWeapon,
        BoneAttachment? Magazine,
        BoneAttachment? MovingPart) SmoothedRig(float aimPitch, float alpha) =>
        _pair.SmoothedRig(Rig, _body.Yaw, _body.Pitch, aimPitch, alpha);
}
