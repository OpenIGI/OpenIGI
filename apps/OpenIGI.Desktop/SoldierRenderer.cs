using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

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
/// <see cref="ViewModelRenderer"/>).
/// </para>
/// </remarks>
public sealed class SoldierRenderer : IDisposable
{
    private const string LogCategory = "Soldier";

    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    private const double ProbeAboveInUnits = 2048.0;

    /// <summary>How far below the declared position the probe reaches — 2 m, the sliding step-down budget.</summary>
    private const double ProbeBelowInUnits = 8192.0;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Group> _groups = [];
    private readonly Dictionary<SoldierView, HumanHealth> _health = [];

    // A dead soldier cannot stay in a shared group: the group is skinned from ONE representative's pose, so
    // a corpse there would keep standing in the group's idle animation. Each gets its own single-member
    // group instead, ticked and re-skinned from its own rig, which is what lets it fall.
    private readonly List<Group> _dead = [];
    private readonly Dictionary<SoldierView, AiSoldier> _brains = [];
    private int _deathsSoFar;
    private int _walkAnimation = -1;
    private int _tick;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SoldierRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoldierRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of soldiers that resolved and will draw.</summary>
    public int Count { get; private set; }

    /// <summary>Builds the drawable soldiers from a loaded level.</summary>
    /// <param name="mission">The mission whose packs hold the models and animation sets.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="terrain">The level's terrain, for standing soldiers on the ground it declares.</param>
    /// <param name="cast">The placed-object ray cast, for soldiers standing on floors and platforms.</param>
    /// <returns>The number of soldiers that resolved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int Load(LoadedMission mission, GameTask levelRoot, TerrainData? terrain = null, WorldRayCast? cast = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();

        Dictionary<(string Model, int Skeleton, int Stand), Group> groups = [];
        foreach (HumanSoldierTask soldier in Walk(levelRoot))
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
            }

            group.Soldiers.Add((view, Stand(soldier, group, terrain, cast)));
            Count++;
        }

        _logger.Information(
            LogCategory,
            $"{Count} soldier(s) over {_groups.Count} distinct (model, skeleton, animation) triple(s).");
        return Count;
    }

    /// <summary>Puts a soldier's feet on whatever is under its declared position.</summary>
    /// <remarks>
    /// <para>
    /// In the original the level's declared <c>Position</c> is only where a soldier <em>starts</em>: its
    /// update runs the same movement integrator as the player's, whose ground pass (<c>0x462390</c>)
    /// snaps the body onto the surface below within the first ticks. The port does not tick soldiers
    /// yet, so a body drawn with its skeleton root at the declared height stands buried to the waist
    /// wherever the position was authored at floor level.
    /// </para>
    /// <para>
    /// This reproduces the settled state, not the mechanism: probe down from half a metre above the
    /// declared position — placed objects first, terrain as the fallback, highest surface wins — and
    /// stand the <b>skin's lowest vertex</b> on it. The original's own foot reference is
    /// <c>0x48A340</c>'s bone-0-plus-<c>Human+1300</c> offset, and the <c>+1300</c> term is still
    /// unread (<c>player.md</c> §14.9), so the mesh's own extent is the honest stand-in. A recorded
    /// divergence, retired with the soldier update.
    /// </para>
    /// </remarks>
    private static Vector3d Stand(HumanSoldierTask soldier, Group group, TerrainData? terrain, WorldRayCast? cast)
    {
        Vector3d declared = soldier.Position;
        double probeTop = declared.Z + ProbeAboveInUnits;
        double groundZ = declared.Z;

        if (cast is not null
            && cast(
                new Vector3d(declared.X, declared.Y, probeTop),
                new Vector3d(declared.X, declared.Y, declared.Z - ProbeBelowInUnits),
                out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
        }

        if (terrain is not null
            && TerrainHeightQuery.TryGetGround(terrain, declared.X, declared.Y, probeTop, out TerrainGround ground)
            && ground.Height >= declared.Z - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = Math.Max(groundZ, ground.Height);
        }

        return new Vector3d(
            declared.X / WorldScene.UnitsPerMetre,
            declared.Y / WorldScene.UnitsPerMetre,
            (groundZ / WorldScene.UnitsPerMetre) - group.LowestVertexZ);
    }

    /// <summary>Gives every soldier an AI that patrols the level's nav graph and reacts to the player.</summary>
    /// <param name="graph">The graph to patrol. Nothing is attached when it holds fewer than two nodes.</param>
    /// <param name="sight">The world cast used for line of sight, or <see langword="null"/> to see through walls.</param>
    /// <param name="scripts">Where the archetype behaviour script is read from, or <see langword="null"/> for none.</param>
    /// <param name="natives">The registry the behaviour script resolves its AI natives against.</param>
    /// <param name="levelRoot">
    /// The level's task tree, which the authored patrol paths are collected from. Without it a soldier has
    /// no path to walk and stands where the level put it.
    /// </param>
    /// <returns>How many soldiers got a brain.</returns>
    /// <remarks>
    /// The phase is the soldier's index modulo four, which is what staggers the level's detection sweeps
    /// across the four ticks the original spreads them over (<c>ai.md</c> §5.1).
    /// </remarks>
    public int Animate(
        GraphFile graph,
        WorldRayCast? sight = null,
        ResourceRegistry? scripts = null,
        NativeRegistry? natives = null,
        GameTask? levelRoot = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _brains.Clear();
        if (graph.RouteTable is null || graph.Nodes.Count < 2)
        {
            _logger.Warning(LogCategory, "The nav graph has no route table, so no soldier can patrol.");
            return 0;
        }

        // The walk clip is the locomotion table's forward-travelling state (state 1 travels +Y), read from
        // 0x538870 -- not a chosen id.
        _walkAnimation = HumanLocomotionStates.For(1).AnimationId;

        // The level's authored patrol paths, keyed by the PatrolPath task's own id -- what AIAction_Patrol's
        // first argument names (ai.md 7.1).
        AiPatrolPathRegistry? paths = levelRoot is null ? null : AiPatrolPathRegistry.Build(levelRoot);
        if (paths is not null)
        {
            _logger.Information(LogCategory, $"{paths.Count} authored patrol path(s) in this level.");
        }

        AiScriptHost? host = scripts is not null && natives is not null ? new AiScriptHost(natives, _logger) : null;
        Dictionary<string, QvmProgram?> scriptCache = new(StringComparer.Ordinal);

        int index = 0;
        int patrolling = 0;
        foreach (Group group in _groups)
        {
            foreach ((SoldierView view, Vector3d _) in group.Soldiers)
            {
                AiSoldier brain = new(graph, view.Position, GammaOf(view), index & 3, index);
                brain.SetPatrolPaths(paths);
                if (sight is not null)
                {
                    // 0x489B20: the target is seen when the cast is clear, OR when whatever blocked it sits at
                    // or past 0.79 of the way there - so clipping a corner near the target does not hide it.
                    brain.SetLineOfSight((from, to) =>
                    {
                        if (!sight(from, to, out WorldRayHit blocked))
                        {
                            return true;
                        }

                        double total = Length(from, to);
                        return total <= 0.0
                            || Length(from, blocked.Position) / total >= AiSoldier.SightObstructionTolerance;
                    });
                }

                // Which script this soldier runs: its own MISSION:AI/<taskid>.qvm when the level ships one,
                // otherwise the archetype its AIType selects (ai.md 7.5a and 2.3). The per-task script is
                // what names the soldier's patrol path, so without it a guard has nothing to patrol.
                HumanAiTask? mind = FindMind(view);
                if (host is not null && scripts is not null)
                {
                    QvmProgram? script = null;
                    if (mind is not null && mind.Id != GameTask.NoId)
                    {
                        script = LoadScript(scripts, scriptCache, AiScriptApi.MissionScriptPath(mind.Id));
                    }

                    AiArchetype archetype = mind?.Archetype ?? AiArchetype.Guard;
                    script ??= LoadScript(scripts, scriptCache, AiScriptApi.ScriptPath(archetype));

                    if (script is not null)
                    {
                        brain.SetScript(host, script);
                        patrolling++;
                    }
                }

                _brains[view] = brain;
                index++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{_brains.Count} soldier(s) with brains ({patrolling} scripted), walk animation {_walkAnimation}.");
        return _brains.Count;
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
    private static HumanAiTask? FindMind(SoldierView view)
    {
        foreach (GameTask child in view.Soldier.Children)
        {
            if (child is HumanAiTask mind)
            {
                return mind;
            }
        }

        return null;
    }

    private QvmProgram? LoadScript(ResourceRegistry scripts, Dictionary<string, QvmProgram?> cache, string qscPath)
    {
        string path = qscPath.Replace(".qsc", ".qvm", StringComparison.Ordinal);
        if (cache.TryGetValue(path, out QvmProgram? cached))
        {
            return cached;
        }

        QvmProgram? program = null;
        try
        {
            program = QvmProgram.Read(scripts.Load(path));
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException or InvalidOperationException)
        {
            // A level ships a per-task script only for the soldiers it wants to override, so a miss here is
            // ordinary rather than an error.
            _logger.Debug(LogCategory, $"No behaviour script at '{path}': {ex.Message}");
        }

        cache[path] = program;
        return program;
    }

    private static float GammaOf(SoldierView view) => view.Soldier.Gamma;

    private static double Length(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
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
    public void Tick(Vector3d enemy = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tick++;

        // EVERY soldier's rig is advanced, not just each group's representative. Ticking only the
        // representative left every other soldier's BoneObject frozen, so its RootMotion() was zero and the
        // AI never moved it -- while the shared group mesh, posed from the representative, showed it walking
        // on the spot. Exactly one soldier per group actually moved: that group's representative.
        foreach ((SoldierView view, AiSoldier _) in _brains)
        {
            view.Tick();
        }

        // The AI runs second, so the root motion it is handed is this tick's, not last tick's.
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (view.IsDead)
            {
                brain.Kill();
                continue;
            }

            double travel = Math.Abs(view.RootMotion().Y);
            brain.Tick(_tick, enemy, travel);

            // A patrol ANIMATION command names a clip outright; otherwise the gait picks between the walk
            // cycle and the soldier's own stand animation.
            if (brain.RequestedAnimation >= 0)
            {
                view.SetMoving(true, brain.RequestedAnimation);
            }
            else
            {
                view.SetMoving(brain.Activity == AiSoldierActivity.Patrolling, _walkAnimation);
            }

            view.PlaceAt(brain.Position, brain.Orientation);
        }

        foreach (Group group in Enumerable.Concat(_groups, _dead))
        {
            if (group.Soldiers.Count == 0 || group.Model is null)
            {
                continue;
            }

            SoldierView representative = group.Soldiers[0].View;
            if (!_brains.ContainsKey(representative))
            {
                // Un-brained soldiers still need their own rig advanced.
                representative.Tick();
            }

            Vertex3D[] skinned = [.. group.Geometry.Vertices];
            try
            {
                MefSkinner.Skin(
                    group.Model,
                    representative.UnrotatedPose(),
                    (float)(1.0 / WorldScene.UnitsPerMetre),
                    skinned);
            }
            catch (ArgumentException)
            {
                continue;
            }

            MeshHandle replacement;
            try
            {
                replacement = _device.CreateMesh(skinned, group.Geometry.Indices);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            _device.DestroyMesh(group.Mesh);
            group.Mesh = replacement;
        }
    }

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_groups.Count == 0)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        foreach (Group group in Enumerable.Concat(_groups, _dead))
        {
            foreach ((SoldierView soldier, Vector3d metres) in group.Soldiers)
            {
                // A dead soldier keeps being drawn -- a body stays on the ground in the original, and
                // vanishing was only ever the port having no death animation to play. SoldierView.Kill
                // starts the id the hit-zone table names and the clip holds its last frame.
                //
                // KNOWN GAP: the group shares one skinned mesh, so a dead soldier is still posed by the
                // group's stand animation until per-soldier skinning lands. It stays upright rather than
                // falling. Recorded in TODO.md.

                // A soldier with a brain is drawn where its AI put it; one without keeps the stood position
                // the level authored, converted once at load.
                Vector3d where = _brains.ContainsKey(soldier)
                    ? new Vector3d(
                        soldier.Position.X / WorldScene.UnitsPerMetre,
                        soldier.Position.Y / WorldScene.UnitsPerMetre,
                        metres.Z)
                    : metres;

                Matrix4f modelView = camera.CreateModelView(where, soldier.Orientation);

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
                        diffuse = GetTexture(texture);
                    }

                    _device.DrawMesh(
                        group.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        diffuse,
                        TextureHandle.None,
                        BlendMode.Opaque);
                }
            }
        }
    }

    /// <summary>Offers a round to every soldier and scores it against the nearest one it meets.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="hit">The nearest soldier struck.</param>
    /// <returns><see langword="false"/> when the round meets nobody.</returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, because the health belongs to the soldier and not to the
    /// round. A soldier already dead is skipped rather than shot again — the original stops a corpse
    /// taking damage through a flag on the human, and this is the same effect with the state that
    /// exists.
    /// </remarks>
    public bool TryShoot(Vector3d origin, Vector3d end, float damageFactor, out HumanHit hit)
    {
        hit = default;
        HumanHealth? struck = null;
        SoldierView? struckView = null;

        foreach (Group group in _groups)
        {
            foreach ((SoldierView soldier, Vector3d _) in group.Soldiers)
            {
                if (!_health.TryGetValue(soldier, out HumanHealth? health))
                {
                    health = new HumanHealth(HumanHitZoneTable.Soldier);
                    _health[soldier] = health;
                }

                if (health.IsDead)
                {
                    continue;
                }

                // The facing is the soldier's own declared orientation, whose +Y is forward -- the same
                // column 0x462E50 takes the shot direction out of.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);

                if (!HumanHitTest.TryCast(
                        origin, end, soldier.Position, facing, soldier.Pose(), out HumanHit candidate))
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

        if (struck is null)
        {
            return false;
        }

        struck.Apply(hit.Zone, damageFactor);

        // On death, play one of the soldier class's two death animations - the literal ids 24 and 25 in its
        // two death state-entry functions (0x45C5A0 and 0x45C650). Which of the two the original picks
        // depends on the unread transition into the state, so this alternates, which at least varies the
        // corpses the way the game does.
        if (struck.IsDead && struckView is not null)
        {
            int which = _deathsSoFar++ % SoldierView.DeathAnimations.Count;
            struckView.Kill(SoldierView.DeathAnimations[which]);
            Detach(struckView);
        }

        return true;
    }

    /// <summary>Moves a soldier out of its shared group into one of its own, so it can pose independently.</summary>
    /// <remarks>
    /// Its own mesh is uploaded from its own rig, which is what makes a death animation visible. Without this
    /// a killed guard stays standing in the group's idle pose — which reads exactly like the shot never
    /// registered, and is worse than the old behaviour of vanishing.
    /// </remarks>
    private void Detach(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            int index = group.Soldiers.FindIndex(s => ReferenceEquals(s.View, view));
            if (index < 0)
            {
                continue;
            }

            (SoldierView View, Vector3d Position) entry = group.Soldiers[index];
            group.Soldiers.RemoveAt(index);

            if (group.Model is null)
            {
                return;
            }

            MeshHandle mesh;
            try
            {
                mesh = _device.CreateMesh(group.Geometry.Vertices, group.Geometry.Indices);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            Group solo = new(group.Geometry, mesh, group.Instance, group.Pack, group.LowestVertexZ)
            {
                Model = group.Model,
            };
            solo.Soldiers.Add(entry);
            _dead.Add(solo);
            return;
        }
    }

    /// <summary>Gets a delegate that offers rounds to these soldiers.</summary>
    /// <returns>The delegate the player takes.</returns>
    public HumanShot Shooter() => TryShoot;

    /// <summary>Gets how many soldiers have been killed.</summary>
    public int Killed
    {
        get
        {
            int dead = 0;
            foreach (HumanHealth health in _health.Values)
            {
                if (health.IsDead)
                {
                    dead++;
                }
            }

            return dead;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();

        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _textures.Clear();
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
        // Stand() rests on the ground. Only the base vertices count - the array's tail holds the
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
        return new Group(geometry, mesh, instance, pack, lowest) { Model = model };
    }

    private static IEnumerable<HumanSoldierTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanSoldierTask soldier)
            {
                yield return soldier;
            }

            foreach (HumanSoldierTask deeper in Walk(child))
            {
                yield return deeper;
            }
        }
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

    private void Release()
    {
        foreach (Group group in Enumerable.Concat(_groups, _dead))
        {
            if (group.Mesh.IsValid)
            {
                _device.DestroyMesh(group.Mesh);
            }
        }

        _groups.Clear();
        _dead.Clear();
        _health.Clear();
        Count = 0;
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        if (_resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(
                        image.Width,
                        image.Height,
                        image.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Repeat),
                    image.Pixels);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }

        _textures[name] = handle;
        return handle;
    }

    private sealed class Group(MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack, float lowestVertexZ)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; set; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        public float LowestVertexZ { get; } = lowestVertexZ;

        public List<(SoldierView View, Vector3d Position)> Soldiers { get; } = [];

        /// <summary>The model every soldier in the group draws, kept so the group can be re-skinned each tick.</summary>
        public MefModel? Model { get; set; }
    }
}
