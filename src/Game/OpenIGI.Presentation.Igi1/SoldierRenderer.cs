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

namespace OpenIGI.Presentation.Igi1;

/// <summary>Draws a level's placed soldiers — each the model, skeleton and stand animation its own task declares.</summary>
/// <remarks>
/// <para>
/// The spawn is <c>0x456E50</c> + <c>0x45F890</c>: the bone object
/// on the declared <c>Bone Heirachy</c> skeleton, the declared <c>Stand Animation</c> (or 2) on bone 0,
/// the ordinary <c>Model</c> parameter resolved by name, and the facing a pure <c>Z(Gamma)</c>. The body
/// draws through <c>Human</c>'s ordinary render handler under the <b>world</b> projection —
/// <c>0x4D9620</c> leaves <c>+212</c> at 1, so <c>0x49F700</c>'s view-model block never rebuilds for it.
/// </para>
/// <para>
/// Soldiers sharing a <c>(model, skeleton, stand animation)</c> triple stand in the same pose, so the
/// CPU skin runs once per distinct triple and each soldier draws that mesh at its own position and
/// facing. The facing can live in the per-soldier model matrix because the pose is skinned with an
/// identity root — the root rotation only ever left-multiplies every bone (see
/// <see cref="IViewModelPresenter"/>).
/// </para>
/// </remarks>
public sealed class SoldierRenderer : IDisposable
{
    private const string LogCategory = "Soldier";
    // The one staged actor whose placement is traced each second, named by soldier id in
    // OPENIGI_TRACE_CUTSCENE_ACTOR. Read once: the check runs for every soldier on every tick.
    private static readonly string? TracedCutsceneActor = Environment.GetEnvironmentVariable("OPENIGI_TRACE_CUTSCENE_ACTOR");




    private readonly IGraphicsDevice _device;

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry? _magic;
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;
    private readonly List<Group> _groups = [];

    private readonly List<HeldWeapon> _weapons = [];
    private readonly Dictionary<string, HeldWeapon> _heldWeaponsByModel = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a presenter for a weapon a soldier holds, or null when the host supplies none.</summary>
    /// <remarks>
    /// A factory rather than a type, because the renderer that draws a held weapon is title content and
    /// lives in the title's own project: naming it here would put I.G.I. 1 content back in
    /// <c>OpenIGI.Presentation</c>, which is what this class is waiting to leave. Null is a soldier that
    /// draws no weapon, which is what a test harness and a backdrop both want.
    /// </remarks>
    private readonly Func<IHeldWeaponPresenter>? _heldWeapons;








    private readonly Dictionary<AiStationaryGun, HeldWeapon?> _stationaryWeaponVisuals = [];

    // The weapon models the vehicles' guns carry, by model, and where the runtime poses each gun (0x470050).
    private readonly Dictionary<string, HeldWeapon?> _vehicleWeaponVisuals = new(StringComparer.OrdinalIgnoreCase);
    private Func<IEnumerable<VehicleGunVisual>>? _vehicleGuns;
    private readonly HashSet<int> _reportedStationaryVisuals = [];











































    // The sound 0x45D3C0 adds to a soldier's grunt when a knife made the hit.
    private const string KnifeStabSound = "stab_01";





    private bool _disposed;

    // The soldiers' simulation: their brains, their bodies' state, their weapons and what they hear and see.
    private readonly SoldierSimulation _sim;

    /// <summary>Gets the soldiers' simulation, which the renderer draws.</summary>
    public SoldierSimulation Simulation => _sim;

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>Owned by the shell: one pack serves every renderer that loads a texture.</remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Gets or sets whether the followed body is actually drawn this frame.</summary>
    /// <value>
    /// <see langword="true"/> once the camera has left the head far enough for a body to be a body
    /// rather than the inside of a skull filling the screen.
    /// </value>
    /// <remarks>
    /// Deliberately separate from <c>FollowPlayerBody</c>'s latch, and read per FRAME rather than
    /// per tick. The follow keeps placing, animating and capturing the tick pair for the whole of the
    /// move between the two views; only the draw is switched, so crossing the threshold cannot be
    /// mistaken for the body appearing out of nowhere and reset the interpolation and temporal history
    /// that make it move smoothly.
    /// </remarks>
    public bool PlayerBodyFollowVisible { get; set; } = true;

    /// <summary>Raised where a round met a body, so the blood can be thrown there.</summary>
    /// <remarks>
    /// <c>Human</c>'s damage handler (<c>0x45F930</c>) calls the blood (<c>0x463AD0</c>) as its very
    /// first act, <b>before</b> the invulnerability and instant-death gates and before any damage is
    /// applied. So this is raised on contact, not on damage: a friendly round or one on an invulnerable
    /// guard still makes it bleed, exactly as it does in the original. The first argument identifies
    /// the body, whose two particle systems the effect runtime keeps and reuses.
    /// </remarks>
    public Action<object, Vector3d, Vector3f>? BloodSpray { get; set; }

    /// <summary>Gets a value indicating whether the player's third-person body is posed and drawing.</summary>
    /// <remarks>Set once the player has died and <see cref="KillPlayerBody"/> has run; the death camera reads it.</remarks>
    public bool IsPlayerBodyActive => _playerBodyActive;

    /// <summary>Gets where the player's corpse actually is, in engine units — what the death camera frames.</summary>
    /// <value>
    /// The body's centre of mass, or <see langword="null"/> when no ragdoll is running — in which case
    /// the death camera's own fixed point off the authoritative body is still the right answer.
    /// </value>
    /// <remarks>
    /// Without this the camera keeps looking at the point the retail clip moved the body to, which once
    /// physics is drawing it somewhere else means framing an empty patch of floor.
    /// </remarks>
    public Vector3d? PlayerRagdollCentre => _playerRagdoll?.Centre;
    // The player's own third-person body -- Jones, the rig the first-person view never shows. It is NOT a
    // guard: it has no AI, is not in _groups, and the soldier tick and draw loops never touch it. It loads
    // with the level (LoadPlayerBody), stands invisibly until the player dies, then poses the death clip
    // (KillPlayerBody) and is drawn (DrawPlayerBody) once the death camera has pulled back off the eye.
    // Reuses the same BuildGroup / re-skin / draw machinery a soldier's body does. See HumanPlayerBody.
    // The player's own corpse, when g_ragdoll is on. Exactly the soldier arrangement: the retail death
    // clip keeps running underneath and keeps moving the authoritative body, and this replaces only the
    // pose and the placement that are drawn.
    private RagdollBody? _playerRagdoll;
    private Group? _playerBodyGroup;
    private Member? _playerBodyMember;
    private bool _playerBodyActive;






    private float _playerBodyFacing;
    private AiSoldier.GroundQuery? _playerBodyGround;

    // The player body's own tick-state pair and frame bookkeeping -- the same smoothing a soldier's
    // Member carries, kept as fields because there is only ever one of it.
    private Vector3d _playerBodyTickWhere;
    private Vector3d _playerBodyPrevTickWhere;
    private bool _playerBodyHasTickState;
    private int _playerBodyCaptureTick = -1;
    private int _playerBodySkinTick = -1;

    // The frame alpha BeginFrame resolved, kept for DrawPlayerBody which runs once per frame.
    private float _frameAlpha = 1f;
    private bool _frameSmooth;

    /// <summary>Initializes a new instance of the <see cref="SoldierRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <param name="magic">The retail multipart-object table, used to attach gun-flame tasks.</param>
    /// <param name="heldWeapons">
    /// Builds a presenter for a weapon a soldier holds, or null for a renderer that draws none. A
    /// factory rather than a type because the renderer behind it is title content and lives in the
    /// title's own project.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoldierRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        IOpenIgiLogger? logger = null,
        MagicObjectRegistry? magic = null,
        Func<IHeldWeaponPresenter>? heldWeapons = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _magic = magic;
        _heldWeapons = heldWeapons;
        _logger = logger ?? NullLogger.Instance;
        _sim = new SoldierSimulation(_logger);
        _sim.SpawnedBody = (generator, roster, fresh, dead) =>
        {
            Group group = _groups.First(candidate => ReferenceEquals(candidate.Roster, roster));
            Member member;
            try
            {
                member = NewMember(group, fresh);
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
            {
                _logger.Warning(LogCategory, $"{generator.Name} could not upload its cloned Human: {ex.Message}");
                return false;
            }

            HeldWeapon? weapon = null;
            if (dead.WeaponType is { } weaponType)
            {
                _heldWeaponsByModel.TryGetValue(weaponType.GunModel, out weapon);
            }

            member.Weapon = weapon;
            group.AddDrawn(member);
            Count++;
            return true;
        };
        _sim.BodyFell = fall =>
        {
            if (FindMember(fall.View) is not { } member)
            {
                return;
            }

            member.Ragdoll = SeedRagdoll(
                fall.View,
                fall.Origin,
                fall.View.Orientation,
                fall.Bone,
                fall.Travel,
                fall.Category,
                fall.Velocity,
                _sim.FindModel(fall.View),
                weapon: null);
            if (fall.DroppedWeapon)
            {
                member.Weapon = null;
            }
        };
        _sim.MountSettled = (view, brain) =>
        {
            if (FindMember(view) is { } member)
            {
                UpdateMountedWeapon(member, brain);
            }
        };
    }

    /// <summary>Gets the number of soldiers that resolved and will draw.</summary>
    public int Count { get; private set; }

    /// <summary>Draws a selection glow around one soldier.</summary>
    /// <param name="index">The soldier's index in the order <see cref="SoldierSimulation.Snapshot"/> fills.</param>
    /// <param name="camera">The eye the frame is drawn from.</param>
    /// <param name="colour">The glow colour.</param>
    /// <param name="radius">How far the glow reaches past the silhouette, in pixels.</param>
    /// <param name="throughWalls">Whether it ignores the depth buffer.</param>
    /// <returns><see langword="true"/> when something was drawn.</returns>
    /// <remarks>
    /// <para>
    /// The pose is handed to the device the same way the ordinary draw hands it over, because a
    /// soldier's mesh is uploaded as two poses the shader blends between. Without it the glow would
    /// stand in whichever pose was uploaded last and lag a step behind the body inside it.
    /// </para>
    /// <para>
    /// The walk mirrors <see cref="SoldierSimulation.Snapshot"/> exactly — group, then member — because that walk is what
    /// defines the handle the inspector asks with.
    /// </para>
    /// </remarks>
    public bool DrawOutline(int index, in Camera3D camera, Bgra32 colour, float radius, bool throughWalls)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (index < 0)
        {
            return false;
        }

        int seen = 0;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (seen++ != index)
                {
                    continue;
                }

                if (!member.HasFrameState)
                {
                    return false;
                }

                _outlineRanges.Clear();
                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount > 0)
                    {
                        _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
                    }
                }

                if (_outlineRanges.Count == 0)
                {
                    return false;
                }

                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);
                _device.DrawOutline(
                    member.Mesh,
                    CollectionsMarshal.AsSpan(_outlineRanges),
                    camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation),
                    colour,
                    radius,
                    throughWalls);
                _device.ClearPoseInterpolation();
                return true;
            }
        }

        return false;
    }

    /// <summary>Draws a team-coloured through-wall glow over every soldier the last binocular scan marked.</summary>
    /// <remarks>
    /// Reuses the same inverted-hull selection glow as <see cref="DrawOutline"/>, keyed by the contacts
    /// <see cref="SoldierSimulation.BinocularTargets"/> cached this frame, so a scanned enemy stays legible through cover
    /// while the remastered optics are up. Must run inside the scene-capture pass, before the resolve, so
    /// the glow carries the lens and bloom with the rest of the world.
    /// </remarks>
    /// <param name="camera">The eye the scene is being drawn through.</param>
    /// <param name="radiusPixels">The glow's screen-space radius, in pixels.</param>
    public void DrawBinocularOutlines(in Camera3D camera, float radiusPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.BinocularDetected.Count == 0)
        {
            return;
        }

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!member.HasFrameState
                    || !_sim.BinocularDetected.TryGetValue(member.Agent.View, out BinocularMarkerColor marker))
                {
                    continue;
                }

                _outlineRanges.Clear();
                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount > 0)
                    {
                        _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
                    }
                }

                if (_outlineRanges.Count == 0)
                {
                    continue;
                }

                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);
                _device.DrawOutline(
                    member.Mesh,
                    CollectionsMarshal.AsSpan(_outlineRanges),
                    camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation),
                    SoldierSimulation.MarkerGlow(marker),
                    radiusPixels,
                    throughWalls: true);
                _device.ClearPoseInterpolation();
            }
        }
    }











    internal bool IsInViewForTesting(SoldierView view) => FindMember(view)?.Agent.InView ?? false;

    internal string DescribeViewForTesting(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.Agent.View, view) && _lastObservedCamera is { } camera)
                {
                    Matrix4f mv = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);
                    BonePose pose = view.Pose();
                    string eye = HumanAttachPoints.AiEye < pose.BoneCount ? pose.Positions[HumanAttachPoints.AiEye].ToString() : "-";
                    return $"lowestZ={group.Body.LowestVertexZ:0.000} eyeOffset={eye} viewZ={view.Position.Z / 4096.0:0.000} drawnZ={member.DrawnWhere.Z:0.000} " +
                        $"cam={camera.Position} viewPos=({mv.M41:0.0},{mv.M42:0.0},{mv.M43:0.0})";
                }
            }
        }

        return "-";
    }

    private Camera3D? _lastObservedCamera;






    /// <summary>Builds the drawable soldiers from a loaded level.</summary>
    /// <param name="mission">The mission whose packs hold the models and animation sets.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="terrain">The level's terrain, for standing soldiers on the ground it declares.</param>
    /// <param name="cast">The placed-object ray cast, for soldiers standing on floors and platforms.</param>
    /// <returns>The number of soldiers that resolved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int Load(LoadedMission mission, GameTask levelRoot, IGroundHeightField? terrain = null, WorldRayCast? cast = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        _sim.Terrain = terrain;
        _sim.Cast = cast;

        Dictionary<(string Model, int Skeleton, int Stand), Group> groups = [];
        foreach (HumanSoldierTask soldier in levelRoot.Descendants().OfType<HumanSoldierTask>())
        {
            if (!mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set) || set is null)
            {
                _logger.Warning(
                    LogCategory,
                    $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, which no pack loaded a set for.");
                continue;
            }

            SoldierView view = new(soldier, set);
            if (soldier.StandAnimation >= 0 && !view.DeclaredStandAnimationFound)
            {
                // The original's own wording (0x456E50).
                _logger.Warning(LogCategory, $"Animation #{soldier.StandAnimation} not found");
            }

            (string, int, int) key = (soldier.Model, soldier.BoneHierarchy, view.StandAnimation);
            if (!groups.TryGetValue(key, out Group? group))
            {
                group = BuildGroup(mission, soldier.Model, view);
                if (group is null)
                {
                    continue;
                }

                groups[key] = group;
                _groups.Add(group);
                _sim.Roster.Add(group.Roster);
            }

            // A human staged beside a CutScene keeps the authored object origin.  Gameplay humans are
            // grounded by their mover on the first ticks, but cinematic performers deliberately sit,
            // crouch or ride at offsets from nearby geometry.  Running the gameplay stand probe over
            // Anya, for example, put the lowest vertex of her seated clip on the control-room floor and
            // lifted the entire actor through the camera framing. Do not add the fallback standing
            // clip's root height here either: the task authors the cinematic pelvis origin, and the real
            // seated clip is selected only after the timeline starts.
            bool cutsceneActor = CinematicActor.IsStaged(soldier);
            Vector3d placement = cutsceneActor
                ? soldier.Position / WorldScene.UnitsPerMetre
                : SoldierSimulation.Stand(soldier, group.Roster, terrain, cast);
            Member member;
            try
            {
                member = NewMember(group, new SoldierAgent(view, placement) { IsCutsceneActor = cutsceneActor });
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            group.Add(member);
            if (group.Body.Model is { NativeMarkerBound: > 0f } model
                && float.IsFinite(model.NativeMarkerBound))
            {
                _sim.DetectorMarkerScaleMetres[view] =
                    model.NativeMarkerBound / (float)WorldScene.UnitsPerMetre;
            }
            Count++;
        }

        _logger.Information(
            LogCategory,
            $"{Count} soldier(s) over {_groups.Count} distinct (model, skeleton, animation) triple(s).");

        // The first player round must not pay for parsing every candidate's collision mesh, and the
        // first lethal round must not pay for measuring the animation corpus and preparing a ragdoll
        // rig. Both operations are immutable mission data; doing them here moves their cost to the
        // already-visible mission hand-over instead of turning a Dragunov hit into a 2–5 FPS hitch.
        WarmHitTestCaches();
        return Count;
    }

    /// <summary>Makes a soldier's own drawing: its first skin, uploaded to both of its GPU streams.</summary>
    /// <param name="group">The soldier's group, whose geometry and model it shares.</param>
    /// <param name="agent">The soldier.</param>
    /// <returns>The member, not yet added to the group.</returns>
    /// <exception cref="InvalidOperationException">The device could not upload the mesh.</exception>
    private Member NewMember(Group group, SoldierAgent agent)
    {
        // Its own mesh, so it can hold its own pose (see Member) -- animated, so each tick's
        // re-skin replaces the vertices in place and the pose replaced becomes the previous-pose
        // stream the motion vectors and the between-tick blending read.
        BonePose initialPose = agent.View.UnrotatedPose();
        Vertex3D[] initialSkin = new Vertex3D[group.Geometry.Vertices.Length];
        group.Geometry.Vertices.CopyTo(initialSkin, 0);
        bool initialSkinReady = false;
        if (group.Body.Model is { } skinModel)
        {
            try
            {
                MefSkinner.Skin(
                    skinModel,
                    initialPose,
                    (float)(1.0 / WorldScene.UnitsPerMetre),
                    initialSkin);
                initialSkinReady = true;
            }
            catch (ArgumentException)
            {
                // Keep the raw mesh if a malformed model cannot be skinned. Its collision path falls
                // back to the authoritative pose until two valid animated uploads establish a pair.
                initialSkinReady = false;
            }
        }

        // Both GPU streams begin with this same skin. A smoothed player shot can therefore use
        // a matching CPU pose pair even on the very first rendered frame; seeding them with raw
        // geometry would make the displayed blend untraceable until a second animation tick.
        MeshHandle own = _device.CreateAnimatedMesh(initialSkin, group.Geometry.Indices);
        return new Member(agent, own)
        {
            DrawnPose = initialPose,
            SkinScratch = initialSkin,
            SkinnedPose = initialSkinReady ? initialPose : null,
            PreviousSkinnedPose = initialSkinReady ? initialPose : null,
        };
    }

    /// <summary>Primes immutable hit-test and corpse-rig data before the first round can arrive.</summary>
    /// <remarks>
    /// Collision geometry and ragdoll rigs are shared by every member of a group. They used to be
    /// created by the first shot/death that touched a group, which made a normal first kill look like a
    /// render stall. A malformed optional collision chunk still falls back to the cylinder path; it must
    /// not make mission loading fail.
    /// </remarks>
    private void WarmHitTestCaches()
    {
        int collisionMeshes = 0;
        int ragdollRigs = 0;
        foreach (Group group in _groups)
        {
            try
            {
                if (group.Body.CollisionMesh is not null)
                {
                    collisionMeshes++;
                }
            }
            catch (PackFormatException ex)
            {
                _logger.Warning(LogCategory, $"A soldier collision mesh was not usable: {ex.Message}");
            }

            if (!GameplayCVars.Ragdoll.Value || group.Body.Model is null || group.Soldiers.Count == 0)
            {
                continue;
            }

            if (_sim.RagdollRigFor(group.Soldiers[0].Agent.View.Bones.Set, group.Body.Model) is not null)
            {
                ragdollRigs++;
            }
        }

        _logger.Information(
            LogCategory,
            $"Prepared {collisionMeshes} soldier collision mesh(es) and {ragdollRigs} corpse rig(s) before play.");
    }


    /// <summary>
    /// Names the soldier under a crosshair ray by tracing its <b>skinned collision mesh</b> — the same
    /// geometry a round is scored against, so the readout hits exactly where a shot would.
    /// </summary>
    /// <param name="originUnits">The eye, in world units.</param>
    /// <param name="direction">The look direction; need not be unit length.</param>
    /// <param name="reachUnits">How far to look, in world units.</param>
    /// <param name="name">Receives the struck soldier's model instance name.</param>
    /// <param name="distanceUnits">Receives how far along the ray it sits.</param>
    /// <param name="feetUnits">Receives the struck soldier's feet position, in world units.</param>
    /// <param name="zone">Receives the hit zone the round would score, or <c>-1</c> when none.</param>
    /// <returns><see langword="false"/> when the crosshair is not on a soldier's body.</returns>
    public bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone)
    {
        name = string.Empty;
        distanceUnits = double.MaxValue;
        feetUnits = default;
        zone = -1;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        Vector3d end = new(
            originUnits.X + (dir.X * reachUnits),
            originUnits.Y + (dir.Y * reachUnits),
            originUnits.Z + (dir.Z * reachUnits));
        bool found = false;

        foreach (Group group in _groups)
        {
            if (group.Body.CollisionMesh is not { } mesh)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.Agent.View;
                if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }
                double snappedZ = _sim.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                Vector3d feet = new(view.Position.X, view.Position.Y, snappedZ);

                // Broad phase: a generous body sphere, so a ray nowhere near a soldier never touches its mesh.
                Vector3d centre = new(feet.X, feet.Y, feet.Z + SoldierSimulation.PickChestUnits);
                if (!SoldierSimulation.RaySphere(originUnits, dir, centre, SoldierSimulation.PickCullRadiusUnits, reachUnits, out _))
                {
                    continue;
                }

                Matrix3f m = view.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                if (HumanHitTest.TryCastMesh(
                        originUnits, end, feet, facing, mesh, view.Pose(), view.RootSample.Z, out HumanHit h)
                    && h.Distance < distanceUnits)
                {
                    distanceUnits = h.Distance;
                    name = group.Instance?.Name ?? "soldier";
                    feetUnits = feet;
                    zone = h.Zone;
                    found = true;
                }
            }
        }

        return found;
    }





    /// <summary>Puts each soldier's own weapon model in its hand.</summary>
    /// <param name="mission">The mission whose packs hold the weapon models.</param>
    /// <param name="roster">The weapon table, which maps a <c>WEAPON_ID_*</c> to the model that draws it.</param>
    /// <returns>How many soldiers ended up holding something.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three reads join up here. <b>What a soldier carries is authored</b>, as ordinary <c>Gun</c> child
    /// tasks of the <c>HumanSoldier</c> carrying a <c>WEAPON_ID_*</c> — the same mechanism that arms the
    /// player — and the <b>first</b> of them is the one in hand, because everything that asks a human what
    /// it is holding takes slot 0 of the list at <c>Human+1220</c> (see
    /// <see cref="PlayerLoadout.WeaponsOf"/>). <b>Which model</b> comes from that id's row of the weapon
    /// table, whose <c>GunModel</c> field is what the world draws it with. And <b>where it hangs</b> is
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/>, bone 22, which <c>0x4600C0</c>'s third-person arm
    /// reads out of the body's own palette at <c>+2928</c>/<c>+2940</c>.
    /// </para>
    /// <para>
    /// <b>Correction to the milestone's own framing.</b> The weapon is <em>not</em> resolved from the
    /// soldier's <c>AIType</c>. The AK in <c>AITYPE_PATROL_AK</c> is part of the <c>settings.qsc</c> key
    /// <c>(AIType, Difficulty)</c>, and a level-1 guard whose type says AK routinely carries an MP5SD —
    /// because its own child task says so. Nothing in the engine maps a type to a model.
    /// </para>
    /// </remarks>
    public int Arm(LoadedMission mission, WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(roster);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _heldWeaponsByModel.Clear();

        Dictionary<string, HeldWeapon?> cache = new(StringComparer.OrdinalIgnoreCase);
        _stationaryWeaponVisuals.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
        _reportedStationaryVisuals.Clear();
        if (_sim.Interactions is { } interactions)
        {
            foreach (AiStationaryGun gun in interactions.Items
                .Select(static item => item.StationaryGun)
                .OfType<AiStationaryGun>())
            {
                if (!roster.TryGetByScriptId(gun.WeaponId, out WeaponType weapon))
                {
                    gun.BindWeapon(null);
                    _stationaryWeaponVisuals[gun] = null;
                    _logger.Warning(
                        LogCategory + ".Gunner",
                        $"task={gun.TaskId} weapon={gun.WeaponId} is not in the weapon table.");
                    continue;
                }

                gun.BindWeapon(weapon);

                // 0x478BA0 fires a mounted gun from its own model's magic vertex 2 -- the origin when
                // the model has no such vertex -- because the stand passes it no eye to start from.
                if (mission.TryGetModel(weapon.GunModel, out MefModel? gunModel)
                    && gunModel is not null
                    && gunModel.TryGetMagicVertex(2, out Vector3f muzzle))
                {
                    gun.MuzzleVertex = muzzle;
                }

                if (!cache.TryGetValue(weapon.GunModel, out HeldWeapon? held))
                {
                    held = BuildWeapon(mission, weapon.GunModel);
                    cache[weapon.GunModel] = held;
                }

                _stationaryWeaponVisuals[gun] = held;
                _logger.Information(
                    LogCategory + ".Gunner",
                    $"task={gun.TaskId} weapon={weapon.ScriptId} model={weapon.GunModel} "
                        + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) "
                        + $"visual={(held is null ? "missing" : "ready")}");
            }
        }

        int armed = 0;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(member.Agent.View.Soldier);

                // Human inventory scan 0x44FBA0 examines every carried Weapon task, not only slot 0.
                // Category 4 publishes HumanAI+15869, which is the patrol grenade chooser's gate.
                bool hasGrenade = carried.Any(item =>
                    roster.TryGetByScriptId(item.WeaponId, out WeaponType candidate)
                    && candidate.Category == WeaponCategory.Grenade);
                if (_sim.Brains.TryGetValue(member.Agent.View, out AiSoldier? brain))
                {
                    brain.SetGrenadeAvailable(hasGrenade);
                }

                if (carried.Count == 0)
                {
                    continue;
                }

                // Slot 0 of the weapon list, which is the first child that named a weapon.
                string weaponId = carried[0].WeaponId;
                if (!roster.TryGetByScriptId(weaponId, out WeaponType weapon))
                {
                    _logger.Warning(LogCategory, $"'{weaponId}' is not in the weapon table.");
                    continue;
                }

                // The Human's held Weapon task exists independently of the third-person mesh. Combat
                // reads that task directly (IGI1 0x48D8E0; IGI2 HumanAIContent_RunFire), so a missing or
                // unrenderable model must not turn an armed guard into an unarmed AI that only stares.
                member.Agent.WeaponType = weapon;
                member.Agent.CarriedWeaponType = weapon;
                member.Agent.AmmoInClip = weapon.RoundsPerClip;

                if (!cache.TryGetValue(weapon.GunModel, out HeldWeapon? held))
                {
                    held = BuildWeapon(mission, weapon.GunModel);
                    cache[weapon.GunModel] = held;
                }

                if (held is null)
                {
                    continue;
                }

                member.Weapon = held;
                _heldWeaponsByModel[weapon.GunModel] = held;
                member.CarriedWeapon = held;
                armed++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{armed} of {Count} soldier(s) hold a weapon model, over {cache.Values.Count(w => w is not null)} " +
            $"distinct model(s).");
        return armed;
    }

    /// <summary>Loads the weapon models the vehicles' guns carry and takes where the runtime poses them.</summary>
    /// <param name="mission">The mission whose packs hold the models.</param>
    /// <param name="weaponModels">Every vehicle gun's weapon model (magic-object argument 1).</param>
    /// <param name="guns">Each gun's weapon object as this tick posed it.</param>
    /// <remarks>
    /// An <c>AIStationaryGun</c> on a vehicle carries its weapon as an object of its own (<c>+452</c>), which the
    /// mount model does not include: a T80's turret has no barrel until its cannon is drawn on it. The weapon's
    /// own children come with it, the M2HB's gun flame among them.
    /// </remarks>
    public void ArmVehicleGuns(
        LoadedMission mission,
        IEnumerable<string> weaponModels,
        Func<IEnumerable<VehicleGunVisual>> guns)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(weaponModels);
        ArgumentNullException.ThrowIfNull(guns);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _vehicleWeaponVisuals.Clear();
        foreach (string model in weaponModels)
        {
            if (!_vehicleWeaponVisuals.ContainsKey(model))
            {
                _vehicleWeaponVisuals[model] = BuildWeapon(mission, model);
            }
        }

        _vehicleGuns = _vehicleWeaponVisuals.Count > 0 ? guns : null;
    }

    private HeldWeapon? BuildWeapon(LoadedMission mission, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' is in no pack this mission opened.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' has nothing to draw.");
            return null;
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        IHeldWeaponPresenter? presentation = null;
        if (_magic is not null && _heldWeapons?.Invoke() is { } candidate)
        {
            if (candidate.Load(mission, modelName))
            {
                presentation = candidate;
            }
            else
            {
                candidate.Dispose();
            }
        }

        HeldWeapon held = new(geometry, mesh, instance, pack, presentation);
        _weapons.Add(held);
        return held;
    }





    /// <summary>Swaps the rendered held model to the live stationary gun while a gunner is mounted.</summary>
    private void UpdateMountedWeapon(Member member, AiSoldier brain)
    {
        if (brain.MountedGun is { } mounted
            && mounted.Weapon is not null
            && _stationaryWeaponVisuals.TryGetValue(mounted, out HeldWeapon? visual)
            && visual is not null)
        {
            member.Weapon = visual;
            member.RenderedMountedGun = mounted;
            return;
        }

        if (member.RenderedMountedGun is not null)
        {
            member.Weapon = member.CarriedWeapon;
            member.RenderedMountedGun = null;
        }
    }

    /// <summary>The body a soldier's held weapon becomes once its hand is dead.</summary>
    /// <remarks>
    /// Measured from the weapon's own drawn geometry, so a pistol gets a pistol's capsule and a rifle a
    /// rifle's with nothing named. The vertices are in metres, because that is what the mesh builder
    /// scaled them to; the solver works in engine units.
    /// </remarks>
    private static RagdollBone? WeaponBody(HeldWeapon? weapon)
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


    private Group? FindGroup(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.Agent.View, view))
                {
                    return group;
                }
            }
        }

        return null;
    }


    private Member? FindMember(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.Agent.View, view))
                {
                    return member;
                }
            }
        }

        return null;
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
    private RagdollBody? SeedRagdoll(
        SoldierView view,
        Vector3d originInUnits,
        Matrix3f orientation,
        int bone,
        Vector3d travel,
        WeaponCategory category,
        Vector3d carriedVelocity,
        MefModel? model,
        HeldWeapon? weapon)
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
            _logger.Warning(LogCategory, $"Could not seed a ragdoll: {ex.Message}");
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










    /// <summary>
    /// The soldier model's bounding radius the draw callback's frustum test scales by 1.41, in metres.
    /// </summary>
    /// <remarks>
    /// An approximation: retail reads it from the model's LOD record (<c>+60</c>), which is not parsed yet, and
    /// the skinned geometry's own bounds are not in posed model space. A metre about the model origin -- the
    /// pelvis, 0.8 m under the eye bone -- covers a standing body, and only decides guards at the screen edge.
    /// </remarks>
    private const float SkinCullRadiusMetres = 1.0f;

    /// <summary>How far from the camera a drawn soldier's eye bone is still refreshed, in metres.</summary>
    /// <remarks>
    /// Measured on the retail game: past about 51 m the soldier is drawn at its fifth LOD (0x4CFF70 returns
    /// 4; 50.6 m gave 3 and 51.9 m gave 4), and that LOD's skinning leaves the eye bone's palette entry as it
    /// was -- a guard skinned at 65 m kept its rest-pose eye, one at 50 m followed its body. The LOD pick is by
    /// view depth scaled by the field of view, so zoomed optics would reach further; that is not modelled.
    /// </remarks>
    private const double EyeRefreshDistanceMetres = 51.0;

    /// <summary>
    /// Records which soldiers the main view draws closely enough to refresh their eyes this frame -- the
    /// frustum sphere test retail's draw callback makes before it skins a body (<c>0x4D06C0</c>, the model's
    /// radius times 1.41), and the LOD range within which that skinning reaches the eye bone.
    /// </summary>
    /// <param name="camera">The frame's main camera.</param>
    /// <param name="projection">Its projection.</param>
    /// <remarks>Only the main view counts: the shadow cascades' light cameras skin nothing in the original.</remarks>
    public void ObserveView(in Camera3D camera, in Matrix4f projection)
    {
        _lastObservedCamera = camera;
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(projection);
        BoundingSphere test = new(Vector3f.Zero, SkinCullRadiusMetres * 1.41f);
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                member.Agent.InView = ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier)
                    && member.HasFrameState
                    && Vector3d.Distance(member.DrawnWhere, camera.Position) <= EyeRefreshDistanceMetres
                    && frustum.SphereVisible(camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation), test);
            }
        }
    }






    /// <summary>Records a soldier's drawn placement for this tick, keeping the tick before.</summary>
    private void CaptureTickState(Member member)
    {
        (Vector3d where, Matrix3f orientation) = TickPlacementOf(member);
        if (TracedCutsceneActor is { } traceActor
            && traceActor == member.Agent.View.Soldier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && _sim.TickCount % 30 == 0)
        {
            BoneClip? clip = member.Agent.View.Bones.SourceOf(0);
            _logger.Warning(LogCategory + ".CutsceneActor",
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
    /// The placement <see cref="Draw"/> used to derive per pass, moved to the tick so the ground probe
    /// runs once per soldier per tick instead of once per soldier per pass — the shadow cascades and
    /// the geometry pass then agree by construction.
    /// </remarks>
    private (Vector3d Where, Matrix3f Orientation) TickPlacementOf(Member member)
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
                && _sim.Brains.TryGetValue(soldier, out AiSoldier? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
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
        if (_sim.Brains.TryGetValue(soldier, out AiSoldier? brain) && brain.HasGroundQuery)
        {
            // Snap the feet to whatever the collision puts under them, exactly as the static-soldier
            // SoldierSimulation.Stand() does. A level often authors a soldier a little below its floor; the query only
            // pulls UP onto a floor within the half-metre probe, so a soldier already on its floor
            // does not move.
            double groundedZ = _sim.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
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
    private static BonePose PoseToDraw(Member member) =>
        member.Ragdoll?.Pose() ?? member.Agent.View.UnrotatedPose();

    /// <summary>Resolves where every soldier is drawn for one rendered frame.</summary>
    /// <param name="tickFraction">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <param name="smooth">
    /// Whether to draw between the two tick states. Off, every value is this tick's verbatim — the
    /// original's stepped presentation, which the vanilla reference captures depend on being exact.
    /// </param>
    /// <remarks>
    /// Once per frame, before any pass draws. <see cref="Draw"/> runs once per shadow cascade and once
    /// for the geometry pass, so anything resolved per frame — the blend, the ground snap, the motion
    /// bookkeeping — has to be resolved here or the passes would disagree with each other.
    /// </remarks>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float alpha = Math.Clamp(tickFraction, 0f, 1f);
        _frameAlpha = alpha;
        _frameSmooth = smooth;
        _sim.ForgetGroundSnaps();

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                ResolveFrameState(member, alpha, smooth);
            }
        }

        // The player's own body is not in _groups, so it resolves here rather than in the loop -- and it
        // has to resolve HERE rather than inside its draw, because its draw now runs once per shadow
        // cascade as well as once for the geometry pass, and the frame bookkeeping below records what was
        // on screen LAST frame. Doing it per draw would make a body cast three shadows' worth of history
        // and report itself as never having moved.
        ResolvePlayerBodyFrameState();
    }

    /// <summary>Computes one soldier's drawn placement, pose blend and motion for this frame.</summary>
    private void ResolveFrameState(Member member, float alpha, bool smooth)
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

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
    /// <remarks>
    /// Called once per shadow cascade and once for the geometry pass; everything per-frame was settled
    /// by <see cref="BeginFrame"/>, so every pass draws the same bodies in the same places.
    /// </remarks>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_groups.Count == 0 && _stationaryWeaponVisuals.Count == 0 && _vehicleGuns is null)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        // StationaryGun's holder is a separate authored model. An AI gunner replaces it with the
        // mounted soldier's hand attachment, but the first-person player has no equivalent world
        // attachment, so the holder stays visible while the player owns the task.
        DrawStationaryGuns(camera, projection);
        DrawVehicleGuns(camera, projection);

        // Motion is a geometry-pass concern: only the G-buffer records it, and the previous pose is
        // built against the scene camera, which the shadow cascades' light cameras are not.

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier))
                {
                    continue;
                }

                // A dead soldier keeps being drawn -- a body stays on the ground in the original, and
                // vanishing was only ever the port having no death animation to play. Each soldier
                // carries its own skinned mesh, so a corpse is posed by its own death clip.
                if (!member.HasFrameState)
                {
                    ResolveFrameState(member, 1f, smooth: false);
                }

                Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

                // The pose blend applies to the body's animated mesh in every pass — the shadow pass
                // blends too, or the body would stand a step from its own shadow.
                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

                // The gun first, with its OWN previous transform: it is placed by the hand bone, and
                // handing it the body's previous pose would report it sliding by the distance between
                // the two.
                DrawWeapon(member, camera, projection);

                // Matte, and out of the ambient occlusion: see SurfaceMaterial.Body. The occlusion's
                // right repair is a thickness test in the search itself; until that exists, soldiers
                // are left out rather than shaded wrongly.
                _device.SetMaterialOverride(SurfaceMaterial.Body with
                {
                    NoHistory = member.NoHistoryThisFrame,
                });

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
                        diffuse = Textures.Get(texture);
                    }

                    _device.DrawMesh(
                        member.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        diffuse,
                        TextureHandle.None,
                        BlendMode.Opaque);
                }

                _device.SetMaterialOverride(SurfaceMaterial.Default);
                _device.ClearPoseInterpolation();
            }
        }
    }

    private void DrawStationaryGuns(in Camera3D camera, in Matrix4f projection)
    {
        foreach ((AiStationaryGun gun, HeldWeapon? visual) in _stationaryWeaponVisuals)
        {
            bool playerUsing = gun.IsOccupied && _sim.Interactions?.Items.Any(item =>
                ReferenceEquals(item.StationaryGun, gun) && item.IsPlayerUsingStationaryGun) == true;
            if (!ShouldDrawStaticStationaryGun(gun, playerUsing) || visual is null)
            {
                continue;
            }

            if (_reportedStationaryVisuals.Add(gun.TaskId))
            {
                _logger.Information(
                    LogCategory + ".Gunner",
                    $"static visual task={gun.TaskId} weapon={gun.WeaponId} "
                    + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) drawn");
            }

            // 0x474420's pose: on the stand's mount point, turned and elevated by the shown aim. The
            // gun's barrel is authored on local +Y and the stand's on local -Y; the π inside
            // GunOrientation is what swings one onto the other.
            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                // No multipart presenter is built for the level's weapons, so the gun is drawn the way a
                // soldier's is: as its one rigid mesh. Requiring the presenter here is what left every
                // stand empty.
                DrawRigidWeapon(visual, gun.GunPosition / WorldScene.UnitsPerMetre, gun.GunOrientation, camera);
            }
        }
    }

    private void DrawVehicleGuns(in Camera3D camera, in Matrix4f projection)
    {
        if (_vehicleGuns is not { } guns)
        {
            return;
        }

        foreach (VehicleGunVisual gun in guns())
        {
            if (!_vehicleWeaponVisuals.TryGetValue(gun.WeaponModel, out HeldWeapon? visual) || visual is null)
            {
                continue;
            }

            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                DrawRigidWeapon(visual, gun.PositionUnits / WorldScene.UnitsPerMetre, gun.Orientation, camera);
            }
        }
    }


    /// <summary>
    /// Loads the player's third-person body — the model the level's <c>HumanPlayer</c> declares, on
    /// skeleton 0's set, standing in clip 2 — so the death camera has a body to reveal.
    /// </summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="model">The player's model, from the <c>HumanPlayer</c> task's <c>Model</c> parameter.</param>
    /// <param name="set">Skeleton 0's animation set (<c>000.iff</c>), the same one the player walks on.</param>
    /// <param name="position">Where the level places the player, in engine units.</param>
    /// <param name="gamma">The heading the player starts facing.</param>
    /// <returns><see langword="false"/> when the model does not resolve or does not upload.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The body is built from a synthetic <see cref="HumanSoldierTask"/> because that is all
    /// <see cref="SoldierView"/> needs — skeleton, stand clip, position and heading — and the player's
    /// constructor (<c>0x410370</c>) builds exactly that on <c>Human+104</c> (see <see cref="HumanPlayerBody"/>).
    /// It is kept out of <c>_groups</c> so nothing else draws or ticks it; only the death path does.
    /// </remarks>
    public bool LoadPlayerBody(
        LoadedMission mission, string model, BoneAnimationSet set, Vector3d position, float gamma)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(set);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReleasePlayerBody();

        HumanSoldierTask task = new()
        {
            Model = model,
            BoneHierarchy = set.Skeleton.Id,
            StandAnimation = HumanPlayerBody.StandAnimation,
            Position = position,
            Gamma = gamma,
        };

        SoldierView view;
        try
        {
            view = new SoldierView(task, set);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"The player body could not be posed on skeleton {set.Skeleton.Id}: {ex.Message}");
            return false;
        }

        Group? group = BuildGroup(mission, model, view);
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
            _logger.Warning(LogCategory, $"The player collision mesh was not usable: {ex.Message}");
        }

        if (GameplayCVars.Ragdoll.Value && group.Body.Model is not null)
        {
            _ = _sim.RagdollRigFor(set, group.Body.Model);
        }

        MeshHandle own;
        try
        {
            own = _device.CreateAnimatedMesh(group.Geometry.Vertices, group.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"The player body '{model}' did not upload: {ex.Message}");
            return false;
        }

        _sim.PlayerBodyView = view;
        _sim.PlayerBodyModel = group.Body;
        _playerBodyGroup = group;
        _playerBodyMember = new Member(new SoldierAgent(view, default), own);
        _playerBodyGround = _sim.MakeGroundQuery();
        _playerBodyActive = false;
        _playerBodyHasTickState = false;
        _playerBodyCaptureTick = -1;
        _playerBodySkinTick = -1;
        _logger.Information(LogCategory, $"Loaded the player's third-person body '{model}' on skeleton {set.Skeleton.Id}.");
        return true;
    }

    /// <summary>
    /// Poses the player's body with its death fall and starts drawing it — the player has died.
    /// </summary>
    /// <param name="deathAnimation">The fall clip the death state chose, from the player's hit-zone row.</param>
    /// <param name="feet">Where the body falls, in engine units — the player's own position.</param>
    /// <param name="gamma">Which way the body faces as it goes down.</param>
    /// <param name="bone">The bone the killing round went through, or <c>-1</c> when none was named.</param>
    /// <param name="travel">The killing round's own line, from the muzzle towards where it ended.</param>
    /// <param name="category">What fired it, which is where the impulse's magnitude comes from.</param>
    /// <param name="velocity">What the player was doing at the moment of death, in units per tick.</param>
    /// <remarks>
    /// Does nothing when no body was loaded; the death still happens, just without a body to show. The
    /// last four arguments are the ragdoll's, and are ignored entirely when <c>g_ragdoll</c> is off —
    /// the retail clip above them runs either way.
    /// </remarks>
    public void KillPlayerBody(
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

        _playerBodyFacing = gamma;
        _sim.PlayerBodySuppressRootHeight = false;
        _sim.PlayerBodyVisualLift = 0.0;
        _sim.PlayerBodyView.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, gamma));
        _sim.PlayerBodyView.Kill(deathAnimation);
        _playerBodyActive = true;

        // Seeded before the tick state is captured, so the very first capture is already the corpse's
        // own placement rather than the clip's -- a death is a cut and there is nothing to blend from.
        _playerRagdoll = SeedRagdoll(
            _sim.PlayerBodyView,
            new Vector3d(feet.X, feet.Y, feet.Z + _sim.PlayerBodyView.RootSample.Z),
            _sim.PlayerBodyView.Orientation,
            bone,
            travel,
            category,
            velocity,
            _playerBodyGroup?.Body.Model,
            weapon: null);

        // A death is a cut, not a movement: the body appears where the player fell, and nothing on
        // screen is its history. Snap the interpolation pair and start the frame bookkeeping over.
        CapturePlayerBodyTickState(snap: true);
        if (_playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
            fresh.HasTickState = false;
        }

        // The fall clip has to resolve in the player's set, or Kill silently keeps the idle and the body
        // never falls; reported once so a wrong id is visible rather than a body that just stands there.
        if (_sim.PlayerBodyView.CurrentAnimation != deathAnimation)
        {
            _logger.Warning(
                LogCategory,
                $"The player's death clip {deathAnimation} is not in its set; the body keeps clip "
                    + $"{_sim.PlayerBodyView.CurrentAnimation} and will not fall.");
        }
    }

    /// <summary>Advances the player body's death clip by one tick and re-skins it, once it is active.</summary>
    /// <remarks>
    /// The same per-tick re-skin a corpse gets: the clip advances, holding its last frame when it runs out,
    /// and the body's own mesh is rebuilt from the new pose so the fall plays through.
    /// </remarks>
    public void TickPlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playerBodyActive
            || _sim.PlayerBodyView is null
            || _playerBodyMember is null
            || _playerBodyGroup?.Body.Model is not { } model)
        {
            return;
        }

        _sim.PlayerBodyView.Tick();

        // The knock-back. A dying body is thrown -- the fall clip's own root-motion track carries it back
        // and down, and the retail mover consumes exactly that (0x4109A0 samples the body clip's root delta
        // through sub_4D2FB0 and moves the human by it, until 0x40E620 sees the body grounded and stops it).
        // Advancing the clip WITHOUT moving the body left it crumpling on the spot instead of being thrown,
        // which is the missing "knocked to the ground" feel. This is the corpse move a soldier already gets
        // (AiSoldier.TickDeadBody): the horizontal root motion, rotated by the facing, plus a ground snap.
        Vector3f rootMotion = _sim.PlayerBodyView.RootMotion();
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(rootMotion.X, rootMotion.Y, rootMotion.Z), _playerBodyFacing);

        double px = _sim.PlayerBodyView.Position.X + world.X;
        double py = _sim.PlayerBodyView.Position.Y + world.Y;
        double pz = _sim.PlayerBodyView.Position.Z + world.Z;
        if (_playerBodyGround is { } ground
            && ground(px, py, pz + SoldierSimulation.ProbeAboveInUnits, out double groundZ))
        {
            pz = groundZ;
        }

        _sim.PlayerBodyView.PlaceAt(new Vector3d(px, py, pz), _sim.PlayerBodyView.Orientation);

        // The corpse, when there is one. It is stepped after the retail move above rather than instead
        // of it: that move is what the rest of the game believes, and only the drawing changes here.
        if (_playerRagdoll is { } playerCorpse)
        {
            playerCorpse.Step(
                _sim.RagdollWorldForCorpses(),
                GameplayCVars.RagdollSubsteps.Value,
                GameplayCVars.RagdollGravity.Value);
            if (!playerCorpse.IsValid)
            {
                _playerRagdoll = null;
                _logger.Warning(LogCategory, "The player's ragdoll left the level; falling back to the death clip.");
            }
        }

        ReskinPlayerBody(_sim.PlayerBodyView, model);
        CapturePlayerBodyTickState(snap: false);
    }

    /// <summary>Stands the player's body at a place and draws it, outside death and outside climbing.</summary>
    /// <param name="feetUnits">Where the body stands, in engine units — the level's own spawn.</param>
    /// <param name="facing">Which way it faces, as the spawn's gamma.</param>
    /// <remarks>
    /// <para>
    /// A third reason to draw the body, and deliberately a separate flag rather than a reuse of either
    /// existing one. The mission-entry shot dives onto the character standing at their spawn and seats
    /// the view inside their head, so it needs the body visible for a second — but it must not touch
    /// <c>_playerBodyActive</c>, which is the death latch and also drives the fall's per-tick re-skin and
    /// the knock-back, nor the climbing latch, which the ladder code clears on its own schedule.
    /// </para>
    /// <para>
    /// <b>It has to place and re-skin, not merely make visible.</b> <see cref="LoadPlayerBody"/> uploads
    /// the model's REST mesh and leaves the body unplaced — the death path places and poses it, the
    /// climbing path places and poses it, and until one of them runs there is no pose in the vertex
    /// buffer at all. Drawing it in that state put enormous black shards across the middle of the descent,
    /// which is exactly what an unskinned rig looks like when its vertices are still at the origin.
    /// </para>
    /// <para>
    /// Cleared by <see cref="SoldierSimulation.HidePlayerBody"/> and by <see cref="Release"/>, so a mission that ends
    /// mid-shot does not leave a body standing in the next one.
    /// </para>
    /// </remarks>
    public void StandPlayerBodyAt(Vector3d feetUnits, float facing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_sim.PlayerBodyShown;
        _playerBodyFacing = facing;
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
        if ((appearing || _playerBodySkinTick != _sim.TickCount) && _playerBodyGroup?.Body.Model is { } model)
        {
            view.Tick();
            ReskinPlayerBody(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _sim.PlayerBodyShown = true;

        // Appearing is a cut: nothing on screen is this body's history, so neither the interpolation pair
        // nor the temporal history may bridge from wherever it last stood.
        CapturePlayerBodyTickState(snap: appearing);
        if (appearing && _playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }




    /// <summary>Walks the third-person body with the player, in the clip the player's own legs are in.</summary>
    /// <param name="feetUnits">Where the player is standing, in engine units — <c>PlayerBody.Position</c>.</param>
    /// <param name="facing">Which way the body faces, in radians — the player's yaw.</param>
    /// <param name="animation">
    /// The clip the player's <see cref="HumanLocomotionAnimator"/> is playing, or
    /// <see cref="HumanLocomotionStates.NoAnimation"/> for the states that name none.
    /// </param>
    /// <param name="loops">Whether that state's table entry loops.</param>
    /// <param name="pose">
    /// The half of the body the legs' clip does not drive — the weapon's torso animation and where the
    /// player is looking. See <see cref="PlayerBodyPose"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Called on the simulation tick, not on the frame.</b> The body's clip advances one authoring
    /// frame per <see cref="SoldierView.Tick"/> and the player's animator advances one per tick, so
    /// stepping it here — and only here — is what keeps the two in phase without any clock being copied
    /// between them: the same state entered on the same tick starts the same clip at the same zero. Step
    /// it from the draw instead and a display slower than 30 Hz walks the legs at less than the speed the
    /// body is actually travelling, which is a character skating along the ground.
    /// </para>
    /// <para>
    /// The placement is the ordinary one a soldier gets — the task position lifted by the clip's own root
    /// height (<see cref="PlayerBodyPlacement"/>) — so a crouch sits where a crouching guard sits.
    /// </para>
    /// <para>
    /// <paramref name="facing"/> is a <b>gamma</b>, not the player's yaw: the two run opposite ways, and
    /// the caller negates. See the call site in <c>DesktopGame</c>.
    /// </para>
    /// </remarks>
    public void FollowPlayerBody(Vector3d feetUnits, float facing, int animation, bool loops, in PlayerBodyPose pose)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_sim.PlayerBodyFollowing;
        _playerBodyFacing = facing;
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
        if ((appearing || _playerBodySkinTick != _sim.TickCount) && _playerBodyGroup?.Body.Model is { } model)
        {
            view.Tick();
            ReskinPlayerBody(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _sim.PlayerBodyFollowing = true;
        CapturePlayerBodyTickState(snap: appearing);
        if (appearing && _playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }

    /// <summary>Where the third-person body's hand is this frame, for the weapon it is holding.</summary>
    /// <returns>
    /// The hand bone's world placement in engine units, or <see langword="null"/> when no body is being
    /// followed or the rig has no such bone.
    /// </returns>
    /// <remarks>
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/> — bone 22, the arm <c>0x4600C0</c> resolves for
    /// a body seen from outside, which is the same bone a guard's rifle rides. Interpolated between the
    /// same two ticks the body itself is drawn between, or the gun steps at 30 Hz in a hand that glides.
    /// </remarks>
    public BoneAttachment? PlayerBodyHandAttachment()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sim.PlayerBodyFollowing || _sim.PlayerBodyView is not { } view || _playerBodyMember is not { } member)
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
        if (_frameSmooth && member.HasHandState)
        {
            hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * _frameAlpha);
            handRotation = Interpolation.InterpolateRotation(
                member.PrevTickHandRotation, member.TickHandRotation, _frameAlpha);
        }

        (Vector3d where, Matrix3f orientation) = PlayerBodyPlacement();
        if (_frameSmooth && _playerBodyHasTickState)
        {
            where = Vector3d.Lerp(_playerBodyPrevTickWhere, _playerBodyTickWhere, _frameAlpha);
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

    /// <summary>Starts the third-person body in one of IGI1's ladder animation states.</summary>
    /// <param name="animation">The state's authored clip.</param>
    /// <param name="loops">Whether the clip loops.</param>
    /// <param name="backwards">Whether it starts at its end and runs backwards.</param>
    /// <param name="paused">Whether its playback rate starts at zero.</param>
    public void StartPlayerLadderAnimation(
        int animation,
        bool loops,
        bool backwards = false,
        bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        _sim.StartPlayerTraversalAnimation(
            view, animation, loops, backwards, paused, suppressRootHeight: true, visualLift: 0.0);

        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        ResetPlayerTraversalHistory();
    }

    /// <summary>Starts a non-ladder interaction clip with its authored root height intact.</summary>
    /// <remarks>
    /// Wire animations 172/173 carry a non-zero root-height track. IGI1 adds that sample when locating
    /// the body; suppressing it as if the wire were a ladder leaves both hands visibly off the cable.
    /// </remarks>
    public void StartPlayerInteractionAnimation(int animation, bool loops)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ResetPlayerTraversalHistory();
    }


    private void ResetPlayerTraversalHistory()
    {
        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        _playerBodyHasTickState = false;
        _playerBodySkinTick = -1;
        if (_playerBodyMember is { } member)
        {
            member.HasFrameState = false;
        }
    }

    /// <summary>Advances the current ladder clip and returns its movement and state signals.</summary>
    /// <param name="facing">Which way the body faces — into the ladder.</param>
    /// <param name="rootDelta">
    /// Receives the clip's root-motion delta for this tick, already rotated into the facing frame (world
    /// units) — the same transform a walking guard's motion gets. The caller applies it to the position.
    /// </param>
    /// <param name="rungBoundary">Receives whether animation 168 crossed authored event 8.</param>
    /// <param name="ended">Receives whether a non-looping top transition reached its endpoint.</param>
    /// <remarks>Ticks and re-skins the body but does not move it; placement is the caller's, via
    /// <see cref="PlacePlayerBodyClimbing"/>.</remarks>
    public void AdvancePlayerLadderAnimation(
        float facing,
        out Vector3d rootDelta,
        out bool rungBoundary,
        out bool ended)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        rootDelta = default;
        rungBoundary = false;
        ended = false;
        if (_sim.PlayerBodyView is not { } view || _playerBodyGroup?.Body.Model is not { } model)
        {
            return;
        }

        _sim.PlayerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.Tick();

        Vector3f rm = view.RootMotion();
        rootDelta = SoldierDeathFall.RotateAboutZ(new Vector3d(rm.X, rm.Y, rm.Z), facing);
        rungBoundary = view.CrossedLadderBoundary;
        ended = view.ClipEndedThisTick;
        ReskinPlayerBody(view, model);

        // The caller places the body with the delta straight after; the placement call captures the
        // tick state, so nothing is recorded here off a stale position.
    }

    /// <summary>Places the climbing body without advancing its clip — holds the pose at a rung boundary.</summary>
    /// <param name="taskPosition">Where IGI1 places the human task root, in engine units.</param>
    /// <param name="facing">Which way it faces — into the ladder.</param>
    public void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        _sim.PlayerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.PlaceAt(taskPosition, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // A newly paused rung must upload its held pose into both interpolation streams. ReskinPlayerBody's
        // pose latch permits that one duplicate upload, then suppresses further skinning until the pose moves.
        // AdvancePlayerLadderAnimation already uploads each genuinely moving tick.
        if (_playerBodySkinTick != _sim.TickCount && _playerBodyGroup?.Body.Model is { } model)
        {
            ReskinPlayerBody(view, model);
        }

        CapturePlayerBodyTickState(snap: false);
    }


    // Re-poses the player body's animated mesh in place. Shared by the death fall and the ladder climb.
    private void ReskinPlayerBody(SoldierView view, MefModel model)
    {
        if (_playerBodyMember is not { } member || _playerBodyGroup is null || !member.Mesh.IsValid)
        {
            return;
        }

        BonePose pose = _playerRagdoll is { } corpse ? corpse.Pose() : view.UnrotatedPose();
        member.DrawnPose = pose;
        if (!member.PoseUploads.ShouldUpload(pose))
        {
            _playerBodySkinTick = _sim.TickCount;
            return;
        }

        Vertex3D[]? skinned = member.SkinScratch;
        if (skinned is null)
        {
            skinned = new Vertex3D[_playerBodyGroup.Geometry.Vertices.Length];
            _playerBodyGroup.Geometry.Vertices.CopyTo(skinned, 0);
            member.SkinScratch = skinned;
        }
        try
        {
            MefSkinner.Skin(model, pose, (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            _device.UpdateAnimatedMesh(member.Mesh, skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        member.PoseFlippedSinceFrame = true;
        _playerBodySkinTick = _sim.TickCount;
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
    private (Vector3d Where, Matrix3f Orientation) PlayerBodyPlacement()
    {
        if (_playerRagdoll is { } corpse)
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
    private void CapturePlayerBodyTickState(bool snap)
    {
        if (_sim.PlayerBodyView is not { } view)
        {
            return;
        }

        Vector3d where = PlayerBodyPlacement().Where;

        if (snap || !_playerBodyHasTickState)
        {
            _playerBodyPrevTickWhere = where;
        }
        else if (_playerBodyCaptureTick != _sim.TickCount)
        {
            _playerBodyPrevTickWhere = _playerBodyTickWhere;
        }

        // A second capture in the same tick only refines the current state; the previous one stands.
        _playerBodyTickWhere = where;

        // The hand the third-person weapon rides, captured in the same pair and by the same rules a
        // soldier's is (ResolveTickState). Only the followed body needs it: the death and ladder paths
        // draw no weapon, and asking a posed rig for a bone it has costs one array read.
        if (_sim.PlayerBodyFollowing
            && _playerBodyMember is { } member
            && HumanAttachPoints.ThirdPersonWeapon < view.UnrotatedPose().BoneCount)
        {
            BonePose pose = view.UnrotatedPose();
            Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
            Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
            bool fresh = snap || !member.HasHandState;
            member.PrevTickHandPosition = fresh
                ? hand
                : _playerBodyCaptureTick != _sim.TickCount ? member.TickHandPosition : member.PrevTickHandPosition;
            member.PrevTickHandRotation = fresh
                ? rotation
                : _playerBodyCaptureTick != _sim.TickCount ? member.TickHandRotation : member.PrevTickHandRotation;
            member.TickHandPosition = hand;
            member.TickHandRotation = rotation;
            member.HasHandState = true;
        }

        _playerBodyCaptureTick = _sim.TickCount;
        _playerBodyHasTickState = true;
    }

    /// <summary>Settles where the player's own body is drawn this frame, and what it moved from.</summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ResolveFrameState"/> a soldier gets, for the one body that is not in a group. Its
    /// placement is a ladder position or a fall or a walk — <see cref="PlayerBodyPlacement"/> knows which
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
    private void ResolvePlayerBodyFrameState()
    {
        if (_sim.PlayerBodyView is null || _playerBodyMember is not { } member)
        {
            return;
        }

        (Vector3d where, Matrix3f orientation) = PlayerBodyPlacement();
        if (_frameSmooth && _playerBodyHasTickState)
        {
            where = Vector3d.Lerp(_playerBodyPrevTickWhere, _playerBodyTickWhere, _frameAlpha);
        }

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = _frameSmooth ? 1f - _frameAlpha : 0f;
        member.PoseFlippedSinceFrame = false;
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;
    }

    /// <summary>Draws the player's third-person body, when it is active — what the death camera looks at.</summary>
    /// <param name="camera">The eye, which by now is the death camera pulled back off the body.</param>
    /// <param name="projection">The projection to draw with.</param>
    /// <remarks>
    /// Drawn on its own, not in the soldier loop, and only when active — so it never appears in front of
    /// the first-person view during normal play. The body sits at its own position lifted by the death
    /// clip's root height, exactly as a fallen soldier is (see <see cref="Draw"/>).
    /// </remarks>
    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool visible = _playerBodyActive
            || _sim.PlayerBodyClimbing
            || _sim.PlayerBodyShown
            || (_sim.PlayerBodyFollowing && PlayerBodyFollowVisible);

        // Drawn for nobody, but THERE for a ray. In first person the body is not drawn -- the camera
        // would be inside its head -- and until a reflection could shade a hit from the hit's own
        // material, that also meant it could never appear in a window, because the only geometry a ray
        // can strike is geometry something submitted. It can be shaded now, so it is submitted now: the
        // player gets a reflection of themselves, and a shadow, without their own legs in shot.
        bool tracedOnly = !visible && _sim.PlayerBodyFollowing;

        if ((!visible && !tracedOnly)
            || _sim.PlayerBodyView is null
            || _playerBodyMember is not { } member
            || _playerBodyGroup is not { } group)
        {
            return;
        }

        if (tracedOnly)
        {
            SubmitPlayerBodyToTracedWorld(camera, member, group);
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
                diffuse = Textures.Get(texture);
            }

            _device.DrawMesh(
                member.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

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
    /// written by <see cref="ReskinPlayerBody"/> on the animation tick rather than at the draw.
    /// </remarks>
    private void SubmitPlayerBodyToTracedWorld(in Camera3D camera, Member member, Group group)
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
                diffuse = Textures.Get(texture);
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
        if (!_playerBodyTracedReported)
        {
            _playerBodyTracedReported = true;
            _logger.Information(
                LogCategory,
                $"Player body in the traced world: {subsets} subset(s), {textured} with a texture "
                    + $"(first '{first}'); instance {(group.Instance is null ? "absent" : "present")}, "
                    + $"pack {(group.Pack is null ? "absent" : "present")}.");
        }
    }

    private bool _playerBodyTracedReported;

    private void ReleasePlayerBody()
    {
        if (_playerBodyGroup is { } group && group.Mesh.IsValid)
        {
            _device.DestroyMesh(group.Mesh);
        }

        if (_playerBodyMember is { } member && member.Mesh.IsValid)
        {
            _device.DestroyMesh(member.Mesh);
        }

        _sim.PlayerBodyView = null;
        _sim.PlayerBodyModel = null;
        _playerBodyGroup = null;
        _playerBodyMember = null;
        _playerRagdoll = null;
        _playerBodyActive = false;
        _sim.PlayerBodyClimbing = false;
        _sim.PlayerBodyShown = false;
        _sim.PlayerBodyFollowing = false;
        _sim.PlayerBodyUpperBodySequence = -1;
        _sim.PlayerBodyVisualLift = 0.0;
        _sim.PlayerBodyRootHeightAtStart = 0.0;
        _playerBodyHasTickState = false;
        _playerBodyCaptureTick = -1;
        _playerBodySkinTick = -1;
    }

    /// <summary>Draws the one-tick muzzle flames of every soldier that fired, additively.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform.</param>
    /// <remarks>
    /// Once per frame, in the <b>forward</b> pass — never from <see cref="Draw"/>, which serves the
    /// geometry and shadow passes where an additive glow would be shaded like scenery. The flame is
    /// drawn at the same resolved weapon transform the body pass used, so the two halves of the gun
    /// agree to the pixel.
    /// </remarks>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!member.Agent.FiredThisTick
                    || !member.HasWeaponFrameState
                    || member.Weapon is not { } weapon
                    || weapon.Presentation is not { } presentation)
                {
                    continue;
                }

                presentation.Draw(
                    new BoneAttachment(
                        member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                        member.DrawnWeaponRotation),
                    camera,
                    projection,
                    firedThisTick: true,
                    member.Agent.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }

        // A gun the player is firing from its stand flames at the same pose DrawStationaryGuns drew.
        foreach ((AiStationaryGun gun, HeldWeapon? visual) in _stationaryWeaponVisuals)
        {
            if (!gun.FiredThisTick || visual?.Presentation is not { } gunPresentation)
            {
                continue;
            }

            gunPresentation.Draw(
                new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                camera,
                projection,
                firedThisTick: true,
                gun.ShotSequence,
                WeaponDrawParts.FlameOnly);
        }

        // A vehicle's gun flames on the ticks it puts a round out, on the weapon object DrawVehicleGuns drew. Only a
        // weapon model with a gun-flame child has one: a T80's M2HB does, its cannon does not.
        if (_vehicleGuns is { } vehicleGuns)
        {
            foreach (VehicleGunVisual gun in vehicleGuns())
            {
                if (!gun.FiredThisTick
                    || !_vehicleWeaponVisuals.TryGetValue(gun.WeaponModel, out HeldWeapon? visual)
                    || visual?.Presentation is not { } vehiclePresentation)
                {
                    continue;
                }

                vehiclePresentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: true,
                    gun.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }
    }

    /// <summary>Draws the weapon a soldier is holding, at its hand bone's own world transform.</summary>
    /// <param name="member">The soldier.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The current world projection.</param>
    /// <remarks>
    /// <para>
    /// The attachment handlers (<c>0x4600C0</c>, <c>player.md</c> §13) do exactly two things: they add the
    /// object's own position to the bone's, and they <b>copy the bone's rotation whole</b>. There is no
    /// per-weapon offset anywhere in the engine, so the model's own origin is where the hand grips it.
    /// </para>
    /// <para>
    /// The transform itself — hand bone through the body's heading, interpolated between ticks — was
    /// resolved by <see cref="ResolveFrameState"/>. The composed rotation is <c>heading · bone</c>,
    /// applied as <c>M·v</c>, which is the convention
    /// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> and the bone palette both use —
    /// <b>not</b> <see cref="Matrix3f.Transform(Vector3f)"/>'s row-vector one, which is its inverse.
    /// </para>
    /// <para>
    /// A gun is rigid, so unlike the body its previous <em>transform</em> reprojects every vertex
    /// exactly; last frame's is reported as its previous pose on the geometry pass.
    /// </para>
    /// </remarks>
    private void DrawWeapon(Member member, in Camera3D camera, in Matrix4f projection)
    {
        if (member.Weapon is not { } weapon || !member.HasWeaponFrameState)
        {
            return;
        }

        if (weapon.Presentation is { } presentation)
        {
            // Body only: the muzzle flame is LIGHT, and drawing it here put it into the deferred
            // G-buffer to be shaded like scenery -- a one-frame dark square at the muzzle. It is
            // drawn by DrawMuzzleFlashes instead, in the forward pass, like the player's own.
            presentation.Draw(
                new BoneAttachment(
                    member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                    member.DrawnWeaponRotation),
                camera,
                projection,
                firedThisTick: false,
                member.Agent.ShotSequence,
                WeaponDrawParts.BodyOnly);
            return;
        }

        DrawRigidWeapon(weapon, member.DrawnWeaponOrigin, member.DrawnWeaponRotation, camera);
    }

    // A weapon model drawn whole as one rigid mesh, for when no multipart presenter was built for it.
    // The origin is in metres, as CreateModelView takes it.
    private void DrawRigidWeapon(HeldWeapon weapon, Vector3d originMetres, Matrix3f rotation, in Camera3D camera)
    {
        Matrix4f modelView = camera.CreateModelView(originMetres, rotation);

        foreach (MeshSubset subset in weapon.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && weapon.Instance is { } instance
                && weapon.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = Textures.Get(texture);
            }

            _device.DrawMesh(
                weapon.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

    }

    /// <summary>Offers a round to every soldier and scores it against the nearest one it meets.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="category">The firing weapon's category, which decides how a body it kills falls.</param>
    /// <param name="hit">The nearest soldier struck.</param>
    /// <returns><see langword="false"/> when the round meets nobody.</returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, because the health belongs to the soldier and not to the
    /// round. A soldier already dead is skipped rather than shot again — the original stops a corpse
    /// taking damage through a flag on the human, and this is the same effect with the state that
    /// exists.
    /// </remarks>
    public bool TryShoot(
        Vector3d origin,
        Vector3d end,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit)
    {
        hit = default;

        HumanHealth? struck = null;
        SoldierView? struckView = null;
        Vector3d ray = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double reach = Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y) + (ray.Z * ray.Z));
        Vector3d direction = reach > 0.0
            ? new Vector3d(ray.X / reach, ray.Y / reach, ray.Z / reach)
            : default;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.Agent.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier))
                {
                    continue;
                }
                HumanHealth health = _sim.HealthOf(soldier);

                if (health.IsDead)
                {
                    continue;
                }

                // The player fires against the body the crosshair was drawn over. With r_smooth that is
                // the last presentation transform, not the newer simulation state the next tick has just
                // published. AI fire stays on its own fixed-tick path below: this is only the player-shot
                // delegate supplied by Shooter().
                bool presented = _frameSmooth && member.HasFrameState;
                Vector3d feet;
                Matrix3f m;
                if (presented)
                {
                    Vector3d root = member.DrawnWhere * WorldScene.UnitsPerMetre;
                    feet = new Vector3d(root.X, root.Y, root.Z - soldier.RootSample.Z);
                    m = member.DrawnOrientation;
                }
                else
                {
                    // r_smooth off is the vanilla simulation transform verbatim. The feet the round is
                    // cast at must be the SAME feet the body is drawn on — grounded onto the floor — and
                    // the bones sit a root height above them, exactly as the draw lifts the pelvis.
                    double snappedZ = _sim.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                    feet = new Vector3d(soldier.Position.X, soldier.Position.Y, snappedZ);
                    m = soldier.Orientation;
                }
                Vector3f facing = new(m.M12, m.M22, m.M32);

                // The faithful path: trace the round at the soldier's skinned collision mesh and score it by
                // the bone the round actually went through (BoneDynCubeObj_TraceLinesHandler). Skinning every
                // collision vertex for every guard is far costlier than the ray itself, so reject bodies the
                // segment cannot reach with the same generous body sphere TryPick uses. Starting inside the
                // sphere still reaches the mesh, which preserves point-blank hits even when the round ends
                // before it leaves the broad-phase sphere. Only if the model ships no collision mesh does it
                // fall back to the cylinder-and-nearest-bone stand-in.
                bool met;
                HumanHit candidate;
                if (group.Body.CollisionMesh is { } collision)
                {
                    Vector3d centre = new(feet.X, feet.Y, feet.Z + SoldierSimulation.PickChestUnits);
                    double offsetX = origin.X - centre.X;
                    double offsetY = origin.Y - centre.Y;
                    double offsetZ = origin.Z - centre.Z;
                    bool startsInside = (offsetX * offsetX) + (offsetY * offsetY) + (offsetZ * offsetZ)
                        <= SoldierSimulation.PickCullRadiusUnits * SoldierSimulation.PickCullRadiusUnits;
                    if (reach <= 0.0 || (!startsInside
                        && !SoldierSimulation.RaySphere(origin, direction, centre, SoldierSimulation.PickCullRadiusUnits, reach, out _)))
                    {
                        continue;
                    }

                    if (presented
                        && member.SkinnedPose is { } currentPose
                        && member.PreviousSkinnedPose is { } previousPose)
                    {
                        // The renderer already owns both poses used by the motion-blended draw. Reuse
                        // them directly; do not re-evaluate the animation for a candidate that has
                        // already survived the broad phase.
                        met = HumanHitTest.TryCastMeshBlended(
                            origin,
                            end,
                            member.DrawnWhere * WorldScene.UnitsPerMetre,
                            facing,
                            collision,
                            currentPose,
                            previousPose,
                            member.DrawnOrientation,
                            member.PoseBlend,
                            out candidate);
                    }
                    else
                    {
                        // Pose evaluation (especially a presented-orientation re-evaluation) skins the
                        // complete rig. Keep it behind the ray/sphere rejection so off-ray soldiers do
                        // not pay that cost on every scoped shot.
                        BonePose pose = presented
                            ? SoldierSimulation.PoseAtPresentationOrientation(soldier, m)
                            : soldier.Pose();
                        met = HumanHitTest.TryCastMesh(
                            origin, end, feet, facing, collision, pose, soldier.RootSample.Z, out candidate);
                    }
                }
                else
                {
                    // The cylinder fallback still needs a pose for bone-zone refinement, but only after
                    // there is no collision mesh broad phase to skip.
                    BonePose pose = presented
                        ? SoldierSimulation.PoseAtPresentationOrientation(soldier, m)
                        : soldier.Pose();
                    met = HumanHitTest.TryCast(
                        origin, end, feet, facing, pose, out candidate, soldier.RootSample.Z);
                }

                if (!met)
                {
                    continue;
                }

                if (struck is null || candidate.Distance < hit.Distance)
                {
                    hit = candidate;
                    struck = health;
                    struckView = soldier;
                }
            }
        }

        // Human_EvaluateGunShotHandler evaluates each live projectile segment independently of ordinary
        // shot audibility. Do this after choosing the nearest collision so the struck guard gets the
        // TAKINGDAMAGE path while nearby guards get GUNSHOTMISS.
        _sim.NotifyGunshotMiss(origin, end, struckView, origin);

        if (struck is null)
        {
            return false;
        }

        // 0x45F94A, before anything else the damage handler does.
        if (BloodSpray is { } bleed)
        {
            Vector3d line = end - origin;
            double length = line.Length;
            bleed(
                (object?)struckView ?? struck,
                hit.Position,
                length > 1e-6
                    ? new Vector3f(
                        (float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                    : default);
        }

        // The soldier's brain carries two flags the damage handler 0x45D3C0 reads: invulnerability skips
        // the whole damage block (the round still MEETS the body, it just does nothing), and instant-death
        // being disabled makes a head/neck hit take the plain damage factor rather than the ×100 lethal
        // multiplier. Both default off, so an ordinary guard is unaffected.
        AiSoldier? brain = struckView is not null && _sim.Brains.TryGetValue(struckView, out AiSoldier? b) ? b : null;

        // Friendly fire: the player's round cannot hurt a soldier on the player's own team. Retail disables
        // same-team damage (the round still meets the body and the hit registers, but it does nothing), so
        // a friendly stands where the round landed without a scratch. Same mechanism as invulnerability:
        // the whole damage block is skipped, and the death branch never runs.
        bool friendlyFire = struckView is not null && struckView.Soldier.Team == _sim.PlayerTeam;
        bool invulnerable = friendlyFire || (brain?.IsInvulnerable ?? false);
        if (!invulnerable)
        {
            float dealt = struck.Apply(hit.Zone, damageFactor, brain?.IsInstantDeathDisabled ?? false);
            brain?.ReceiveDamage(origin);

            if (struck.IsDead && struckView is not null)
            {
                _sim.Die(struckView, hit, origin, end, category, SoldierDeath.ModeFor(category));
            }
            else if (struckView is not null)
            {
                _sim.ReactToHit(struckView, brain, hit.Zone, dealt, explosion: false);

                // 0x45D3C0 adds the blade's own sound to the grunt when the weapon's category is the knife.
                if (category == WeaponCategory.Knife && struckView.Soldier.Class.SoundsKnifeStab)
                {
                    _sim.ScriptSound?.Invoke(KnifeStabSound, struckView.Position, true);
                }
            }
        }

        // Deliberately the same shape as the probe's [dmg] line from the retail game (probes.cpp
        // OnDamageHandler), so a run of the port and a capture of the original diff directly. The port
        // resolves the zone from the pose (HumanHitTest.NearestBone) where the engine reads a part index
        // the bullet physics recorded, so a zone that disagrees for the same shot is the one to chase.
        _logger.Information(LogCategory + ".Damage",
            $"port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} dmgFactor={damageFactor:0.000} "
            + $"invuln={(invulnerable ? 1 : 0)} instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
            + $"friendly={(friendlyFire ? 1 : 0)} accum={struck.Damage:0.000} max={struck.MaximumHealth:0.000} dead={(struck.IsDead ? 1 : 0)}");

        return true;
    }




    /// <summary>Gets a delegate that offers rounds to these soldiers.</summary>
    /// <returns>The delegate the player takes.</returns>
    public HumanShot Shooter() => TryShoot;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sim.Retire();
        Release();

        _textureCache?.Clear();
    }

    private Group? BuildGroup(LoadedMission mission, string modelName, SoldierView posed)
    {
        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"'{posed.Soldier.Name}' names model '{modelName}', which no pack holds.");
            return null;
        }

        if (!MefSkinner.IsSkinned(model))
        {
            _logger.Warning(LogCategory, $"'{modelName}' is {model.Format}, not a skinned model.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f);
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"'{modelName}' has nothing to draw.");
            return null;
        }

        Vertex3D[] skinned = [.. geometry.Vertices];
        int written;
        try
        {
            // The unrotated pose: the facing is applied per soldier in the model matrix instead.
            written = MefSkinner.Skin(model, posed.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"'{modelName}' could not be posed: {ex.Message}");
            return null;
        }

        // The lowest point of the posed skin, in metres below the skeleton root. Z rotation cannot
        // change it, so it is a property of the (model, skeleton, animation) triple, and it is what
        // SoldierSimulation.Stand() rests on the ground. Only the base vertices count - the array's tail holds the
        // extra-influence records, which no draw record indexes.
        float lowest = 0f;
        for (int i = 0; i < written; i++)
        {
            lowest = Math.Min(lowest, skinned[i].Position.Z);
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(skinned, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        return new Group(new SoldierModelGroup(new SoldierBodyModel(model, lowest)), geometry, mesh, instance, pack);
    }

    private void Release()
    {
        foreach (Group group in _groups)
        {
            if (group.Mesh.IsValid)
            {
                _device.DestroyMesh(group.Mesh);
            }

            // Each soldier owns a mesh of its own now, and every one of them has to go back.
            foreach (Member member in group.Soldiers)
            {
                if (member.Mesh.IsValid)
                {
                    _device.DestroyMesh(member.Mesh);
                }
            }
        }

        foreach (HeldWeapon weapon in _weapons)
        {
            weapon.Presentation?.Dispose();
            if (weapon.Mesh.IsValid)
            {
                _device.DestroyMesh(weapon.Mesh);
            }
        }

        _weapons.Clear();
        _heldWeaponsByModel.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
        _groups.Clear();
        _sim.Roster.Clear();
        _sim.Health.Clear();
        _sim.DetectorMarkerScaleMetres.Clear();
        _sim.BinocularDetected.Clear();
        _sim.BinocularOverlay.Clear();
        _sim.BinocularVisibility.Clear();
        _sim.BinocularVisibilityTick = int.MinValue;
        _sim.Cast = null;
        _sim.Sight = null;
        _sim.RagdollRigs.Clear();
        _sim.RagdollWorld = null;
        ReleasePlayerBody();
        Count = 0;
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return (null, null);
    }

    /// <summary>Gets whether the holder model remains the visible gun for this ownership state.</summary>
    internal static bool ShouldDrawStaticStationaryGun(AiStationaryGun gun, bool playerUsing)
    {
        ArgumentNullException.ThrowIfNull(gun);
        return !gun.IsOccupied || playerUsing;
    }

    /// <summary>Advances every soldier's animation by one tick and re-skins the groups.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing advanced a soldier's rig before this existed</b>, so every guard in the level stood frozen on
    /// the first frame of its stand animation — which reads as a default A-pose. Soldiers are grouped by
    /// (model, skeleton, animation) and share one skinned mesh, so ticking the group's first soldier and
    /// re-skinning from its pose animates the whole group: they run the same clip from the same start, so they
    /// share a pose by construction.
    /// </para>
    /// <para>
    /// The device's meshes are immutable, so this destroys and re-uploads. There are only a handful of groups
    /// in a level, so it is a few uploads a tick — worth replacing with a dynamic vertex buffer, but not worth
    /// leaving the guards frozen for.
    /// </para>
    /// </remarks>
    /// <param name="enemy">The enemy's eye position, which the view cones are tested against.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a target. A dead player is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the guards lose it and stop firing at the
    /// corpse — passing <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, what combat aims at (<c>sub_451380</c>). Defaults to
    /// <paramref name="enemy"/> when the caller has no body to speak of.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    public void Tick(
        Vector3d enemy = default,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Standalone (a renderer outside a level walk: the menu backdrop, tests): the same per-task updates, run
        // in one pass. Inside a mission the level walk runs each where the tree places it (AttachTo).
        _sim.TickStandalone(new SoldierEnemy(enemy, enemyAlive, enemyFeet, enemyCrouching));
        EndTick();
    }

    /// <summary>Closes a tick: the soldiers' collision, corpses, dropped weapons and skinning.</summary>
    public void EndTick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sim.FinishTick();

        if (GameplayCVars.Ragdoll.Value)
        {
            RagdollWorld corpses = _sim.RagdollWorldForCorpses();
            int substeps = GameplayCVars.RagdollSubsteps.Value;
            double fall = GameplayCVars.RagdollGravity.Value;
            foreach (Group group in _groups)
            {
                foreach (Member member in group.Soldiers)
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
                        _logger.Warning(LogCategory, "A ragdoll left its own level; falling back to the death clip.");
                    }
                }
            }
        }

        // A GunPickup simulates independently after a dead human drops it, including when the optional
        // visual corpse ragdoll is off. Sharing these probes keeps its walls and floor identical to the
        // ones that stop the body.
        _sim.TickAfterCorpses();

        foreach (Group group in _groups)
        {
            if (group.Body.Model is null)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                BonePose drawn = PoseToDraw(member);
                member.DrawnPose = drawn;

                // A stopped one-shot (death, interaction or paused ladder pose) needs one duplicate
                // upload to settle both interpolation streams, then no more skinning or driver traffic.
                if (!member.PoseUploads.ShouldUpload(drawn))
                {
                    continue;
                }

                Vertex3D[]? skinned = member.SkinScratch;
                if (skinned is null)
                {
                    skinned = new Vertex3D[group.Geometry.Vertices.Length];
                    group.Geometry.Vertices.CopyTo(skinned, 0);
                    member.SkinScratch = skinned;
                }
                try
                {
                    MefSkinner.Skin(
                        group.Body.Model,
                        drawn,
                        (float)(1.0 / WorldScene.UnitsPerMetre),
                        skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!member.Mesh.IsValid)
                {
                    continue;
                }

                try
                {
                    _device.UpdateAnimatedMesh(member.Mesh, skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                // UpdateAnimatedMesh moves the old current stream into the previous stream before it
                // writes this pose. Keep the matching collision-pose pair so a player ray can trace the
                // very same r_smooth blend the GPU presents, rather than a newer animation frame.
                if (member.SkinnedPose is null)
                {
                    // A malformed model began with raw geometry. The first successful upload gives us a
                    // current pose but not a trustworthy previous one; wait for the next upload before
                    // claiming a blend pair that would disagree with the device.
                    member.SkinnedPose = drawn;
                    member.PreviousSkinnedPose = null;
                }
                else
                {
                    member.PreviousSkinnedPose = member.SkinnedPose;
                    member.SkinnedPose = drawn;
                }
                member.PoseFlippedSinceFrame = true;
            }
        }

        // With the poses settled, record where each body stands and faces THIS tick. The presentation
        // draws between the last two of these records, so they are the whole interface between the
        // 30 Hz simulation and however fast the display runs.
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                CaptureTickState(member);
            }
        }
    }

    private sealed class Group(SoldierModelGroup roster, MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack)
    {
        /// <summary>The soldiers this group draws, as the simulation keeps them, and what it reads of their model.</summary>
        public SoldierModelGroup Roster { get; } = roster;

        /// <summary>What the simulation reads of the group's model: the model, its collision mesh and its lowest vertex.</summary>
        public SoldierBodyModel Body => Roster.Body;

        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; set; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        private readonly List<Member> _soldiers = [];

        /// <summary>The soldiers the group draws, in the order the roster holds their agents; joined through <see cref="Add"/>.</summary>
        public List<Member> Soldiers => _soldiers;

        /// <summary>Adds a soldier whose agent the simulation has just added to the roster, to be drawn beside it.</summary>
        /// <param name="member">The soldier.</param>
        public void AddDrawn(Member member) => _soldiers.Add(member);

        /// <summary>Adds a soldier to the group, and its agent to the roster in the same place.</summary>
        /// <param name="member">The soldier.</param>
        public void Add(Member member)
        {
            _soldiers.Add(member);
            Roster.Add(member.Agent);
        }
    }

    /// <summary>
    /// One soldier drawn from the group's geometry, with <b>its own skinned mesh</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group used to share a single skinned mesh, posed from its first member. That was only ever
    /// defensible while every soldier in a group ran the same clip from the same start; once they were given
    /// real patrol paths they diverged, and the whole group rendered whichever pose the representative
    /// happened to be in. With most guards standing and a few walking, that read as <b>no soldier
    /// animating at all</b>.
    /// </para>
    /// <para>
    /// Hooking the original's skinner (<c>0x49F700</c>) settles what it does: it is called through a
    /// stack-allocated descriptor about once per soldier per frame, so the original skins <b>every soldier
    /// separately, every frame</b>. This does the same. It costs one mesh upload per soldier per tick
    /// instead of one per group, which is the price of each soldier having its own pose.
    /// </para>
    /// </remarks>
    private sealed class Member(SoldierAgent agent, MeshHandle mesh)
    {
        /// <summary>The soldier this draws: who it is, where it spawned, what it is armed with.</summary>
        public SoldierAgent Agent { get; } = agent;

        public MeshHandle Mesh { get; set; } = mesh;

        /// <summary>What this soldier holds, or <see langword="null"/> when the level armed it with nothing.</summary>
        public HeldWeapon? Weapon { get; set; }

        /// <summary>The carried weapon restored after a temporary stationary-gun mount ends.</summary>
        public HeldWeapon? CarriedWeapon { get; set; }

        /// <summary>The stationary gun whose model currently replaces the carried weapon.</summary>
        public AiStationaryGun? RenderedMountedGun { get; set; }

        /// <summary>The persistent buffer this soldier's pose is skinned into, allocated once.</summary>
        public Vertex3D[]? SkinScratch { get; set; }

        /// <summary>
        /// The pose this soldier was skinned from this tick — its rig's, or its corpse's.
        /// </summary>
        /// <remarks>
        /// Kept because <b>anything attached to the body has to be placed from the same pose the body was
        /// drawn from</b>, and for a corpse that is not the pose its rig is playing. Reading the rig
        /// directly is what left a dead guard's rifle hanging in the air where the death animation would
        /// have put his hand.
        /// </remarks>
        public BonePose? DrawnPose { get; set; }

        /// <summary>The pose in the animated mesh's current GPU vertex stream.</summary>
        /// <remarks>
        /// This is separate from <see cref="DrawnPose"/> because a stopped pose may not upload again;
        /// player collision needs the exact pair that is still resident on the device.
        /// </remarks>
        public BonePose? SkinnedPose { get; set; }

        /// <summary>The pose in the animated mesh's previous GPU vertex stream.</summary>
        public BonePose? PreviousSkinnedPose { get; set; }

        /// <summary>Tracks whether this pose still needs an animated-buffer upload.</summary>
        public PoseUploadLatch PoseUploads { get; } = new();

        /// <summary>
        /// The simulated corpse this soldier is drawn as once it has been killed, or
        /// <see langword="null"/> while it is alive or when <c>g_ragdoll</c> is off.
        /// </summary>
        /// <remarks>
        /// Presentation only. The soldier's brain still runs its retail death — same tick, same
        /// authoritative position, same consequences for the guards around it — and this replaces
        /// nothing but the pose that reaches the skinner.
        /// </remarks>
        public RagdollBody? Ragdoll { get; set; }

        /// <summary>Whether the animated mesh took a new pose since the last frame's bookkeeping.</summary>
        /// <remarks>
        /// When it did, the previous-pose stream now holds what was on screen last frame, and the
        /// blend that described last frame is expressed against the OLD stream pair — see
        /// <see cref="ResolveFrameState"/> for how each case is reported.
        /// </remarks>
        public bool PoseFlippedSinceFrame { get; set; }

        // ---- Captured on the simulation tick: the two states the presentation draws between. ----

        /// <summary>Whether the tick states below have been captured at least once.</summary>
        public bool HasTickState { get; set; }

        /// <summary>Where the body is drawn for the current tick, in metres.</summary>
        public Vector3d TickWhere { get; set; }

        /// <summary>Where it was drawn the tick before.</summary>
        public Vector3d PrevTickWhere { get; set; }

        /// <summary>This tick's world 3×3, verbatim — what an unblended frame draws, to the bit.</summary>
        public Matrix3f TickOrientation { get; set; }

        /// <summary>This tick's heading in radians, for interpolating.</summary>
        public float TickFacing { get; set; }

        /// <summary>The tick before's heading.</summary>
        public float PrevTickFacing { get; set; }

        /// <summary>Whether the hand-bone states below are captured (only armed soldiers need them).</summary>
        public bool HasHandState { get; set; }

        /// <summary>The hand bone's position in body space this tick, in world units.</summary>
        public Vector3f TickHandPosition { get; set; }

        /// <summary>The tick before's.</summary>
        public Vector3f PrevTickHandPosition { get; set; }

        /// <summary>The hand bone's rotation this tick.</summary>
        public Matrix3f TickHandRotation { get; set; } = Matrix3f.Identity;

        /// <summary>The tick before's.</summary>
        public Matrix3f PrevTickHandRotation { get; set; } = Matrix3f.Identity;

        // ---- Resolved once per rendered frame by ResolveFrameState; every pass draws from these. ----

        /// <summary>Whether a frame has been resolved at all.</summary>
        public bool HasFrameState { get; set; }

        /// <summary>Where the body is drawn this frame — between the two tick states when smoothing.</summary>
        public Vector3d DrawnWhere { get; set; }

        /// <summary>Its world 3×3 this frame.</summary>
        public Matrix3f DrawnOrientation { get; set; } = Matrix3f.Identity;

        /// <summary>Where it was drawn LAST frame — the rigid half of its motion vector.</summary>
        public (Vector3d Where, Matrix3f Orientation)? PreviousDrawn { get; set; }

        /// <summary>This frame's share of the previous-pose vertex stream in the drawn position.</summary>
        public float PoseBlend { get; set; }

        /// <summary>The share that reconstructs LAST frame's position from the same streams.</summary>
        public float PreviousPoseBlend { get; set; }

        /// <summary>Whether this frame is the body's first — nothing on screen is its history.</summary>
        public bool NoHistoryThisFrame { get; set; }

        /// <summary>Whether the weapon transform below is resolved.</summary>
        public bool HasWeaponFrameState { get; set; }

        /// <summary>The held weapon's world origin this frame, in metres.</summary>
        public Vector3d DrawnWeaponOrigin { get; set; }

        /// <summary>Its world rotation this frame — heading times hand bone.</summary>
        public Matrix3f DrawnWeaponRotation { get; set; } = Matrix3f.Identity;

        /// <summary>Last frame's weapon transform — a rigid model's exact previous pose.</summary>
        public (Vector3d Origin, Matrix3f Rotation)? PreviousWeapon { get; set; }
    }

    /// <summary>
    /// One weapon model, uploaded once and drawn by every soldier carrying that weapon.
    /// </summary>
    /// <remarks>
    /// Unlike a body, a gun is a rigid model: it is not skinned and its pose never changes, so a single
    /// mesh serves every soldier and only the transform differs. That transform is the hand bone's, which
    /// is why the body mesh has to be posed but this one does not.
    /// </remarks>
    private sealed class HeldWeapon(
        MeshGeometry geometry,
        MeshHandle mesh,
        MtpInstance? instance,
        LoadedPack? pack,
        IHeldWeaponPresenter? presentation)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        /// <summary>The multipart retail object, including its transient TASKTYPE_GUNFLAME child.</summary>
        public IHeldWeaponPresenter? Presentation { get; } = presentation;
    }

    // A model texture just uploaded gets the detail map derived from it, keyed by its name and its override's stamp.
    private void AttachDetail(string name, TextureHandle handle, TextureImage image) =>
        DetailMaps?.Attach(
            MaterialDetailPack.KeyFor(name, _resources.OverrideStamp(LoadedPack.TextureResourceName(name))),
            handle,
            image.GetLevel(0),
            image.Width,
            image.Height);

    // The textures the models name, loaded on first use.
    private TexTextureCache Textures => _textureCache ??= new(_device, _resources, _logger, LogCategory) { Loaded = AttachDetail };
}
