using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;

namespace OpenIGI.Game.Igi1.Player;

/// <content>
/// Modern aim's part of the player (<see cref="ModernAimCVars"/>): the sights on the alternate-fire button, the M203 on its own,
/// and what the sights do to the view, the round and the step. <b>Not retail.</b> With modern aim off none of this changes
/// anything: the frame goes to the original's machines as it came, and every scale is one.
/// </content>
public sealed partial class PlayerCamera
{
    private readonly AimDownSights _ads = new();
    private ModernAimOptions _modernAim = ModernAimOptions.Off;
    private bool _aimWasHeld;
    private bool _aimHeld;
    private RigOffset _adsReference = RigOffset.Identity;
    private AdsProfile? _adsReferenceFor;

    /// <summary>Gets or sets where the tick reads modern aim's settings, or <see langword="null"/> for the variables.</summary>
    /// <remarks>A session's server hands its players its own, so a client and the server play by the same.</remarks>
    public Func<ModernAimOptions>? ModernAimSource { get; set; }

    /// <summary>Gets or sets the table of the weapons that can be aimed, and how.</summary>
    public AdsProfileTable AimProfiles { get; set; } = AdsProfileTable.Igi1;

    /// <summary>Gets the aim down the sights.</summary>
    public AimDownSights Ads => _ads;

    /// <summary>Gets the sideways input this tick, minus one (left) to one (right), for the weapon's lean against a strafe.</summary>
    public float StrafeInput => PlayerInputSampler.Axis(_input, PlayerInputSlot.MoveRight, PlayerInputSlot.MoveLeft);

    /// <summary>Gets the settings modern aim ran the last tick with.</summary>
    public ModernAimOptions ModernAim => _modernAim;

    /// <summary>Gets the move that lays the held weapon's sights on the eye, at its still stand pose.</summary>
    /// <remarks>Worked out once for each weapon, as it is taken up; the identity for one that cannot be aimed.</remarks>
    public RigOffset AdsReference => _adsReference;

    /// <summary>Gets what the sights make of the look's speed: one at the hip, the profile's share through them.</summary>
    /// <remarks>On top of the zoom's own scale (<see cref="MouseLookSettings.ZoomSensitivityScale"/>), which the sights' zoom
    /// already brings in through the view's tangent.</remarks>
    public float AimLookScale => AimDownSights.Blend(1f, _modernAim.Sensitivity, _ads.Progress);

    // The frame as the original's machines are to see it. For a weapon with sights, the alternate-fire button is the sights' and
    // the underbarrel key takes its place, so the M16's launcher fires from it; any other weapon -- the Dragunov's scope, the
    // binoculars -- keeps its own. Read here, before the machines run, so the aim and the launcher answer the same tick.
    private PlayerInputFrame TakeModernAimInput(in PlayerInputFrame frame)
    {
        _modernAim = ModernAimSource?.Invoke() ?? ModernAimOptions.Current;
        _aimWasHeld = _aimHeld;
        _aimHeld = false;
        if (!_modernAim.Enabled || !AimProfiles.TryGet(_weapons?.InHand?.Weapon.ScriptId, out _))
        {
            return frame;
        }

        PlayerInputFrame taken = frame;
        _aimHeld = taken.IsSet(PlayerInputSlot.AlternateFire);
        taken[(int)PlayerInputSlot.AlternateFire] = taken.IsSet(PlayerInputSlot.Underbarrel)
            ? (ulong)PlayerAction.AlternateFire
            : 0f;
        return taken;
    }

    // Steps the aim, the lean and the brace from this tick's keys, as the weapon and the body stood at the end of the last: the
    // sights up only with a ready weapon in hands that are free, on feet that are on the ground.
    private void TickAds()
    {
        AdsProfile? profile = _modernAim.Enabled && AimProfiles.TryGet(_weapons?.InHand?.Weapon.ScriptId, out AdsProfile found)
            ? found
            : null;
        bool free = !_ladder.IsOnLadder
            && _interaction.Active is null
            && !HandsOnBomb
            && _weapons is not { State: HumanWeaponState.Peeking }
            && _locomotion.State is not (HumanBodyStateMachine.Jump or HumanBodyStateMachine.Fall or HumanBodyStateMachine.Lean);
        bool canAim = free && _weapons is { IsWeaponReady: true };

        TickMount(profile, canAim);
        bool aimHeld = _aimHeld || (_mountedByKey && _mount.IsMounted);
        _ads.Tick(_modernAim, profile, aimHeld, _aimHeld && !_aimWasHeld, canAim);
        TickLean(free);

        if (!ReferenceEquals(profile, _adsReferenceFor))
        {
            _adsReferenceFor = profile;
            _adsReference = profile is not null && _firstPerson.Rig is { } rig && _weapons?.InHand?.Weapon is { } weapon
                ? SightAlignment.Reference(profile, rig.Bones.Set, weapon.StandAnimation)
                : RigOffset.Identity;
        }

        float progress = _ads.Progress;
        float braced = _mount.IsMounted ? 1f : 0f;
        _firstPerson.AimLookScale = AimLookScale;
        _body.AimSpeedScale = (profile is null ? 1f : AimDownSights.Blend(1f, profile.MoveScale, progress))
            * (_lean.Amount != 0f ? LeaningSpeedScale : 1f);
        if (_weapons is not null)
        {
            _weapons.AimSpreadScale = (profile is null ? 1f : AimDownSights.Blend(1f, profile.SpreadScale, progress))
                * AimDownSights.Blend(1f, WeaponMount.SpreadScale, braced);
            _weapons.AimRecoilScale = (profile is null ? 1f : AimDownSights.Blend(1f, profile.RecoilScale, progress))
                * AimDownSights.Blend(1f, WeaponMount.RecoilScale, braced);
        }
    }

    // The sights' zoom, written over what the optics left in both tangents -- for a weapon with sights the optics have nothing
    // of their own to say, the MP5's toggle having been taken off its button.
    private void ApplyAdsOptics(HumanView view)
    {
        if (_ads.Profile is not { } profile || !_modernAim.Enabled)
        {
            return;
        }

        float shape = AimDownSights.Shape(_ads.Progress);
        float magnification = 1f + ((MathF.Max(1f, profile.Magnification) - 1f) * _modernAim.Zoom);
        view.TanX = HumanView.DefaultTanX / AimDownSights.Blend(1f, magnification, shape);
        view.ViewModelTanX = AimDownSights.Blend(HumanView.DefaultViewModelTanX, profile.ViewModelTanX, shape);
    }

    /// <summary>What modern aim does to the rig in a frame drawn between the last two ticks.</summary>
    /// <param name="alpha">How far the frame sits between them, 0 to 1.</param>
    /// <param name="motion">The frame's view-model motion.</param>
    /// <returns>The sights' eased move and the motion; <see cref="RigAim.None"/> with modern aim off.</returns>
    public RigAim AimRig(float alpha, RigOffset motion) => !_modernAim.Enabled
        ? RigAim.None
        : new RigAim(_ads.Profile, AimDownSights.Shape(_ads.Smoothed(alpha)), _adsReference, motion);

    // Takes the sights down with everything else the optics reset: a death, a placement.
    private void ResetAds()
    {
        _ads.Reset();
        _lean.Reset();
        _mount.Reset();
        _mountedByKey = false;
        _aimHeld = false;
        _aimWasHeld = false;
        _body.AimSpeedScale = 1f;
        _firstPerson.AimLookScale = 1f;
        _firstPerson.ModernLean = EyeLean.None;
        _firstPerson.LookLimit = null;
        if (_weapons is not null)
        {
            _weapons.AimSpreadScale = 1f;
            _weapons.AimRecoilScale = 1f;
        }
    }
}
