using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

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
public sealed partial class SoldierRenderer : ISoldierPresenter, IDisposable
{
    internal const string LogCategory = "Soldier";

    private readonly IGraphicsDevice _device;

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly Dictionary<object, BinocularMarkerColor> _markedOthers = [];
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;
    private readonly List<SoldierGroup> _groups = [];

    // What each soldier, stationary gun and vehicle gun is armed with.
    private readonly SoldierArmoury _armoury;

    // Where each body is drawn between ticks, and its corpse.
    private readonly SoldierFrameInterpolator _frames;

    // What the rounds and the pick meet.
    private readonly SoldierHitTesting _hits;

    // What draws the bodies, the guns and the flames.
    private readonly SoldierWeaponDrawer _drawer;

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

    /// <summary>
    /// Gets or sets where the level's fixed-function light is read from: the original draws a soldier's body (<c>0x49F700</c>)
    /// and the gun in its hand (<c>0x49DF60</c>) through the lit state, the object ambient plus the Dirlight.
    /// </summary>
    public Func<FixedFunctionSun?>? Sun
    {
        get => _drawer.Sun;
        set => _drawer.Sun = value;
    }

    /// <summary>
    /// Gets or sets where the level's detail settings are read from: the original draws a distant soldier from its model's lower
    /// levels (<c>0x49F700</c>), and past its cutoff not at all. None draws every body whole.
    /// </summary>
    public Func<ModelLodPolicy?>? Lod
    {
        get => _drawer.Lod;
        set => _drawer.Lod = value;
    }

    /// <inheritdoc/>
    /// <remarks>Project I.G.I.'s sector cull gates each soldier as <c>QCube_CallHandlers</c> gates every queued object.</remarks>
    public IPlacementVisibility? Visibility
    {
        get => _drawer.Visibility;
        set => _drawer.Visibility = value as Game.Igi1.World.Igi1SectorVisibility;
    }

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

    /// <summary>Gets how far this frame sits between the last two ticks, as <see cref="BeginFrame"/> resolved it.</summary>
    internal float FrameAlpha => _frames.FrameAlpha;

    /// <summary>Gets whether this frame is drawn between ticks rather than at the last one.</summary>
    internal bool FrameSmooth => _frames.FrameSmooth;

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
        _frames = new SoldierFrameInterpolator(_groups, _sim, _logger);
        _hits = new SoldierHitTesting(_groups, _sim, _frames, _logger);
        _armoury = new SoldierArmoury(_groups, _sim, _meshes, _logger);
        _drawer = new SoldierWeaponDrawer(_groups, _device, _armoury, _frames, () => Textures, _logger);
        _playerBody = new PlayerBodyRenderer(this, _device, _sim, _logger);
        _sim.SpawnedBody = (generator, roster, fresh, dead) =>
        {
            SoldierGroup group = _groups.First(candidate => ReferenceEquals(candidate.Roster, roster));
            SoldierMember member;
            try
            {
                member = _meshes.NewMember(group, fresh);
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
            {
                _logger.Warning(LogCategory, $"{generator.Name} could not upload its cloned Human: {ex.Message}");
                return false;
            }

            SoldierHeldWeapon? weapon = null;
            if (dead.WeaponType is { } weaponType)
            {
                _armoury.TryGetHeldWeapon(weaponType.GunModel, out weapon);
            }

            member.Weapon = weapon;
            group.AddDrawn(member);
            Count++;
            return true;
        };

        // A generated guard's body leaves the world before its replacement comes (0x44F410's unregister): it is drawn no more.
        _sim.BodyRemoved = agent =>
        {
            foreach (SoldierGroup group in _groups)
            {
                if (group.Soldiers.Find(candidate => ReferenceEquals(candidate.Agent, agent)) is { } corpse)
                {
                    group.Soldiers.Remove(corpse);
                    if (corpse.Mesh.IsValid)
                    {
                        _device.DestroyMesh(corpse.Mesh);
                    }

                    SoldierLevelOfDetail.Drop(_device, corpse);

                    Count--;
                    return;
                }
            }
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

        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
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
    public IReadOnlyDictionary<object, BinocularMarkerColor> BinocularMarkedOthers
    {
        get
        {
            _markedOthers.Clear();
            foreach ((IAiDetectable human, BinocularMarkerColor marker) in _sim.BinocularJoinedDetected)
            {
                _markedOthers[human] = marker;
            }

            return _markedOthers;
        }
    }

    /// <inheritdoc/>
    public void DrawBinocularOutlines(in Camera3D camera, float radiusPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sim.BinocularDetected.Count == 0)
        {
            return;
        }

        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
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
                BinocularMarking.Draw(
                    _device,
                    member.Mesh,
                    CollectionsMarshal.AsSpan(_outlineRanges),
                    camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation),
                    marker,
                    radiusPixels);
                _device.ClearPoseInterpolation();
            }
        }
    }

    /// <summary>Where the player's body model is drawn, in engine units: retail's <c>Human+32</c>, for scenario runs.</summary>
    internal Vector3d PlayerBodyDrawnOriginForTesting => _playerBody.DrawnOriginForTesting;

    /// <summary>The player body clip's current root sample height (<c>Human+1308</c>), for scenario runs.</summary>
    internal double PlayerBodyRootSampleZForTesting => _sim.PlayerBodyView?.RootSample.Z ?? 0.0;

    internal bool IsInViewForTesting(SoldierView view) => FindMember(view)?.Agent.InView ?? false;

    /// <summary>Gets the groups the renderer draws, each with its soldiers, for the suites that inspect them.</summary>
    internal IReadOnlyList<SoldierGroup> GroupsForTesting => _groups;

    /// <summary>Where one soldier's body is placed for the current tick, as the tick capture places it.</summary>
    /// <param name="member">The soldier.</param>
    /// <returns>The placement, in metres, and the orientation.</returns>
    internal (Vector3d Where, Matrix3f Orientation) TickPlacementOfForTesting(SoldierMember member) => _frames.TickPlacementOf(member);

    internal string DescribeViewForTesting(SoldierView view)
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
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

        Dictionary<(string Model, int Skeleton, int Stand), SoldierGroup> groups = [];
        foreach (HumanSoldierTask soldier in levelRoot.Descendants().OfType<HumanSoldierTask>())
        {
            // Another game drives it: its body, brain and drawing are that game's.
            if (soldier.Driver is not null)
            {
                continue;
            }

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
            if (!groups.TryGetValue(key, out SoldierGroup? group))
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
            SoldierMember member;
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
        _hits.WarmHitTestCaches();
        return Count;
    }

    // Every textured subset of a body's own mesh, opaque, through the material the caller set.
    internal void DrawSubsets(SoldierGroup group, SoldierMember member, in Matrix4f modelView) =>
        _drawer.DrawSubsets(group, member, modelView);

    /// <inheritdoc/>
    public bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone) =>
        _hits.TryPick(originUnits, direction, reachUnits, out name, out distanceUnits, out feetUnits, out zone);

    /// <inheritdoc/>
    public int Arm(LoadedMission mission, WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(roster);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _armoury.Arm(mission, roster, Count);
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
        _armoury.ArmVehicleGuns(mission, weaponModels, guns);
    }

    private SoldierGroup? FindGroup(SoldierView view)
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (ReferenceEquals(member.Agent.View, view))
                {
                    return group;
                }
            }
        }

        return null;
    }

    private SoldierMember? FindMember(SoldierView view)
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
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
    /// <remarks>See <see cref="SoldierFrameInterpolator.SeedRagdoll"/>.</remarks>
    internal RagdollBody? SeedRagdoll(
        SoldierView view,
        Vector3d originInUnits,
        Matrix3f orientation,
        int bone,
        Vector3d travel,
        WeaponCategory category,
        Vector3d carriedVelocity,
        MefModel? model,
        SoldierHeldWeapon? weapon) =>
        _frames.SeedRagdoll(view, originInUnits, orientation, bone, travel, category, carriedVelocity, model, weapon);

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
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                // Human_DrawHandler (0x45F280) is reached past the sector cull, and stamps the render only when the frustum
                // sphere lets the body through; the eye is refreshed only within the LOD range on top of that.
                member.Agent.DrawnLastRender = ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier)
                    && member.HasFrameState
                    && _drawer.IsSectorVisible(group, member)
                    && frustum.SphereVisible(camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation), test);
                member.Agent.InView = member.Agent.DrawnLastRender
                    && Vector3d.Distance(member.DrawnWhere, camera.Position) <= EyeRefreshDistanceMetres;
            }
        }
    }

    /// <inheritdoc/>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _frames.BeginFrame(tickFraction, smooth);

        // The player's own body is not in _groups, so it resolves here rather than in the loop -- and it
        // has to resolve HERE rather than inside its draw, because its draw now runs once per shadow
        // cascade as well as once for the geometry pass, and the frame bookkeeping below records what was
        // on screen LAST frame. Doing it per draw would make a body cast three shadows' worth of history
        // and report itself as never having moved.
        _playerBody.ResolveFrameState();
        ResolvePartnerFrameStates();
        _preview?.ResolveFrameState();
    }

    /// <inheritdoc/>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _drawer.Draw(camera, projection);
    }

    /// <inheritdoc/>
    public bool LoadPlayerBody(LoadedMission mission, string model, BoneAnimationSet animations, Vector3d position, float gamma)
    {
        RememberPartnerLoad(mission, model, animations);
        return _playerBody.Load(mission, model, animations, position, gamma);
    }

    /// <inheritdoc/>
    public void RevivePlayerBody() => _playerBody.Revive();

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
    public void AdvancePlayerLadderAnimation(float facing, out Vector3d rootDelta, out float? rungEventTime, out bool ended) =>
        _playerBody.AdvanceLadderAnimation(facing, out rootDelta, out rungEventTime, out ended);

    /// <inheritdoc/>
    public void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing) =>
        _playerBody.PlaceClimbing(taskPosition, facing);

    /// <inheritdoc/>
    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        _playerBody.Draw(in camera, in projection);
        DrawPartners(in camera, in projection);
    }

    /// <inheritdoc/>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _drawer.DrawMuzzleFlashes(camera, projection);
        DrawPartnerMuzzleFlashes(camera, projection);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player's round, as the shot's trace offers it to the humans it meets: each living soldier weighs it
    /// (<c>Human</c>'s <c>WeaponCollisionEvaluate</c>, <c>0x45E3E0</c>), the nearest that agrees takes its
    /// <c>WeaponHit</c>, and the rest hear it pass. The round is cast at the body the crosshair was drawn over.
    /// </remarks>
    public bool TryShoot(
        Vector3d origin,
        Vector3d rayEnd,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit) =>
        TryShoot(origin, rayEnd, damageFactor, category, double.PositiveInfinity, alreadyHeard: false, out hit);

    /// <summary>Offers the player's round to the soldiers short of the surface that stops it.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="rayEnd">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="category">The firing weapon's category.</param>
    /// <param name="stopDistance">The distance to the surface the round's trace met first; see <see cref="HumanShot"/>.</param>
    /// <param name="alreadyHeard">Whether this round's near miss was already offered; see <see cref="HumanShot"/>.</param>
    /// <param name="hit">The nearest crossing of a soldier's skin; an exit is reported without damage.</param>
    /// <returns><see langword="false"/> when the round meets nobody short of the stop.</returns>
    public bool TryShoot(
        Vector3d origin,
        Vector3d rayEnd,
        float damageFactor,
        WeaponCategory category,
        double stopDistance,
        bool alreadyHeard,
        out HumanHit hit) =>
        LevelBodies.Shoot(
            [this],
            origin,
            rayEnd,
            _sim.PlayerTask,
            stopDistance,
            alreadyHeard,
            struck => new Igi1WeaponHit(struck, origin, rayEnd, damageFactor, category, Shooter: null, FromPlayer: true).ToShared(),
            out hit);

    /// <inheritdoc/>
    public HumanShot Shooter() => TryShoot;

    /// <inheritdoc/>
    public IEnumerable<IShootableBody> Bodies() => _hits.Bodies();

    /// <inheritdoc/>
    public void RoundCast(Vector3d origin, Vector3d target, GameTask? shooter) => _sim.RefreshEyesAlongPlayerRound(origin, target);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human_EvaluateGunShotHandler</c> evaluates each live projectile segment independently of ordinary shot
    /// audibility, after the nearest collision is chosen, so the struck guard gets the TAKINGDAMAGE path while nearby
    /// guards get GUNSHOTMISS.
    /// </remarks>
    public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck) =>
        _sim.NotifyGunshotMiss(origin, target, struck is HumanTask human ? _sim.ViewOf(human) : null, origin);

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
        _gunShotTracers?.Dispose();
        _gunShotTracers = null;

        _textureCache?.Clear();
        ReleaseShadows();
    }

    private void Release()
    {
        foreach (SoldierGroup group in _groups)
        {
            if (group.Mesh.IsValid)
            {
                _device.DestroyMesh(group.Mesh);
            }

            // Each soldier owns a mesh of its own now, and every one of them has to go back.
            foreach (SoldierMember member in group.Soldiers)
            {
                if (member.Mesh.IsValid)
                {
                    _device.DestroyMesh(member.Mesh);
                }

                SoldierLevelOfDetail.Drop(_device, member);
            }
        }

        _meshes.ReleaseWeapons();
        _armoury.Release();
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
        ReleasePartners();
        Count = 0;
    }

    /// <summary>Where a soldier's gun muzzle is drawn this frame, in world units.</summary>
    /// <param name="shooter">The round's shooter, as the march records it.</param>
    /// <returns>
    /// The held weapon's gun-flame part on the hand that holds it -- where the muzzle flash is drawn -- or the hand
    /// itself for a model without one; <see langword="null"/> for anything that is not a soldier with a drawn gun.
    /// </returns>
    public Vector3d? MuzzleOf(object? shooter)
    {
        if (shooter is SoldierView gunner && _sim.Brains.TryGetValue(gunner, out Igi1HumanAi? brain) && brain.MountedGun is { } gun)
        {
            return gun.MuzzlePosition;
        }

        if (shooter is not SoldierView view || FindMember(view) is not { HasWeaponFrameState: true } member)
        {
            return null;
        }

        Vector3d hand = member.DrawnWeaponOrigin;
        Vector3f offset = member.Weapon?.Presentation?.MuzzleMetres is { } muzzle
            ? member.DrawnWeaponRotation.Transposed().Transform(muzzle)
            : default;
        return new Vector3d(hand.X + offset.X, hand.Y + offset.Y, hand.Z + offset.Z) * WorldScene.UnitsPerMetre;
    }

    /// <summary>Gets every soldier gun's live tracers, in the order the guns were made, for the tracer draw.</summary>
    public IEnumerable<GunShotTracer> GunShotTracers => _sim.GunShotTracers;

    /// <inheritdoc/>
    public void DrawGunShotTracers(in Camera3D camera, in Matrix4f projection, int renderHeight, bool retailLine)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gunShotTracers ??= new GunShotTracerRenderer(_device);
        _gunShotTracers.Draw(
            GunShotTracers,
            camera,
            projection,
            renderHeight,
            retailLine ? TracerStyle.Retail : TracerStyle.Remastered,
            MuzzleOf);
    }

    // The rounds' tracers, made on the first frame that draws any.
    private GunShotTracerRenderer? _gunShotTracers;

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
        _shadowTick++;

        _frames.StepRagdolls();

        // A GunPickup simulates independently after a dead human drops it, including when the optional
        // visual corpse ragdoll is off. Sharing these probes keeps its walls and floor identical to the
        // ones that stop the body.
        _sim.TickAfterCorpses();

        foreach (SoldierGroup group in _groups)
        {
            if (group.Body.Model is null)
            {
                continue;
            }

            foreach (SoldierMember member in group.Soldiers)
            {
                BonePose drawn = SoldierFrameInterpolator.PoseToDraw(member);
                member.DrawnPose = drawn;

                // A stopped one-shot (death, interaction or paused ladder pose) needs one duplicate
                // upload to settle both interpolation streams, then no more skinning or driver traffic.
                bool posed = member.PoseUploads.ShouldUpload(drawn)
                    && SoldierSkinning.Upload(_device, member, group.Geometry, group.Body.Model, drawn);
                if (!posed)
                {
                    FollowLevel(group, member, posed: false);
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

                FollowLevel(group, member, posed: true);
            }
        }

        // With the poses settled, record where each body stands and faces THIS tick. The presentation
        // draws between the last two of these records, so they are the whole interface between the
        // 30 Hz simulation and however fast the display runs.
        _frames.CaptureTickStates();
    }

    // Poses a soldier's lower level for the eye it was last drawn from, or frees it once the body is drawn whole again.
    private void FollowLevel(SoldierGroup group, SoldierMember member, bool posed)
    {
        int level = _drawer.Lod?.Invoke() is { HasBoneSettings: true } lod && _drawer.LastEye is { } eye
            ? SoldierLevelOfDetail.Select(lod, group, member, eye, _drawer.LastFieldOfView)
            : 0;
        SoldierLevelOfDetail.Follow(_device, group, member, level, posed);
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
