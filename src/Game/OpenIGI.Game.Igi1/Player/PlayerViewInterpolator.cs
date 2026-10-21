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
/// The first-person rig's last two tick states, and the frames an uncapped presentation draws between them.
/// </summary>
/// <remarks>
/// Captured at the end of the player's tick — what the presentation draws between when r_smooth is on. All
/// of it is kept in bone/eye space WITHOUT the look folded in, so the per-frame mouse orientation composes on
/// top and the weapon stays welded to the view however fast the frames come.
/// </remarks>
internal sealed class PlayerViewInterpolator
{
    private readonly HumanViewShake _viewShake;
    private Vector3d _prevTickEye;
    private Vector3d _tickEye;
    private double _prevTickWallPullback;
    private double _tickWallPullback;
    private Vector3f _prevTickRootMotion;
    private Vector3f _tickRootMotion;
    private ViewBonePair _weaponBone;
    private ViewBonePair _magazineBone;
    private ViewBonePair _movingPartBone;
    private float _prevTickSwayPitch;
    private float _tickSwayPitch;
    private float _prevTickSwayYaw;
    private float _tickSwayYaw;
    private float _prevTickRecoilPitch;
    private float _tickRecoilPitch;
    private float _prevTickTanX = HumanView.DefaultTanX;
    private float _tickTanX = HumanView.DefaultTanX;
    private float _prevTickViewModelTanX = HumanView.DefaultViewModelTanX;
    private float _tickViewModelTanX = HumanView.DefaultViewModelTanX;
    private (float Pitch, float Roll, float Yaw) _prevTickShake;
    private (float Pitch, float Roll, float Yaw) _tickShake;

    /// <summary>Initializes the interpolator over the view's hit shake.</summary>
    /// <param name="viewShake">The shake whose angles are folded into the camera and the rig.</param>
    internal PlayerViewInterpolator(HumanViewShake viewShake)
    {
        _viewShake = viewShake;
    }

    /// <summary>Gets or sets whether the rig has two tick states for the presentation to draw between.</summary>
    internal bool HasPair { get; set; }

    /// <summary>Drops the pair and the three bones' pairs, so the next tick starts a fresh one.</summary>
    internal void Forget()
    {
        HasPair = false;
        _weaponBone.Forget();
        _magazineBone.Forget();
        _movingPartBone.Forget();
    }

    /// <summary>Puts both FOV tangent pairs back at their defaults and drops the pair.</summary>
    internal void ResetZoom()
    {
        _prevTickTanX = HumanView.DefaultTanX;
        _tickTanX = HumanView.DefaultTanX;
        _prevTickViewModelTanX = HumanView.DefaultViewModelTanX;
        _tickViewModelTanX = HumanView.DefaultViewModelTanX;
        HasPair = false;
    }

    /// <summary>Records the rig's per-tick halves, keeping the tick before's.</summary>
    /// <param name="view">The first-person rig.</param>
    /// <param name="eye">The eye the frame is drawn from, with the landing dip and the shake folded in.</param>
    /// <param name="swayPitch">The weapon sway's pitch this tick.</param>
    /// <param name="swayYaw">The weapon sway's yaw this tick.</param>
    /// <param name="recoilPitch">The view kick this tick.</param>
    internal void Capture(HumanView view, Vector3d eye, float swayPitch, float swayYaw, float recoilPitch)
    {
        _prevTickEye = HasPair ? _tickEye : eye;
        _tickEye = eye;

        // The shake's angles step on the tick too, and sit in both the camera and the rig.
        _prevTickShake = HasPair ? _tickShake : (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);
        _tickShake = (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);

        double wallPullback = view.WallPullbackInUnits;
        _prevTickWallPullback = HasPair ? _tickWallPullback : wallPullback;
        _tickWallPullback = wallPullback;

        Vector3f rootMotion = view.RootMotion;
        _prevTickRootMotion = HasPair ? _tickRootMotion : rootMotion;
        _tickRootMotion = rootMotion;

        // The sway IS the weapon-change animation — no clip plays; two angles ease over eleven ticks
        // (see HumanViewSway). They step on the tick, so they interpolate like everything else here.
        _prevTickSwayPitch = HasPair ? _tickSwayPitch : swayPitch;
        _prevTickSwayYaw = HasPair ? _tickSwayYaw : swayYaw;
        _tickSwayPitch = swayPitch;
        _tickSwayYaw = swayYaw;

        // The view kick accumulates and decays on the tick (ResolveFire ran just above), and it sits
        // in both the camera and the rig's eye orientation -- so both take the same blended value, or
        // the weapon would shear against the view with every shot.
        _prevTickRecoilPitch = HasPair ? _tickRecoilPitch : recoilPitch;
        _tickRecoilPitch = recoilPitch;

        // GunMP5SD changes both FOV tangents on the fixed tick. Preserve that exact retail state
        // transition, but keep the pair so an uncapped presentation can draw the space between the
        // two values instead of exposing the original game's 30 Hz steps.
        _prevTickTanX = HasPair ? _tickTanX : view.TanX;
        _prevTickViewModelTanX = HasPair ? _tickViewModelTanX : view.ViewModelTanX;
        _tickTanX = view.TanX;
        _tickViewModelTanX = view.ViewModelTanX;

        // The weapon bone out of the UNROTATED pose — the identity-root one — so the look can be
        // applied per frame at draw. Equivalent by the same argument the third-person draw leans on:
        // the root rotation only ever left-multiplies every bone.
        BonePose unrotated = view.Bones.Pose();
        _weaponBone.Capture(unrotated, HumanAttachPoints.FirstPersonWeapon);

        // The magazine and the moving part ride bones of their own, and a reload clip moves all three
        // independently -- so all three need the pair an uncapped presentation interpolates between, or
        // a slide would step at 30 Hz against a gun body that glides.
        _magazineBone.Capture(unrotated, HumanAttachPoints.FirstPersonSecondary);
        _movingPartBone.Capture(unrotated, HumanAttachPoints.FirstPersonTertiary);

        HasPair = true;
    }

    /// <summary>The drawn pitch with the view kick blended between its last two tick values.</summary>
    /// <param name="bodyPitch">The mouse-driven pitch, this frame's own.</param>
    /// <param name="aimPitch">The un-smoothed aim pitch, for a rig without a pair.</param>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// The mouse half (<see cref="PlayerBody.Pitch"/>) is per frame already; only the recoil
    /// accumulates and decays on the tick. At <paramref name="alpha"/> 1 this is
    /// <see cref="PlayerCamera.AimPitch"/> exactly.
    /// </remarks>
    internal float SmoothedAimPitch(float bodyPitch, float aimPitch, float alpha) => HasPair
        ? bodyPitch + _prevTickRecoilPitch + ((_tickRecoilPitch - _prevTickRecoilPitch) * alpha)
        : aimPitch;

    // The shake's three angles blended between their last two tick values.
    internal (float Pitch, float Roll, float Yaw) SmoothedShake(float alpha) => HasPair
        ? (Interpolation.LerpAngle(_prevTickShake.Pitch, _tickShake.Pitch, alpha),
            Interpolation.LerpAngle(_prevTickShake.Roll, _tickShake.Roll, alpha),
            Interpolation.LerpAngle(_prevTickShake.Yaw, _tickShake.Yaw, alpha))
        : (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);

    /// <summary>The eye blended between its last two tick values, in world units.</summary>
    /// <param name="current">The body's eye, for a rig without a pair.</param>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    internal Vector3d SmoothedEye(Vector3d current, float alpha) =>
        HasPair ? Vector3d.Lerp(_prevTickEye, _tickEye, alpha) : current;

    /// <summary>Gets the two FOV tangents blended for an uncapped presentation frame.</summary>
    /// <param name="view">The first-person rig, or <see langword="null"/> before one is given.</param>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// This changes only presentation. <see cref="Mp5SightZoom"/> still advances by the original
    /// fixed-tick multipliers, so the sight reaches and leaves its limits in exactly the same number
    /// of simulation ticks at every render frame rate.
    /// </remarks>
    internal SightZoomFrame SmoothedSightZoom(HumanView? view, float alpha)
    {
        if (view is null)
        {
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        if (!HasPair)
        {
            return new SightZoomFrame(view.TanX, view.ViewModelTanX);
        }

        float t = Math.Clamp(alpha, 0f, 1f);
        return new SightZoomFrame(
            _prevTickTanX + ((_tickTanX - _prevTickTanX) * t),
            _prevTickViewModelTanX + ((_tickViewModelTanX - _prevTickViewModelTanX) * t));
    }

    /// <summary>The rig's transform and held-weapon attachments for a frame between two ticks.</summary>
    /// <param name="view">The first-person rig, or <see langword="null"/> before one is given.</param>
    /// <param name="bodyYaw">The look's yaw, this frame's own.</param>
    /// <param name="bodyPitch">The mouse-driven pitch, this frame's own.</param>
    /// <param name="aimPitch">The un-smoothed aim pitch.</param>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>
    /// The rig origin in engine units, its orientation, and the three weapon attachments — the body's
    /// bone, the magazine's and the moving part's — when those bones exist.
    /// </returns>
    /// <remarks>
    /// The tick-stepped halves — the eye the body carried, the wall pullback, the clip's hold offset,
    /// the weapon bone, and the sway angles that ARE the weapon-change animation — are interpolated between the pair;
    /// the LOOK is this frame's own, because the mouse already runs per frame and the arms must stay
    /// welded to it. At <paramref name="alpha"/> 1 this is the rig's un-smoothed transform exactly.
    /// </remarks>
    internal (
        Vector3d OriginUnits,
        Matrix3f Orientation,
        BoneAttachment? HeldWeapon,
        BoneAttachment? Magazine,
        BoneAttachment? MovingPart) SmoothedRig(
            HumanView? view,
            float bodyYaw,
            float bodyPitch,
            float aimPitch,
            float alpha)
    {
        if (view is null || !HasPair)
        {
            return (
                view?.Position ?? default,
                view?.Orientation ?? Matrix3f.Identity,
                view?.HeldWeapon,
                view?.HeldWeaponSecondary,
                view?.HeldWeaponTertiary);
        }

        // The same composition SyncView commits — eye orientation times sway — with the tick-stepped
        // angles blended: the sway pair (the weapon-change animation) and the recoil kick inside the
        // aim pitch. The mouse-driven part of the pitch and the yaw are this frame's own.
        (float shakePitch, float shakeRoll, float shakeYaw) = SmoothedShake(alpha);
        Matrix3f eyeLook = Matrix3f.FromEngineEulerAngles(
            SmoothedAimPitch(bodyPitch, aimPitch, alpha) + shakePitch, shakeRoll, -bodyYaw + shakeYaw);
        Matrix3f look = eyeLook * Matrix3f.FromEngineEulerAngles(
                Interpolation.LerpAngle(_prevTickSwayPitch, _tickSwayPitch, alpha),
                0f,
                Interpolation.LerpAngle(_prevTickSwayYaw, _tickSwayYaw, alpha));

        Vector3d eye = Vector3d.Lerp(_prevTickEye, _tickEye, alpha);

        // M · v — the palette's own convention — is the transpose of the row-vector Transform.
        Vector3f held = look.Transposed().Transform(
            Vector3f.Lerp(_prevTickRootMotion, _tickRootMotion, alpha));
        double wallPullback = _prevTickWallPullback + ((_tickWallPullback - _prevTickWallPullback) * alpha);
        Vector3d origin = new(
            eye.X + held.X + (eyeLook.M12 * wallPullback),
            eye.Y + held.Y + (eyeLook.M22 * wallPullback),
            eye.Z + held.Z + (eyeLook.M32 * wallPullback));

        return (
            origin,
            look,
            _weaponBone.Smoothed(origin, look, alpha),
            _magazineBone.Smoothed(origin, look, alpha),
            _movingPartBone.Smoothed(origin, look, alpha));
    }

    /// <summary>One view-rig attachment bone over the last two ticks, ready to interpolate.</summary>
    /// <remarks>
    /// The bone is taken from the UNROTATED pose — the identity-root one — so the look can be applied
    /// per frame at draw. Equivalent by the argument the third-person draw leans on: the root rotation
    /// only ever left-multiplies every bone.
    /// </remarks>
    private struct ViewBonePair()
    {
        private Vector3f _previousPosition;
        private Vector3f _position;
        private Matrix3f _previousRotation = Matrix3f.Identity;
        private Matrix3f _rotation = Matrix3f.Identity;
        private bool _hasPair;

        /// <summary>Reads this tick's value, keeping the previous one, when the rig has the bone.</summary>
        /// <param name="pose">The rig's unrotated pose.</param>
        /// <param name="bone">The bone index, from <see cref="HumanAttachPoints"/>.</param>
        public void Capture(BonePose pose, int bone)
        {
            if (bone >= pose.BoneCount)
            {
                _hasPair = false;
                return;
            }

            Vector3f position = pose.Positions[bone];
            Matrix3f rotation = pose.Rotations[bone];
            _previousPosition = _hasPair ? _position : position;
            _previousRotation = _hasPair ? _rotation : rotation;
            _position = position;
            _rotation = rotation;
            _hasPair = true;
        }

        /// <summary>Drops the pair, so the next capture starts a fresh one from where the rig now is.</summary>
        public void Forget() => _hasPair = false;

        /// <summary>Resolves the attachment for a frame between the two ticks.</summary>
        /// <param name="origin">The rig origin the frame drew at, in engine units.</param>
        /// <param name="look">The rig's orientation this frame.</param>
        /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
        /// <returns>The attachment, or <see langword="null"/> when the rig has no such bone.</returns>
        public readonly BoneAttachment? Smoothed(Vector3d origin, Matrix3f look, float alpha)
        {
            if (!_hasPair)
            {
                return null;
            }

            // M . v -- the palette's own convention -- is the transpose of the row-vector Transform.
            Vector3f bone = look.Transposed().Transform(
                Vector3f.Lerp(_previousPosition, _position, alpha));
            return new BoneAttachment(
                new Vector3d(origin.X + bone.X, origin.Y + bone.Y, origin.Z + bone.Z),
                look * Interpolation.InterpolateRotation(_previousRotation, _rotation, alpha));
        }
    }
}
