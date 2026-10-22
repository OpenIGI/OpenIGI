using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Places the soldiers between ticks: each body's placement captured on the simulation tick, and the
/// frame's drawn placement, pose blend and hand-held weapon resolved between the last two of those.
/// </summary>
/// <remarks>
/// <para>
/// The two records a tick captures are the whole interface between the 30 Hz simulation and however fast
/// the display runs. With smoothing off, the tick's own values pass through verbatim, so the vanilla
/// picture is identical rather than close.
/// </para>
/// <para>
/// The corpses live here too: a ragdoll replaces nothing but the pose a body is drawn from and the
/// placement it is drawn at, so seeding one and stepping it are the same bookkeeping.
/// </para>
/// </remarks>
/// <param name="groups">The groups the renderer draws; shared, not copied.</param>
/// <param name="sim">The soldiers' simulation.</param>
/// <param name="logger">Where a ragdoll that cannot be seeded or has gone wrong is reported.</param>
internal sealed class SoldierFrameInterpolator(List<SoldierGroup> groups, SoldierSimulation sim, IOpenIgiLogger logger)
{
    // The one staged actor whose placement is traced each second, named by soldier id in
    // OPENIGI_TRACE_CUTSCENE_ACTOR. Read once: the check runs for every soldier on every tick.
    private static readonly string? TracedCutsceneActor = Environment.GetEnvironmentVariable("OPENIGI_TRACE_CUTSCENE_ACTOR");

    private readonly List<SoldierGroup> _groups = groups;
    private readonly SoldierSimulation _sim = sim;
    private readonly IOpenIgiLogger _logger = logger;

    // The frame alpha BeginFrame resolved, kept for the player's body, which draws once per frame.
    private float _frameAlpha = 1f;
    private bool _frameSmooth;

    /// <summary>Gets how far this frame sits between the last two ticks, as <see cref="BeginFrame"/> resolved it.</summary>
    public float FrameAlpha => _frameAlpha;

    /// <summary>Gets whether this frame is drawn between ticks rather than at the last one.</summary>
    public bool FrameSmooth => _frameSmooth;

    /// <summary>Resolves every soldier's drawn state for the frame about to be drawn.</summary>
    /// <param name="tickFraction">How far the frame sits between the last two ticks.</param>
    /// <param name="smooth">Whether the frame is drawn between ticks rather than at the last one.</param>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        float alpha = Math.Clamp(tickFraction, 0f, 1f);
        _frameAlpha = alpha;
        _frameSmooth = smooth;
        _sim.ForgetGroundSnaps();

        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                ResolveFrameState(member, alpha, smooth);
            }
        }
    }

    /// <summary>Computes one soldier's drawn placement, pose blend and motion for this frame.</summary>
    internal void ResolveFrameState(SoldierMember member, float alpha, bool smooth)
    {
        if (!member.HasTickState)
        {
            // Drawn before its first tick: stand exactly where the level put it.
            CaptureTickState(member);
        }

        // The rigid half. When nothing moved — or smoothing is off — the tick's own values pass
        // through verbatim, so the vanilla picture is not merely close but identical.
        Vector3d where = smooth && member.PrevTickWhere != member.TickWhere
            ? Vector3d.Lerp(member.PrevTickWhere, member.TickWhere, alpha)
            : member.TickWhere;

        Matrix3f orientation = smooth && member.PrevTickFacing != member.TickFacing
            ? Matrix3f.FromEngineEulerAngles(0f, 0f, Interpolation.LerpAngle(member.PrevTickFacing, member.TickFacing, alpha))
            : member.TickOrientation;

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;

        // The pose half. Smoothing draws 1-alpha of the previous tick's skin; the blend that described
        // LAST frame's pose is kept so the geometry pass can reconstruct last frame's vertex from the
        // same two streams. When the streams flipped since the last frame the old blend no longer names
        // the right pair, and the previous stream itself — the whole of last tick's pose — is the
        // closest expressible stand-in; by then last frame's blend was nearly there anyway.
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = smooth ? 1f - alpha : 0f;
        member.PoseFlippedSinceFrame = false;

        // A body's very first frame has no history anywhere on screen; say so rather than let the
        // resolve reproject into whatever stood there before.
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;

        // The held weapon rides the hand bone, interpolated the same way — rigid, so its previous
        // transform alone reprojects it exactly.
        if (member.Weapon is not null && member.HasHandState)
        {
            Vector3f hand;
            Matrix3f handRotation;
            if (smooth)
            {
                hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * alpha);
                handRotation = Interpolation.InterpolateRotation(member.PrevTickHandRotation, member.TickHandRotation, alpha);
            }
            else
            {
                hand = member.TickHandPosition;
                handRotation = member.TickHandRotation;
            }

            // M . v, so the transpose of the row-vector Transform (see DrawWeapon's remarks).
            Vector3f turned = orientation.Transposed().Transform(hand);
            member.PreviousWeapon = member.HasWeaponFrameState
                ? (member.DrawnWeaponOrigin, member.DrawnWeaponRotation)
                : null;
            member.DrawnWeaponOrigin = new Vector3d(
                where.X + (turned.X / WorldScene.UnitsPerMetre),
                where.Y + (turned.Y / WorldScene.UnitsPerMetre),
                where.Z + (turned.Z / WorldScene.UnitsPerMetre));
            member.DrawnWeaponRotation = orientation * handRotation;
            member.HasWeaponFrameState = true;
        }
    }

    /// <summary>Records a soldier's drawn placement for this tick, keeping the tick before.</summary>
    internal void CaptureTickState(SoldierMember member)
    {
        (Vector3d where, Matrix3f orientation) = TickPlacementOf(member);
        if (TracedCutsceneActor is { } traceActor
            && traceActor == member.Agent.View.Soldier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && _sim.TickCount % 30 == 0)
        {
            BoneClip? clip = member.Agent.View.Bones.SourceOf(0);
            _logger.Warning(SoldierRenderer.LogCategory + ".CutsceneActor",
                $"tick={_sim.TickCount} actor={traceActor} active={ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier)} "
                + $"scene={_sim.Cutscenes?.ActiveScene?.Id} staged={member.Agent.IsCutsceneActor} where={where} "
                + $"view={member.Agent.View.Position} clip={member.Agent.View.CurrentAnimation} time={clip?.Time} "
                + $"root={member.Agent.View.RootSample} delta={member.Agent.View.RootMotion()}");
        }
        float facing = SoldierSimulation.FacingOf(orientation);

        member.PrevTickWhere = member.HasTickState ? member.TickWhere : where;
        member.PrevTickFacing = member.HasTickState ? member.TickFacing : facing;
        member.TickWhere = where;
        member.TickFacing = facing;
        member.TickOrientation = orientation;

        if (member.Weapon is not null)
        {
            // The pose the BODY was drawn from, which for a corpse is the ragdoll's rather than the
            // rig's -- otherwise the weapon rides a hand that is no longer where the hand is drawn, and
            // hangs in the air over the body.
            BonePose pose = member.DrawnPose ?? member.Agent.View.UnrotatedPose();
            if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
            {
                Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
                Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];

                // Once the weapon is a body of its own it stops riding the hand and starts being placed
                // by the solver, which is what lets it come to rest on the floor rather than through it.
                // It is written into the same pair the hand fills, so the draw path, the interpolation
                // and the motion vectors all carry on unchanged.
                if (member.Ragdoll is { HasAttachment: true } armed)
                {
                    hand = armed.AttachmentPosition;
                    rotation = armed.AttachmentRotation;
                }
                member.PrevTickHandPosition = member.HasHandState ? member.TickHandPosition : hand;
                member.PrevTickHandRotation = member.HasHandState ? member.TickHandRotation : rotation;
                member.TickHandPosition = hand;
                member.TickHandRotation = rotation;
                member.HasHandState = true;
            }
        }

        member.HasTickState = true;
    }

    /// <summary>Where a soldier's body is drawn for the current tick, in metres, and its heading.</summary>
    /// <remarks>
    /// The placement <see cref="SoldierRenderer.Draw"/> used to derive per pass, moved to the tick so the ground probe
    /// runs once per soldier per tick instead of once per soldier per pass — the shadow cascades and
    /// the geometry pass then agree by construction.
    /// </remarks>
    internal (Vector3d Where, Matrix3f Orientation) TickPlacementOf(SoldierMember member)
    {
        SoldierView soldier = member.Agent.View;

        // A simulated corpse carries its own placement. Its pose is built in world space relative to the
        // origin frozen at the moment it died, with the body's heading folded into the root bone's own
        // rotation, so it is drawn there with no turn applied. Reporting the same pair every tick is
        // also what makes the between-tick blend a no-op for it -- the motion is all in the pose.
        if (member.Ragdoll is { } corpse)
        {
            return (corpse.Origin / WorldScene.UnitsPerMetre, Matrix3f.Identity);
        }

        // A dormant cinematic actor may still have an AI row and therefore a brain object, but its
        // authored staging wins while its conditional scene is active.  Letting the generic brain
        // branch below ground it simply reintroduced the seated-actor lift avoided at construction.
        if (member.Agent.IsCutsceneActor)
        {
            if (_sim.Cutscenes?.TryGetAttachedDrawTransform(
                soldier.Soldier, out Vector3d attached, out Matrix3f attachedOrientation) == true)
            {
                // Human_SetParent derives Human+1904 from the actor's already-staged Human+32. For a
                // seated actor that position is the model/pelvis origin, not gameplay feet. The probe's
                // Mission 1 trace fixes Jones at local (47.574,-2649.593,-866.474) inside heli 1502;
                // Human_AttachedMovement then consumes only the per-tick root DELTA into that local
                // offset. Adding the absolute Human+1300 sample here commits it a second time, lifting
                // Jones into the cabin roof until only his legs survive the depth test.
                return (attached, attachedOrientation);
            }

            // A graph inside a placed model parents the actor to that model instead (0x462390 -> 0x463310): mission 9's
            // intro office holds Anya where she was placed, so she keeps the staging below.
            // An actor with an AI graph is grounded on it every tick like any soldier, and 0x462390 then
            // sets Human+32 to that ground plus the clip's root-bone height (0x48A340). Mission 8's intro
            // guards were drawn at their authored origin or, once their idle clip moved them, at their
            // feet -- 3336 stood waist-deep in the ground beside the second truck.
            // Only once the brain HAS stood it there: before its first tick the position is still the authored
            // model origin, which for a set-dressed actor already sits a root height above the floor -- mission
            // 4's Anya at 1.98 m -- and adding the root height again stood her a whole hip-height up.
            if (!member.Agent.HasCutsceneGameplayMovement
                && _sim.Brains.TryGetValue(soldier, out Igi1HumanAi? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
                && !stagedBrain.GraphInParentFrame && stagedBrain.StoodOnGraph)
            {
                // The graph branch takes its height from the graph, which the brain has already applied.
                return (new Vector3d(
                    soldier.Position.X / WorldScene.UnitsPerMetre,
                    soldier.Position.Y / WorldScene.UnitsPerMetre,
                    (soldier.Position.Z + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
            }

            if (member.Agent.HasCutsceneGameplayMovement)
            {
                double groundedZ = _sim.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                return (new Vector3d(
                    soldier.Position.X / WorldScene.UnitsPerMetre,
                    soldier.Position.Y / WorldScene.UnitsPerMetre,
                    (groundedZ + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
            }

            if (member.Agent.HasCutsceneRootMotion)
            {
                return (soldier.Position / WorldScene.UnitsPerMetre, soldier.Orientation);
            }
            // The authored model origin is already staged (Anya HQ, vehicle briefing sets).
            // Adding an absolute root sample here lifts the whole set-relative performance.
            return (member.Agent.Position, soldier.Orientation);
        }

        // A soldier with a brain is drawn where its AI put it, lifted by the CLIP'S OWN ROOT HEIGHT.
        // That is the original's Human+1300 (see SoldierView.RootSample): every retail skeleton's
        // bone 0 rest translation is zero, so the pose puts the pelvis at the object origin and the
        // feet a metre below it, and the root track's Z is what bridges the two. Only the Z is taken:
        // the horizontal part of the root track is what MOVES the body — the mover consumes the same
        // track's per-tick delta — so adding it again would run the model a stride ahead of its feet.
        if (_sim.Brains.TryGetValue(soldier, out Igi1HumanAi? brain) && brain.HasGroundQuery)
        {
            // Snap the feet to whatever the collision puts under them, exactly as the static-soldier
            // SoldierSimulation.Stand() does. A level often authors a soldier a little below its floor; the query only
            // pulls UP onto a floor within the half-metre probe, so a soldier already on its floor
            // does not move.
            //
            // NOT a soldier its graph stands: 0x462390 takes an AI's height from the graph and casts no ray
            // at all, which the brain has already applied. Mission 12's castle guard sits on a crate edge in
            // clip 4 with his graph node on the floor; the snap found the crate half a metre up and drew him
            // floating over it, where the original draws him at node plus root height.
            double groundedZ = brain.HasGraph && brain.StoodOnGraph && !brain.GraphInParentFrame
                ? soldier.Position.Z
                : _sim.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                (groundedZ + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
        }

        if (_sim.Brains.ContainsKey(soldier))
        {
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                member.Agent.Position.Z), soldier.Orientation);
        }

        return (member.Agent.Position, soldier.Orientation);
    }

    /// <summary>The pose a body is skinned from this tick — its rig's, or its corpse's.</summary>
    /// <remarks>
    /// The single branch the whole feature costs the render path. A ragdoll's pose comes back out of
    /// <c>BonePose.Evaluate</c> like any other, so the skinner, the shaders and the previous-pose stream
    /// that feeds the temporal resolve are all untouched by it.
    /// </remarks>
    internal static BonePose PoseToDraw(SoldierMember member) =>
        member.Ragdoll?.Pose() ?? member.Agent.View.UnrotatedPose();

    /// <summary>Steps every soldier's corpse one tick, dropping any whose solver has gone wrong.</summary>
    public void StepRagdolls()
    {
        if (GameplayCVars.Ragdoll.Value)
        {
            RagdollWorld corpses = _sim.RagdollWorldForCorpses();
            int substeps = GameplayCVars.RagdollSubsteps.Value;
            double fall = GameplayCVars.RagdollGravity.Value;
            foreach (SoldierGroup group in _groups)
            {
                foreach (SoldierMember member in group.Soldiers)
                {
                    if (member.Ragdoll is not { } corpse)
                    {
                        continue;
                    }

                    corpse.Step(corpses, substeps, fall);

                    // A corpse whose solver has gone wrong is dropped rather than drawn: the retail
                    // death clip has been running underneath the whole time, so what the player sees is
                    // the original's own fall, not a body that vanished.
                    if (!corpse.IsValid)
                    {
                        member.Ragdoll = null;
                        _logger.Warning(SoldierRenderer.LogCategory, "A ragdoll left its own level; falling back to the death clip.");
                    }
                }
            }
        }
    }

    /// <summary>Records where each body stands and faces this tick, once the poses are settled.</summary>
    public void CaptureTickStates()
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                CaptureTickState(member);
            }
        }
    }

    /// <summary>
    /// Starts a simulated corpse from the pose a body was last drawn in, and pushes it with the round.
    /// </summary>
    /// <returns>The body, or <see langword="null"/> when ragdolls are off or the rig cannot drive one.</returns>
    /// <remarks>
    /// The origin has to be the placement the body was being drawn at — position, ground snap and the
    /// clip's own root height — or the corpse jumps at the instant the physics takes over. That is the
    /// same triple <see cref="TickPlacementOf"/> builds, in units rather than metres.
    /// </remarks>
    internal RagdollBody? SeedRagdoll(
        SoldierView view,
        Vector3d originInUnits,
        Matrix3f orientation,
        int bone,
        Vector3d travel,
        WeaponCategory category,
        Vector3d carriedVelocity,
        MefModel? model,
        SoldierHeldWeapon? weapon)
    {
        if (!GameplayCVars.Ragdoll.Value || _sim.RagdollRigFor(view.Bones.Set, model) is not { } rig)
        {
            return null;
        }

        RagdollBody body;
        try
        {
            body = RagdollBody.Seed(
                rig,
                view.UnrotatedPose(),
                originInUnits,
                orientation,
                WeaponBody(weapon),
                HumanAttachPoints.ThirdPersonWeapon);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"Could not seed a ragdoll: {ex.Message}");
            return null;
        }

        body.AddVelocity(carriedVelocity);
        RagdollImpulse.Apply(
            body,
            bone,
            travel,
            RagdollImpulse.MomentumOf(rig, category, GameplayCVars.RagdollForce.Value));
        return body;
    }

    /// <summary>The body a soldier's held weapon becomes once its hand is dead.</summary>
    /// <remarks>
    /// Measured from the weapon's own drawn geometry, so a pistol gets a pistol's capsule and a rifle a
    /// rifle's with nothing named. The vertices are in metres, because that is what the mesh builder
    /// scaled them to; the solver works in engine units.
    /// </remarks>
    private static RagdollBone? WeaponBody(SoldierHeldWeapon? weapon)
    {
        if (weapon is null || weapon.Geometry.Vertices.Length == 0)
        {
            return null;
        }

        Vector3f low = new(float.MaxValue);
        Vector3f high = new(float.MinValue);
        foreach (Vertex3D vertex in weapon.Geometry.Vertices)
        {
            low = new Vector3f(
                MathF.Min(low.X, vertex.Position.X),
                MathF.Min(low.Y, vertex.Position.Y),
                MathF.Min(low.Z, vertex.Position.Z));
            high = new Vector3f(
                MathF.Max(high.X, vertex.Position.X),
                MathF.Max(high.Y, vertex.Position.Y),
                MathF.Max(high.Z, vertex.Position.Z));
        }

        float scale = (float)WorldScene.UnitsPerMetre;
        return RagdollWeapon.FromBounds(low * scale, high * scale);
    }
}
