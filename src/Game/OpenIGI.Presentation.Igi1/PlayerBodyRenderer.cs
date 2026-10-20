using System.Runtime.InteropServices;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Physics;
using Group = OpenIGI.Presentation.Igi1.SoldierRenderer.Group;
using Member = OpenIGI.Presentation.Igi1.SoldierRenderer.Member;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The player's own third-person body: Jones, the rig the first-person view never shows.</summary>
/// <remarks>
/// <para>
/// It is NOT a guard: it has no AI, is in none of the renderer's groups, and the soldier tick and draw loops never touch it.
/// It loads with the level (<see cref="Load"/>), stands invisibly until the player dies, then poses the death clip
/// (<see cref="Kill"/>) and is drawn (<see cref="Draw"/>) once the death camera has pulled back off the eye; the ladder,
/// the wires and the third-person camera draw it as well.
/// </para>
/// <para>
/// Its simulation -- the pose, the clip, where it stands -- is <see cref="SoldierSimulation"/>'s; this keeps what is drawn
/// of it, through the same group, re-skin and draw machinery a soldier's body uses on its owning <see cref="SoldierRenderer"/>.
/// </para>
/// </remarks>
/// <param name="owner">The renderer whose machinery the body is drawn with.</param>
/// <param name="device">The device the body is uploaded to and drawn with.</param>
/// <param name="sim">The soldiers' simulation, which holds the body's pose and placement.</param>
/// <param name="logger">Where load problems are reported.</param>
internal sealed class PlayerBodyRenderer(SoldierRenderer owner, IGraphicsDevice device, SoldierSimulation sim, IOpenIgiLogger logger)
{
    private readonly SoldierRenderer _owner = owner;
    private readonly IGraphicsDevice _device = device;
    private readonly SoldierSimulation _sim = sim;
    private readonly IOpenIgiLogger _logger = logger;

    // The player's own third-person body -- Jones, the rig the first-person view never shows. It is NOT a
    // guard: it has no AI, is not in _groups, and the soldier tick and draw loops never touch it. It loads
    // with the level (LoadPlayerBody), stands invisibly until the player dies, then poses the death clip
    // (KillPlayerBody) and is drawn (DrawPlayerBody) once the death camera has pulled back off the eye.
    // Reuses the same BuildGroup / re-skin / draw machinery a soldier's body does. See HumanPlayerBody.
    // The player's own corpse, when g_ragdoll is on. Exactly the soldier arrangement: the retail death
    // clip keeps running underneath and keeps moving the authoritative body, and this replaces only the
    // pose and the placement that are drawn.
    private RagdollBody? _ragdoll;
    private Group? _group;
    private Member? _member;
    private bool _active;

    private float _facing;
    private Igi1HumanAi.GroundQuery? _ground;

    // The player body's own tick-state pair and frame bookkeeping -- the same smoothing a soldier's
    // Member carries, kept as fields because there is only ever one of it.
    private Vector3d _tickWhere;
    private Vector3d _prevTickWhere;
    private bool _hasTickState;
    private int _captureTick = -1;
    private int _skinTick = -1;


    /// <inheritdoc cref="ISoldierBodies.PlayerBodyFollowVisible"/>
    public bool FollowVisible { get; set; } = true;

    /// <inheritdoc cref="ISoldierPresenter.IsPlayerBodyActive"/>
    public bool IsActive => _active;

    /// <inheritdoc cref="ISoldierPresenter.PlayerRagdollCentre"/>
    public Vector3d? RagdollCentre => _ragdoll?.Centre;

    /// <inheritdoc cref="ISoldierBodies.LoadPlayerBody"/>
    public bool Load(
        LoadedMission mission, string model, BoneAnimationSet animations, Vector3d position, float gamma)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(animations);
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);

        Release();

        HumanSoldierTask task = new()
        {
            Model = model,
            BoneHierarchy = animations.Skeleton.Id,
            StandAnimation = HumanPlayerBody.StandAnimation,
            Position = position,
            Gamma = gamma,
        };

        SoldierView view;
        try
        {
            view = new SoldierView(task, animations);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"The player body could not be posed on skeleton {animations.Skeleton.Id}: {ex.Message}");
            return false;
        }

        Group? group = _owner.Meshes.BuildGroup(mission, model, view);
        if (group is null)
        {
            return false;
        }

        // The player's body is also a hit-test candidate for incoming AI rounds. Prime the same immutable
        // data as the mission soldiers so the first hostile hit cannot trigger a synchronous parse/build.
        try
        {
            _ = group.Body.CollisionMesh;
        }
        catch (PackFormatException ex)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"The player collision mesh was not usable: {ex.Message}");
        }

        if (GameplayCVars.Ragdoll.Value && group.Body.Model is not null)
        {
            _ = _sim.RagdollRigFor(animations, group.Body.Model);
        }

        MeshHandle own;
        try
        {
            own = _device.CreateAnimatedMesh(group.Geometry.Vertices, group.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"The player body '{model}' did not upload: {ex.Message}");
            return false;
        }

        _sim.PlayerBodyView = view;
        _sim.PlayerBodyModel = group.Body;
        _group = group;
        _member = new Member(new SoldierAgent(view, default), own);
        _ground = _sim.MakeGroundQuery();
        _active = false;
        _hasTickState = false;
        _captureTick = -1;
        _skinTick = -1;
        _logger.Information(SoldierRenderer.LogCategory, $"Loaded the player's third-person body '{model}' on skeleton {animations.Skeleton.Id}.");
        return true;
    }

    /// <inheritdoc cref="ISoldierPresenter.KillPlayerBody"/>
    public void Kill(
        int deathAnimation,
        Vector3d feet,
        float gamma,
        int bone = -1,
        Vector3d travel = default,
        WeaponCategory category = WeaponCategory.Gun,
        Vector3d velocity = default)
    {
        if (_sim.PlayerBodyView is null)
        {
            return;
        }

        _facing = gamma;
        _sim.PlayerBodySuppressRootHeight = false;
        _sim.PlayerBodyVisualLift = 0.0;
        _sim.PlayerBodyView.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, gamma));
        _sim.PlayerBodyView.Kill(deathAnimation);
        _active = true;

        // Seeded before the tick state is captured, so the very first capture is already the corpse's
        // own placement rather than the clip's -- a death is a cut and there is nothing to blend from.
        _ragdoll = _owner.SeedRagdoll(
            _sim.PlayerBodyView,
            new Vector3d(feet.X, feet.Y, feet.Z + _sim.PlayerBodyView.RootSample.Z),
            _sim.PlayerBodyView.Orientation,
            bone,
            travel,
            category,
            velocity,
            _group?.Body.Model,
            weapon: null);

        // A death is a cut, not a movement: the body appears where the player fell, and nothing on
        // screen is its history. Snap the interpolation pair and start the frame bookkeeping over.
        CaptureTickState(snap: true);
        if (_member is { } fresh)
        {
            fresh.HasFrameState = false;
            fresh.HasTickState = false;
        }

        // The fall clip has to resolve in the player's set, or Kill silently keeps the idle and the body
        // never falls; reported once so a wrong id is visible rather than a body that just stands there.
        if (_sim.PlayerBodyView.CurrentAnimation != deathAnimation)
        {
            _logger.Warning(
                SoldierRenderer.LogCategory,
                $"The player's death clip {deathAnimation} is not in its set; the body keeps clip "
                    + $"{_sim.PlayerBodyView.CurrentAnimation} and will not fall.");
        }
    }

    /// <inheritdoc cref="ISoldierPresenter.TickPlayerBody"/>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (!_active
            || _sim.PlayerBodyView is null
            || _member is null
            || _group?.Body.Model is not { } model)
        {
            return;
        }

        _sim.PlayerBodyView.Tick();

        // The knock-back. A dying body is thrown -- the fall clip's own root-motion track carries it back
        // and down, and the retail mover consumes exactly that (0x4109A0 samples the body clip's root delta
        // through sub_4D2FB0 and moves the human by it, until 0x40E620 sees the body grounded and stops it).
        // Advancing the clip WITHOUT moving the body left it crumpling on the spot instead of being thrown,
        // which is the missing "knocked to the ground" feel. This is the corpse move a soldier already gets
        // (Igi1HumanAi.TickDeadBody): the horizontal root motion, rotated by the facing, plus a ground snap.
        Vector3f rootMotion = _sim.PlayerBodyView.RootMotion();
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(rootMotion.X, rootMotion.Y, rootMotion.Z), _facing);

        double px = _sim.PlayerBodyView.Position.X + world.X;
        double py = _sim.PlayerBodyView.Position.Y + world.Y;
        double pz = _sim.PlayerBodyView.Position.Z + world.Z;
        if (_ground is { } ground
            && ground(px, py, pz + SoldierSimulation.ProbeAboveInUnits, out double groundZ))
        {
            pz = groundZ;
        }

        _sim.PlayerBodyView.PlaceAt(new Vector3d(px, py, pz), _sim.PlayerBodyView.Orientation);

        // The corpse, when there is one. It is stepped after the retail move above rather than instead
        // of it: that move is what the rest of the game believes, and only the drawing changes here.
        if (_ragdoll is { } playerCorpse)
        {
            playerCorpse.Step(
                _sim.RagdollWorldForCorpses(),
                GameplayCVars.RagdollSubsteps.Value,
                GameplayCVars.RagdollGravity.Value);
            if (!playerCorpse.IsValid)
            {
                _ragdoll = null;
                _logger.Warning(SoldierRenderer.LogCategory, "The player's ragdoll left the level; falling back to the death clip.");
            }
        }

        Reskin(_sim.PlayerBodyView, model);
        CaptureTickState(snap: false);
    }

    /// <inheritdoc cref="ISoldierBodies.StandPlayerBodyAt"/>
    public void StandAt(Vector3d feetUnits, float facing)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_sim.PlayerBodyShown;
        _facing = facing;
        _sim.PlayerBodySuppressRootHeight = false;
        _sim.PlayerBodyVisualLift = 0.0;
        view.PlaceAt(feetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // Its own stand clip, advanced so the body breathes rather than being a statue -- and re-skinned
        // at most once per simulation tick, which is the same rate the climbing path uploads at and the
        // rate the previous-pose stream expects.
        //
        // ON THE FRAME IT APPEARS THE SKIN IS NOT OPTIONAL. The mesh was uploaded from the model's REST
        // geometry and holds no pose until something puts one in it, so if the per-tick guard happened to
        // skip that first frame the body was drawn once from its rest pose -- which is a rig splayed
        // across the world and, from the camera, a flash over the whole screen. The visibility flag is
        // therefore only raised AFTER a successful skin: until the body has a pose it is not drawn.
        bool posed = !appearing;
        if ((appearing || _skinTick != _sim.TickCount) && _group?.Body.Model is { } model)
        {
            view.Tick();
            Reskin(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _sim.PlayerBodyShown = true;

        // Appearing is a cut: nothing on screen is this body's history, so neither the interpolation pair
        // nor the temporal history may bridge from wherever it last stood.
        CaptureTickState(snap: appearing);
        if (appearing && _member is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }




    /// <inheritdoc cref="ISoldierPresenter.FollowPlayerBody"/>
    public void Follow(Vector3d feetUnits, float facing, int animation, bool loops, in PlayerBodyPose pose)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_sim.PlayerBodyFollowing;
        _facing = facing;
        _sim.PlayerBodySuppressRootHeight = false;
        _sim.PlayerBodyVisualLift = 0.0;

        if (animation != HumanLocomotionStates.NoAnimation)
        {
            // A no-op when the state has not changed, so holding a direction does not restart the walk
            // every tick — the same contract the player's own animator keeps.
            view.PlayStateAnimation(animation, loops);
        }

        // The torso, on bone 1. A new request is a new clip even when it names the one already playing,
        // because a second shot has to restart the fire animation -- so the SEQUENCE decides, not the id.
        if (pose.UpperBodySequence != _sim.PlayerBodyUpperBodySequence)
        {
            _sim.PlayerBodyUpperBodySequence = pose.UpperBodySequence;
            if (pose.UpperBodyAnimation < 0)
            {
                view.StopUpperBodyAnimation();
            }
            else
            {
                view.PlayUpperBodyAnimation(
                    pose.UpperBodyAnimation, pose.UpperBodyLoops, pose.UpperBodyBlendSeconds);
            }
        }

        view.AimAlong(pose.AimPitch);
        view.PlaceAt(feetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // The mesh is uploaded from the model's REST geometry and holds no pose until something puts one
        // in it, so the first skin is not optional: without it the body's very first drawn frame is a rig
        // splayed across the world. Once per simulation tick after that.
        bool posed = !appearing;
        if ((appearing || _skinTick != _sim.TickCount) && _group?.Body.Model is { } model)
        {
            view.Tick();
            Reskin(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _sim.PlayerBodyFollowing = true;
        CaptureTickState(snap: appearing);
        if (appearing && _member is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }

    /// <inheritdoc cref="ISoldierBodies.PlayerBodyHandAttachment"/>
    public BoneAttachment? HandAttachment()
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (!_sim.PlayerBodyFollowing || _sim.PlayerBodyView is not { } view || _member is not { } member)
        {
            return null;
        }

        BonePose pose = view.UnrotatedPose();
        if (HumanAttachPoints.ThirdPersonWeapon >= pose.BoneCount)
        {
            return null;
        }

        Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
        Matrix3f handRotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
        if (_owner.FrameSmooth && member.HasHandState)
        {
            hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * _owner.FrameAlpha);
            handRotation = Interpolation.InterpolateRotation(
                member.PrevTickHandRotation, member.TickHandRotation, _owner.FrameAlpha);
        }

        (Vector3d where, Matrix3f orientation) = Placement();
        if (_owner.FrameSmooth && _hasTickState)
        {
            where = Vector3d.Lerp(_prevTickWhere, _tickWhere, _owner.FrameAlpha);
        }

        // M . v — the palette's own convention — is the transpose of the row-vector Transform, the same
        // turn DrawWeapon applies to a soldier's hand.
        Vector3f turned = orientation.Transposed().Transform(hand);
        return new BoneAttachment(
            new Vector3d(
                (where.X * WorldScene.UnitsPerMetre) + turned.X,
                (where.Y * WorldScene.UnitsPerMetre) + turned.Y,
                (where.Z * WorldScene.UnitsPerMetre) + turned.Z),
            orientation * handRotation);
    }

    /// <inheritdoc cref="ISoldierPresenter.StartPlayerLadderAnimation"/>
    public void StartLadderAnimation(
        int animation,
        bool loops,
        bool backwards = false,
        bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        _sim.StartPlayerTraversalAnimation(
            view, animation, loops, backwards, paused, suppressRootHeight: true, visualLift: 0.0);

        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        ResetTraversalHistory();
    }

    /// <inheritdoc cref="ISoldierPresenter.StartPlayerInteractionAnimation"/>
    public void StartInteractionAnimation(int animation, bool loops)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        _sim.StartPlayerTraversalAnimation(
            view,
            animation,
            loops,
            backwards: false,
            paused: false,
            suppressRootHeight: false,

            // With the exact 3686.4-unit task anchor from sub_411F20, the posed wrist centres of both
            // retail wire clips sit 375..567 units below the cable. Half a retail model grid cell puts
            // the palms around the cable without moving the gameplay anchor, projection or end margins.
            visualLift: SoldierSimulation.IsWireAnimation(animation) ? 512.0 : 0.0);
        ResetTraversalHistory();
    }


    private void ResetTraversalHistory()
    {
        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        _hasTickState = false;
        _skinTick = -1;
        if (_member is { } member)
        {
            member.HasFrameState = false;
        }
    }

    /// <inheritdoc cref="ISoldierPresenter.AdvancePlayerLadderAnimation"/>
    public void AdvanceLadderAnimation(
        float facing,
        out Vector3d rootDelta,
        out bool rungBoundary,
        out bool ended)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        rootDelta = default;
        rungBoundary = false;
        ended = false;
        if (_sim.PlayerBodyView is not { } view || _group?.Body.Model is not { } model)
        {
            return;
        }

        _sim.PlayerBodyClimbing = true;
        _facing = facing;
        view.Tick();

        Vector3f rm = view.RootMotion();
        rootDelta = SoldierDeathFall.RotateAboutZ(new Vector3d(rm.X, rm.Y, rm.Z), facing);
        rungBoundary = view.CrossedLadderBoundary;
        ended = view.ClipEndedThisTick;
        Reskin(view, model);

        // The caller places the body with the delta straight after; the placement call captures the
        // tick state, so nothing is recorded here off a stale position.
    }

    /// <inheritdoc cref="ISoldierPresenter.PlacePlayerBodyClimbing"/>
    public void PlaceClimbing(Vector3d taskPosition, float facing)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        _sim.PlayerBodyClimbing = true;
        _facing = facing;
        view.PlaceAt(taskPosition, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // A newly paused rung must upload its held pose into both interpolation streams. Reskin's
        // pose latch permits that one duplicate upload, then suppresses further skinning until the pose moves.
        // AdvanceLadderAnimation already uploads each genuinely moving tick.
        if (_skinTick != _sim.TickCount && _group?.Body.Model is { } model)
        {
            Reskin(view, model);
        }

        CaptureTickState(snap: false);
    }


    // Re-poses the player body's animated mesh in place. Shared by the death fall and the ladder climb.
    private void Reskin(SoldierView view, MefModel model)
    {
        if (_member is not { } member || _group is null || !member.Mesh.IsValid)
        {
            return;
        }

        BonePose pose = _ragdoll is { } corpse ? corpse.Pose() : view.UnrotatedPose();
        member.DrawnPose = pose;
        if (!member.PoseUploads.ShouldUpload(pose))
        {
            _skinTick = _sim.TickCount;
            return;
        }

        if (!SoldierSkinning.Upload(_device, member, _group.Geometry, model, pose))
        {
            return;
        }

        _skinTick = _sim.TickCount;
    }

    /// <summary>Where the player's body is drawn, in metres, and which way it is turned.</summary>
    /// <remarks>
    /// <para>
    /// A body is normally shifted by its clip's root height (<c>Human+1300</c>), which for a fall, stand
    /// or wire interaction is part of the authored placement. A ladder position is different: IGI1 writes
    /// the human task root itself (bottom + 3915.776 vertically), and clip 168's lowest foot is authored
    /// at −4128.768. Those values put the foot at ground height already, so adding a pose-derived pelvis
    /// lift there duplicates the mount offset and starts Jones a body-height above the first rung.
    /// </para>
    /// <para>
    /// A simulated corpse answers with its own frozen origin and no turn at all: its pose is already in
    /// world space relative to that origin, with the heading folded into the root bone's rotation.
    /// </para>
    /// </remarks>
    private (Vector3d Where, Matrix3f Orientation) Placement()
    {
        if (_ragdoll is { } corpse)
        {
            return (corpse.Origin / WorldScene.UnitsPerMetre, Matrix3f.Identity);
        }

        if (_sim.PlayerBodyView is not { } view)
        {
            return (default, Matrix3f.Identity);
        }

        double lift = (_sim.PlayerBodySuppressRootHeight ? 0.0 : view.RootSample.Z) + _sim.PlayerBodyVisualLift;
        return (
            new Vector3d(
                view.Position.X / WorldScene.UnitsPerMetre,
                view.Position.Y / WorldScene.UnitsPerMetre,
                (view.Position.Z + lift) / WorldScene.UnitsPerMetre),
            view.Orientation);
    }

    /// <summary>Records where the player's body stands this tick, keeping the tick before.</summary>
    /// <param name="snap">
    /// Whether this placement is a discontinuity — a death, a ladder mount — after which the pair
    /// must not be interpolated across.
    /// </param>
    private void CaptureTickState(bool snap)
    {
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        Vector3d where = Placement().Where;

        if (snap || !_hasTickState)
        {
            _prevTickWhere = where;
        }
        else if (_captureTick != _sim.TickCount)
        {
            _prevTickWhere = _tickWhere;
        }

        // A second capture in the same tick only refines the current state; the previous one stands.
        _tickWhere = where;

        // The hand the third-person weapon rides, captured in the same pair and by the same rules a
        // soldier's is (ResolveTickState). Only the followed body needs it: the death and ladder paths
        // draw no weapon, and asking a posed rig for a bone it has costs one array read.
        if (_sim.PlayerBodyFollowing
            && _member is { } member
            && HumanAttachPoints.ThirdPersonWeapon < view.UnrotatedPose().BoneCount)
        {
            BonePose pose = view.UnrotatedPose();
            Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
            Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
            bool fresh = snap || !member.HasHandState;
            member.PrevTickHandPosition = fresh
                ? hand
                : _captureTick != _sim.TickCount ? member.TickHandPosition : member.PrevTickHandPosition;
            member.PrevTickHandRotation = fresh
                ? rotation
                : _captureTick != _sim.TickCount ? member.TickHandRotation : member.PrevTickHandRotation;
            member.TickHandPosition = hand;
            member.TickHandRotation = rotation;
            member.HasHandState = true;
        }

        _captureTick = _sim.TickCount;
        _hasTickState = true;
    }

    /// <summary>Settles where the player's own body is drawn this frame, and what it moved from.</summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ResolveFrameState"/> a soldier gets, for the one body that is not in a group. Its
    /// placement is a ladder position or a fall or a walk — <see cref="Placement"/> knows which
    /// — interpolated between the last two tick captures exactly as a guard's is.
    /// </para>
    /// <para>
    /// A body normally sits at its clip's own root height (<c>Human+1300</c>), which for a fall, a stand
    /// or a wire interaction is part of the authored placement. A ladder position is not: IGI1 writes the
    /// human task root itself (bottom + 3915.776 vertically) and clip 168's lowest foot is authored at
    /// −4128.768, which put the foot at ground height already, so adding a pose-derived pelvis lift there
    /// duplicates the mount offset and starts Jones a body-height above the first rung. That case clears
    /// the lift rather than being special-cased here.
    /// </para>
    /// </remarks>
    /// <summary>Resolves where the body is drawn this frame, between its last two ticks.</summary>
    internal void ResolveFrameState()
    {
        if (_sim.PlayerBodyView is null || _member is not { } member)
        {
            return;
        }

        (Vector3d where, Matrix3f orientation) = Placement();
        if (_owner.FrameSmooth && _hasTickState)
        {
            where = Vector3d.Lerp(_prevTickWhere, _tickWhere, _owner.FrameAlpha);
        }

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = _owner.FrameSmooth ? 1f - _owner.FrameAlpha : 0f;
        member.PoseFlippedSinceFrame = false;
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;
    }

    /// <inheritdoc cref="ISoldierBodies.DrawPlayerBody"/>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, _owner);

        bool visible = _active
            || _sim.PlayerBodyClimbing
            || _sim.PlayerBodyShown
            || (_sim.PlayerBodyFollowing && FollowVisible);

        // Drawn for nobody, but THERE for a ray. In first person the body is not drawn -- the camera
        // would be inside its head -- and until a reflection could shade a hit from the hit's own
        // material, that also meant it could never appear in a window, because the only geometry a ray
        // can strike is geometry something submitted. It can be shaded now, so it is submitted now: the
        // player gets a reflection of themselves, and a shadow, without their own legs in shot.
        bool tracedOnly = !visible && _sim.PlayerBodyFollowing;

        if ((!visible && !tracedOnly)
            || _sim.PlayerBodyView is null
            || _member is not { } member
            || _group is not { } group)
        {
            return;
        }

        if (tracedOnly)
        {
            SubmitToTracedWorld(camera, member, group);
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

        _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

        // Matte, like every other body: cloth and skin do not shine. It keeps its ambient occlusion,
        // which is the one way it differs from a guard's.
        _device.SetMaterialOverride(new SurfaceMaterial(NoHistory: member.NoHistoryThisFrame, Matte: true));

        _owner.DrawSubsets(group, member, modelView);

        _device.SetMaterialOverride(SurfaceMaterial.Default);

        _device.ClearPoseInterpolation();
    }

    /// <summary>Puts the body into the traced world without drawing any of it.</summary>
    /// <param name="camera">The eye, for the same model-view the drawn path composes.</param>
    /// <param name="member">The body's frame state, which carries its posed mesh.</param>
    /// <param name="group">The body's geometry and the pack its textures come from.</param>
    /// <remarks>
    /// No device state is touched — no projection, no cull mode, no depth state, no pose interpolation —
    /// because none of it reaches a ray and every one of them would leak into whatever draws next. The
    /// pose does not need setting either: the vertex buffer this admits already holds the skinned result,
    /// written by <see cref="Reskin"/> on the animation tick rather than at the draw.
    /// </remarks>
    private void SubmitToTracedWorld(in Camera3D camera, Member member, Group group)
    {
        Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

        int subsets = 0;
        int textured = 0;
        string first = "none";

        foreach (MeshSubset subset in group.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && group.Instance is { } instance
                && group.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = _owner.Textures.Get(texture);
                if (diffuse != TextureHandle.None)
                {
                    textured++;
                    if (first == "none")
                    {
                        first = texture;
                    }
                }
            }

            subsets++;
            // Reflections only: the camera stands inside this body, so to every other ray -- the GI's and
            // the shadows' -- it is an occluder wrapped around the eye, which darkened whatever wall the
            // player faced into a cloud that followed the view. A primitive hidden in game should not
            // affect indirect lighting unless it asks to.
            _device.SubmitTracedMesh(
                member.Mesh, subset.IndexOffset, subset.IndexCount, modelView, diffuse, firstPersonOnly: true);
        }

        // Once, and only for what a reflection needs to know. A body that reflects as a flat silhouette
        // has run out of texture somewhere along a chain that crosses three files, and this says whether
        // it got out of THIS one: no texture here and the ray has nothing to shade from whatever the
        // renderer does later. Named rather than counted, so the name can be checked against the pack.
        if (!_tracedReported)
        {
            _tracedReported = true;
            _logger.Information(
                SoldierRenderer.LogCategory,
                $"Player body in the traced world: {subsets} subset(s), {textured} with a texture "
                    + $"(first '{first}'); instance {(group.Instance is null ? "absent" : "present")}, "
                    + $"pack {(group.Pack is null ? "absent" : "present")}.");
        }
    }

    private bool _tracedReported;

    /// <summary>Frees the body's mesh and forgets it, as the level goes.</summary>
    internal void Release()
    {
        if (_group is { } group && group.Mesh.IsValid)
        {
            _device.DestroyMesh(group.Mesh);
        }

        if (_member is { } member && member.Mesh.IsValid)
        {
            _device.DestroyMesh(member.Mesh);
        }

        _sim.PlayerBodyView = null;
        _sim.PlayerBodyModel = null;
        _group = null;
        _member = null;
        _ragdoll = null;
        _active = false;
        _sim.PlayerBodyClimbing = false;
        _sim.PlayerBodyShown = false;
        _sim.PlayerBodyFollowing = false;
        _sim.PlayerBodyUpperBodySequence = -1;
        _sim.PlayerBodyVisualLift = 0.0;
        _sim.PlayerBodyRootHeightAtStart = 0.0;
        _hasTickState = false;
        _captureTick = -1;
        _skinTick = -1;
    }
}
