using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Project I.G.I.'s first-person view: the rig and the weapon in hand, or the optics that replace them, and the binoculars' range.</summary>
/// <param name="device">The device it draws with.</param>
/// <param name="gameplay">The first game's mission, whose player it draws.</param>
/// <param name="logger">Where it reports the weapon it takes up.</param>
internal sealed class Igi1FirstPersonView(IGraphicsDevice device, Igi1Gameplay gameplay, IOpenIgiLogger logger) : IMissionFirstPersonView
{
    private readonly IGraphicsDevice _device = device;
    private readonly Igi1Gameplay _gameplay = gameplay;
    private readonly IOpenIgiLogger _logger = logger;

    /// <inheritdoc/>
    public FirstPersonResult Draw(in Camera3D eye, in FirstPersonFrame frame)
    {
        MissionWorld mission = _gameplay.World;
        // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
        // then ask the human where the weapon is and draw that. Only in player mode, because the
        // original draws it out of the human's own render and the free camera is not a human -- and
        // NOT once the player is dead, when the death camera is watching the body from the outside
        // and a pair of arms hanging in front of it would be exactly wrong.
        // Not while the entry shot is running: the camera is somewhere over the level, and a pair
        // of arms and a HUD hanging in front of it would be exactly wrong. They come up at the
        // moment the view is handed over, which is also the moment the player can use them.
        bool binocularsOpen = mission.Player is { IsBinocularsOpen: true };

        // Scoped, the Dragunov replaces the first-person picture with its scope overlay: retail's
        // 0x473040 latch branch draws snipersight.spr and never poses the rig, so the rifle and the
        // gloves are gone and only the scope picture remains. Hide the whole view model exactly as
        // the binoculars do, or the weapon mesh shows through the aperture.
        bool sniperScoped = mission.Player is { IsSniperScoped: true };
        float? binocularRangeMetres = null;
        bool drewRig = false;
        if (binocularsOpen && mission.Solids is { } rangeSolids)
        {
            // FUN_00471480's DISTANCE readout is a 1000 m ray cast from the eye along the aim
            // direction; its 0.000244140625 scale is 1/4096 = 1/UnitsPerMetre. The range is
            // whatever the centre crosshair rests on -- terrain or a placed object -- not the
            // nearest detected soldier, so a bare hillside still reads a distance. A ray that
            // reaches its 1000 m end unobstructed leaves the readout at the retail "1000+M".
            Vector3d forward = eye.Orientation.Transform(new Vector3d(0d, 0d, 1d));
            Vector3d rangeOrigin = new(
                eye.Position.X * WorldScene.UnitsPerMetre,
                eye.Position.Y * WorldScene.UnitsPerMetre,
                eye.Position.Z * WorldScene.UnitsPerMetre);
            const double RangeSpanUnits = 1000d * WorldScene.UnitsPerMetre;
            Vector3d rangeEnd = new(
                rangeOrigin.X + (forward.X * RangeSpanUnits),
                rangeOrigin.Y + (forward.Y * RangeSpanUnits),
                rangeOrigin.Z + (forward.Z * RangeSpanUnits));
            WorldTraceCast rangeTrace = frame.Terrain is { } rangeGround
                ? WorldTrace.Nearest(rangeSolids.Tracer(), TerrainRayTrace.Caster(rangeGround))
                : rangeSolids.Tracer();
            if (rangeTrace(
                    new WorldTraceRequest(rangeOrigin, rangeEnd, WorldTraceMask.World),
                    out WorldTraceHit rangeHit)
                && double.IsFinite(rangeHit.Distance))
            {
                binocularRangeMetres = (float)(rangeHit.Distance / WorldScene.UnitsPerMetre);
            }
        }
        // Not before the rig has a clip: see HumanView.IsPosed. At spawn that is the whole lowering
        // half of the first weapon's change, so the weapon rises into an empty frame, as in retail.
        if (frame.ShowViewModel && mission.Player is { View: { IsPosed: true } rig } && !binocularsOpen && !sniperScoped)
        {
            // Only an advancing clip changes the skin, so the re-skin is keyed to the tick rather
            // than the frame.
            if (mission.Player.PoseChanged)
            {
                frame.Arms?.Invalidate();
                mission.Player.AcknowledgePose();
            }

            // The view model gets its OWN projection: 0x49F700 saves the current view block,
            // rebuilds [16..19] from dword_B81700 -- HumanCamera+220, filled from HumanView+488 =
            // tan(30 degrees) -- and restores it afterwards, for any bone model with no LOD chain,
            // which the HumanView is (0x464660 leaves +212 at zero). The rebuild is sub_497D70's
            // own algebra, the SAME build every camera pass uses: [16] is the plain half-angle
            // tangent, [18] = halfWidth / [16] and [19] = Aspect43 * [18] are the pixel scales the
            // projection divides through (sub_50FE60 publishes them as the per-vertex xScale and
            // yScale). So against the world's tan(45), the arms and the weapon really are drawn
            // tan45/tan30 = 1.732x magnified about the screen centre -- which moves a hand held
            // below centre DOWN and out of frame. That is what holds the glove below the bottom
            // edge in retail. (An earlier pass refuted this as "larger is the wrong direction";
            // larger is exactly what hides it.) section 14.7.
            //
            // The vertical tangent is 3/4 of the horizontal one and does NOT follow the window's
            // aspect: [19] = Aspect43() * [18] works out to yScale = halfHeight / (0.75 * tan) at
            // EVERY aspect, so retail's vertical framing -- the glove at the bottom edge -- is
            // aspect-invariant. Dividing the tangent by the aspect instead (the world's widescreen
            // rule) shrinks the view model's vertical reach at 16:9 and pushes the hand off the
            // bottom of the frame. At 4:3 the two rules coincide, tanY = 0.433 and tanX = tan(30).
            Matrix4f viewModelProjection = ViewModelProjection.Create(frame.ViewModelTanX, frame.FarPlane);

            // Between-tick smoothing for everything the rig steps at 30 Hz: its origin (the eye
            // the body carried plus the clip's hold offset), its skin, and the weapon bone. The
            // LOOK is already per frame; without this the arms and the knife stepped at 30 Hz
            // against a camera that now glides -- which read as jitter -- and skipped several
            // poses per drawn frame whenever ticks ran back to back, which read as the animation
            // racing.
            Vector3d? rigOriginUnits = null;
            Matrix3f? rigOrientation = null;
            BoneAttachment? weaponAttachment = mission.Player.HeldWeapon;
            BoneAttachment? magazineAttachment = mission.Player.HeldWeaponSecondary;
            BoneAttachment? movingPartAttachment = mission.Player.HeldWeaponTertiary;
            float previousPoseShare = 0f;
            if (frame.Smooth && mission.Player.HasViewTickPair)
            {
                (
                    Vector3d smoothedOrigin,
                    Matrix3f smoothedOrientation,
                    BoneAttachment? smoothedWeapon,
                    BoneAttachment? smoothedMagazine,
                    BoneAttachment? smoothedMovingPart) =
                    mission.Player.SmoothedRig(frame.TickBlend);
                rigOriginUnits = smoothedOrigin;
                rigOrientation = smoothedOrientation;
                weaponAttachment = smoothedWeapon ?? weaponAttachment;
                magazineAttachment = smoothedMagazine ?? magazineAttachment;
                movingPartAttachment = smoothedMovingPart ?? movingPartAttachment;
                previousPoseShare = 1f - frame.TickBlend;
            }

            // The retail rig is foreground-only: it draws after the world under its own transform
            // layer, so an already-rendered wall must not be allowed to cut through the hands or
            // weapon. Resetting only depth keeps normal arm-versus-weapon self-occlusion.
            ViewModelDepth.Begin(_device);
            // The arms and the weapon are not the world: the eye adaptation must not meter them
            // and the indirect gather must not bounce light off them, or holstering changes the
            // lighting of a room that has not moved.
            _device.SetFirstPersonGeometry(true);
            frame.Arms?.Draw(rig, eye, viewModelProjection, rigOriginUnits, previousPoseShare, rigOrientation);

            // Cycling weapons changes the model as well as the animations, so the renderer has to
            // follow whatever the cycle put in hand.
            if (frame.LoadedForWeapon is { } missionForWeapon)
            {
                if (frame.Held is { } heldToFollow)
                {
                    FollowHeldWeapon(heldToFollow, missionForWeapon);
                }
            }

            if (mission.Player.Weapons?.HideFirstPersonWeapon != true
                && weaponAttachment is { } attachment
                && frame.Held is { IsReady: true })
            {
                frame.Held.Draw(
                    attachment,
                    eye,
                    viewModelProjection,
                    // 0x473510 is a separate GunM16A2 task: it does not pass through the
                    // ordinary Gun_Fire/GUNFLAME path. Do not show the rifle muzzle flame on
                    // an M203 launch; the launcher task owns its own launch report/effect.
                    mission.Player.FiredThisTick && !mission.Player.M203LaunchedThisTick,
                    mission.Player.ShotSequence,
                    WeaponDrawParts.All,
                    magazineAttachment,
                    movingPartAttachment);
            }

            _device.SetFirstPersonGeometry(false);
            drewRig = true;
        }

        return new FirstPersonResult(drewRig, binocularRangeMetres);
    }

    /// <inheritdoc/>
    public void FollowHeldWeapon(IHeldWeaponPresenter held, LoadedMission loadedMission)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(loadedMission);
        if (_gameplay.World.Player is not { } candidate || !candidate.WeaponChanged)
        {
            return;
        }

        candidate.AcknowledgeWeaponChange();

        if (candidate.HeldWeaponType is not { } weapon)
        {
            return;
        }

        if (held.Load(loadedMission, weapon.GunModel))
        {
            _logger.Information("Weapon", $"Holding {weapon.ScriptId} ('{weapon.GunModel}') on view bone "
                + $"{HumanAttachPoints.FirstPersonWeapon}.");
        }
    }
}
