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
public sealed class SoldierRenderer : ISoldierPresenter, IDisposable
{
    internal const string LogCategory = "Soldier";
    // The one staged actor whose placement is traced each second, named by soldier id in
    // OPENIGI_TRACE_CUTSCENE_ACTOR. Read once: the check runs for every soldier on every tick.
    private static readonly string? TracedCutsceneActor = Environment.GetEnvironmentVariable("OPENIGI_TRACE_CUTSCENE_ACTOR");

    private readonly IGraphicsDevice _device;

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;
    private readonly List<Group> _groups = [];

    private readonly Dictionary<string, HeldWeapon> _heldWeaponsByModel = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<AiStationaryGun, HeldWeapon?> _stationaryWeaponVisuals = [];

    // The weapon models the vehicles' guns carry, by model, and where the runtime poses each gun (0x470050).
    private readonly Dictionary<string, HeldWeapon?> _vehicleWeaponVisuals = new(StringComparer.OrdinalIgnoreCase);
    private Func<IEnumerable<VehicleGunVisual>>? _vehicleGuns;
    private readonly HashSet<int> _reportedStationaryVisuals = [];

    // The sound 0x45D3C0 adds to a soldier's grunt when a knife made the hit.

    private bool _disposed;

    // The soldiers' simulation: their brains, their bodies' state, their weapons and what they hear and see.
    private readonly SoldierSimulation _sim;

    // What is drawn of the player's own body.
    private readonly PlayerBodyRenderer _playerBody;

    // What builds the groups', soldiers' and weapons' meshes.
    private readonly SoldierMeshBuilder _meshes;

    /// <inheritdoc/>
    public SoldierSimulation Simulation => _sim;

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>Owned by the shell: one pack serves every renderer that loads a texture.</remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <inheritdoc/>
    public bool PlayerBodyFollowVisible
    {
        get => _playerBody.FollowVisible;
        set => _playerBody.FollowVisible = value;
    }

    /// <inheritdoc/>
    public Action<object, Vector3d, Vector3f>? BloodSpray
    {
        get => _sim.BloodSpray;
        set => _sim.BloodSpray = value;
    }

    /// <inheritdoc/>
    public bool IsPlayerBodyActive => _playerBody.IsActive;

    /// <inheritdoc/>
    public Vector3d? PlayerRagdollCentre => _playerBody.RagdollCentre;
    // The frame alpha BeginFrame resolved, kept for the player's body, which draws once per frame.
    private float _frameAlpha = 1f;
    private bool _frameSmooth;

    /// <summary>Gets how far this frame sits between the last two ticks, as <see cref="BeginFrame"/> resolved it.</summary>
    internal float FrameAlpha => _frameAlpha;

    /// <summary>Gets whether this frame is drawn between ticks rather than at the last one.</summary>
    internal bool FrameSmooth => _frameSmooth;

    /// <summary>Gets what builds the soldiers' meshes.</summary>
    internal SoldierMeshBuilder Meshes => _meshes;

    /// <summary>Gets whether the renderer has been disposed.</summary>
    internal bool IsDisposed => _disposed;

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
        _logger = logger ?? NullLogger.Instance;
        _sim = new SoldierSimulation(_logger);
        _meshes = new SoldierMeshBuilder(_device, _logger, magic, heldWeapons);
        _playerBody = new PlayerBodyRenderer(this, _device, _sim, _logger);
        _sim.SpawnedBody = (generator, roster, fresh, dead) =>
        {
            Group group = _groups.First(candidate => ReferenceEquals(candidate.Roster, roster));
            Member member;
            try
            {
                member = _meshes.NewMember(group, fresh);
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

    /// <inheritdoc/>
    public int Count { get; private set; }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
                group = _meshes.BuildGroup(mission, soldier.Model, view);
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
                member = _meshes.NewMember(group, new SoldierAgent(view, placement) { IsCutsceneActor = cutsceneActor });
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

    // Every textured subset of a body's own mesh, opaque, through the material the caller set.
    internal void DrawSubsets(Group group, Member member, in Matrix4f modelView)
    {
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
                    held = _meshes.BuildWeapon(mission, weapon.GunModel);
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
                    held = _meshes.BuildWeapon(mission, weapon.GunModel);
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

    /// <inheritdoc/>
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
                _vehicleWeaponVisuals[model] = _meshes.BuildWeapon(mission, model);
            }
        }

        _vehicleGuns = _vehicleWeaponVisuals.Count > 0 ? guns : null;
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
    internal RagdollBody? SeedRagdoll(
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
        _playerBody.ResolveFrameState();
    }

    /// <summary>Computes one soldier's drawn placement, pose blend and motion for this frame.</summary>
    internal void ResolveFrameState(Member member, float alpha, bool smooth)
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

    /// <inheritdoc/>
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

                DrawSubsets(group, member, modelView);

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

    /// <inheritdoc/>
    public bool LoadPlayerBody(LoadedMission mission, string model, BoneAnimationSet animations, Vector3d position, float gamma) =>
        _playerBody.Load(mission, model, animations, position, gamma);

    /// <inheritdoc/>
    public void KillPlayerBody(int deathAnimation, Vector3d feet, float gamma, int bone = -1, Vector3d travel = default, WeaponCategory category = WeaponCategory.Gun, Vector3d velocity = default) =>
        _playerBody.Kill(deathAnimation, feet, gamma, bone, travel, category, velocity);

    /// <inheritdoc/>
    public void TickPlayerBody() =>
        _playerBody.Tick();

    /// <inheritdoc/>
    public void StandPlayerBodyAt(Vector3d feetUnits, float facing) =>
        _playerBody.StandAt(feetUnits, facing);

    /// <inheritdoc/>
    public void FollowPlayerBody(Vector3d feetUnits, float facing, int animation, bool loops, in PlayerBodyPose pose) =>
        _playerBody.Follow(feetUnits, facing, animation, loops, in pose);

    /// <inheritdoc/>
    public BoneAttachment? PlayerBodyHandAttachment() =>
        _playerBody.HandAttachment();

    /// <inheritdoc/>
    public void StartPlayerLadderAnimation(int animation, bool loops, bool backwards = false, bool paused = false) =>
        _playerBody.StartLadderAnimation(animation, loops, backwards, paused);

    /// <inheritdoc/>
    public void StartPlayerInteractionAnimation(int animation, bool loops) =>
        _playerBody.StartInteractionAnimation(animation, loops);

    /// <inheritdoc/>
    public void AdvancePlayerLadderAnimation(float facing, out Vector3d rootDelta, out bool rungBoundary, out bool ended) =>
        _playerBody.AdvanceLadderAnimation(facing, out rootDelta, out rungBoundary, out ended);

    /// <inheritdoc/>
    public void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing) =>
        _playerBody.PlaceClimbing(taskPosition, facing);

    /// <inheritdoc/>
    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection) =>
        _playerBody.Draw(in camera, in projection);

    /// <inheritdoc/>
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
    internal void DrawWeapon(Member member, in Camera3D camera, in Matrix4f projection)
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

    /// <inheritdoc/>
    public bool TryShoot(
        Vector3d origin,
        Vector3d rayEnd,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit)
    {
        hit = default;

        HumanHealth? struck = null;
        SoldierView? struckView = null;
        Vector3d ray = new(rayEnd.X - origin.X, rayEnd.Y - origin.Y, rayEnd.Z - origin.Z);
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
                            rayEnd,
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
                            origin, rayEnd, feet, facing, collision, pose, soldier.RootSample.Z, out candidate);
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
                        origin, rayEnd, feet, facing, pose, out candidate, soldier.RootSample.Z);
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
        _sim.NotifyGunshotMiss(origin, rayEnd, struckView, origin);

        if (struck is null || struckView is null)
        {
            return false;
        }

        // The shot's WeaponHit to the soldier struck (0x47BA54); its own handler bleeds it, hurts it and logs the hit.
        struckView.Soldier.WeaponHit(
            new Igi1WeaponHit(hit, origin, rayEnd, damageFactor, category, Shooter: null, FromPlayer: true).ToShared());
        return true;
    }

    /// <inheritdoc/>
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

        _meshes.ReleaseWeapons();
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
        _playerBody.Release();
        Count = 0;
    }

    /// <summary>Gets whether the holder model remains the visible gun for this ownership state.</summary>
    internal static bool ShouldDrawStaticStationaryGun(AiStationaryGun gun, bool playerUsing)
    {
        ArgumentNullException.ThrowIfNull(gun);
        return !gun.IsOccupied || playerUsing;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

                if (!SoldierSkinning.Upload(_device, member, group.Geometry, group.Body.Model, drawn))
                {
                    continue;
                }

                // The upload moves the old current stream into the previous stream before it
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

    internal sealed class Group(SoldierModelGroup roster, MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack)
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
    internal sealed class Member(SoldierAgent agent, MeshHandle mesh)
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
    internal sealed class HeldWeapon(
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
    internal TexTextureCache Textures => _textureCache ??= new(_device, _resources, _logger, LogCategory) { Loaded = AttachDetail };
}
