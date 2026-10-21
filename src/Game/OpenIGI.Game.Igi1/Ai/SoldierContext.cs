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
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What the parts of <see cref="SoldierSimulation"/> share: the roster and its brains, their health and combat rows, the
/// level's one random stream and tick count, the world the bodies are cast against and the ground they stand on.
/// </summary>
/// <remarks>
/// The simulation owns it and coordinates; every part it hands this to reads and writes the same tables rather than a
/// copy, so a brain made by one part is the brain every other part ticks, hits and hears.
/// </remarks>
/// <param name="logger">Where the soldiers report.</param>
internal sealed class SoldierContext(IOpenIgiLogger logger)
{
    /// <summary>The category every soldier line is logged under, and the prefix of the narrower ones.</summary>
    public const string LogCategory = "Soldier";

    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    public const double ProbeAboveInUnits = 2048.0;

    /// <summary>How far below the declared position the probe reaches — 2 m, the sliding step-down budget.</summary>
    // The terrain query itself only reaches this far down (TerrainHeightQuery.SearchDepthUnits), so
    // widening the acceptance window to the same depth settles every surface the query can find.
    // Level 4 authors several soldiers 2.3-3.1m above the terrain (larger than the old 2m window),
    // which otherwise float; a placed surface the ray cast finds (a roof, a vehicle) is higher than the
    // terrain and wins, so a soldier standing on one is not pulled down. A character far above the
    // terrain (a rooftop watcher) has no surface within the search depth and is left alone.
    public const double ProbeBelowInUnits = 16385.0;

    /// <summary>This frame's ground-snap results by queried position; see <see cref="SnapToGround"/>.</summary>
    private readonly Dictionary<(double X, double Y, double Z), double> _groundSnaps = [];

    /// <summary>Gets where the soldiers report.</summary>
    public IOpenIgiLogger Logger { get; } = logger;

    /// <summary>Gets the soldiers as the simulation walks them: each drawn group's roster, in the groups' order.</summary>
    public List<SoldierModelGroup> Roster { get; } = [];

    /// <summary>Gets each soldier's damage state.</summary>
    public Dictionary<SoldierView, HumanHealth> Health { get; } = [];

    /// <summary>Gets each soldier's detector marker scale, in metres.</summary>
    // FUN_004D9620 writes Human+0xA0 from FUN_004D0950(model), which reads the authored model bound
    // at model+0x78. This is the exact value used by the retail detector; the derived vertex radius is
    // not a substitute because it is materially smaller for the retail soldier models.
    public Dictionary<SoldierView, float> DetectorMarkerScaleMetres { get; } = [];

    /// <summary>Gets or sets the stream the soldiers draw from -- the level's one global stream.</summary>
    public IgiRandom AiRandom { get; set; } = new();

    /// <summary>Gets each soldier's combat row.</summary>
    // Each soldier's combat row, resolved from its own AIType at the difficulty in force. It is what
    // gives a guard its maximum health -- sub_406F70 writes the row's "Damage scale" over Human+600,
    // so a guard is worth 0.3 of a person on easy and 0.8 on hard, not the constructor's 1.0.
    public Dictionary<SoldierView, AiCombatParameters> Combat { get; } = [];

    /// <summary>Gets each soldier's brain.</summary>
    // A dead soldier cannot stay in a shared group: the group is skinned from ONE representative's pose, so
    // a corpse there would keep standing in the group's idle animation. Each gets its own single-member
    // group instead, ticked and re-skinned from its own rig, which is what lets it fall.
    public Dictionary<SoldierView, Igi1HumanAi> Brains { get; } = [];

    /// <summary>Gets each brain by its runtime id.</summary>
    public Dictionary<int, Igi1HumanAi> BrainsByRuntimeId { get; } = [];

    /// <summary>Gets each soldier's view by its task, for the hit events the task receives.</summary>
    public Dictionary<HumanTask, SoldierView> ViewsByTask { get; } = [];

    /// <summary>Gets or sets the simulation's tick count.</summary>
    public int TickCount { get; set; }

    /// <summary>Gets or sets the player's team, resolved once per level from the declared HumanPlayer task.</summary>
    public int PlayerTeam { get; set; }

    /// <summary>Gets or sets the level's player task, which the player's rounds name as their shooter.</summary>
    public HumanPlayerTask? PlayerTask { get; set; }

    /// <summary>Gets or sets the combat-target record the brains fight the player through.</summary>
    public AiCombatTarget PlayerTarget { get; set; } = new("HumanPlayer");

    /// <summary>Gets or sets the level's terrain.</summary>
    public IGroundHeightField? Terrain { get; set; }

    /// <summary>Gets or sets the cast against placed collision a body stands on and a round meets.</summary>
    public WorldRayCast? Cast { get; set; }

    /// <summary>Gets or sets the cast a sight line is tested with.</summary>
    public WorldRayCast? Sight { get; set; }

    /// <summary>Gets or sets the placed-object collision index.</summary>
    public ObjectCollisionIndex? Solids { get; set; }

    /// <summary>Gets or sets the active authored cutscene transform source.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Forgets the ground heights the bodies were set down on, for a new level.</summary>
    public void ForgetGroundSnaps() => _groundSnaps.Clear();

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
    /// unread, so the mesh's own extent is the honest stand-in. A recorded
    /// divergence, retired with the soldier update.
    /// </para>
    /// </remarks>
    public static Vector3d Stand(HumanSoldierTask soldier, SoldierModelGroup group, IGroundHeightField? terrain, WorldRayCast? cast)
    {
        Vector3d declared = soldier.Position;
        double probeTop = declared.Z + ProbeAboveInUnits;
        double groundZ = double.MinValue;
        bool foundGround = false;

        if (cast is not null
            && cast(
                new Vector3d(declared.X, declared.Y, probeTop),
                new Vector3d(declared.X, declared.Y, declared.Z - ProbeBelowInUnits),
                out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (terrain is not null
            && terrain.TryGetGround(declared.X, declared.Y, probeTop, out TerrainGround ground)
            && ground.Height >= declared.Z - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        if (!foundGround)
        {
            groundZ = declared.Z;
        }

        return new Vector3d(
            declared.X / WorldScene.UnitsPerMetre,
            declared.Y / WorldScene.UnitsPerMetre,
            (groundZ / WorldScene.UnitsPerMetre) - group.Body.LowestVertexZ);
    }

    /// <summary>The collision surface under a point, in world units — placed objects first, terrain second.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="declaredZ">The feet the query starts from; returned unchanged when nothing is under them.</param>
    /// <returns>The highest floor within the probe, or <paramref name="declaredZ"/> if none.</returns>
    /// <remarks>
    /// The same query <see cref="Stand"/> runs, without the mesh lift — for grounding a live soldier's feet
    /// each frame. It chooses the highest support in the probe and can therefore lower a soldier whose
    /// authored spawn is slightly above the terrain, as well as lift one authored just below its floor.
    /// </remarks>
    public double SnapToGround(double x, double y, double declaredZ)
    {
        // One collision cast and one terrain descent per distinct position per FRAME: the shadow
        // cascades and the geometry pass each resolve every soldier's feet from the same positions,
        // and this query was being paid once per pass. Cleared in BeginFrame, so a moved soldier is
        // re-queried the moment the simulation actually moved it.
        (double X, double Y, double Z) key = (x, y, declaredZ);
        if (_groundSnaps.TryGetValue(key, out double cached))
        {
            return cached;
        }

        double groundZ = double.MinValue;
        bool foundGround = false;
        double probeTop = declaredZ + ProbeAboveInUnits;

        if (Cast is not null
            && Cast(new Vector3d(x, y, probeTop), new Vector3d(x, y, declaredZ - ProbeBelowInUnits), out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (Terrain is not null
            && Terrain.TryGetGround(x, y, probeTop, out TerrainGround ground)
            && ground.Height >= declaredZ - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        // Cached whether or not anything was found, because "nothing is under these feet" costs the
        // same two queries to establish as a floor does and is just as true for the rest of the frame.
        double snapped = foundGround ? groundZ : declaredZ;
        _groundSnaps[key] = snapped;
        return snapped;
    }

    /// <summary>The ground query a brain or a corpse stands on: placed collision and terrain, highest wins.</summary>
    /// <returns>The query, or <see langword="null"/> when the level has neither.</returns>
    public Igi1HumanAi.GroundQuery? MakeGroundQuery()
    {
        IGroundHeightField? terrain = Terrain;
        WorldRayCast? cast = Cast;
        if (terrain is null && cast is null)
        {
            return null;
        }

        return (x, y, z, out groundZ) =>
        {
            groundZ = double.MinValue;
            bool found = false;

            if (cast is not null
                && cast(
                    new Vector3d(x, y, z),
                    new Vector3d(x, y, z - ProbeBelowInUnits - ProbeAboveInUnits),
                    out WorldRayHit hit))
            {
                groundZ = hit.Position.Z;
                found = true;
            }

            if (terrain is not null
                && terrain.TryGetGround(x, y, z, out TerrainGround ground)
                && ground.Height >= z - ProbeBelowInUnits - ProbeAboveInUnits
                && ground.Height <= z)
            {
                if (ground.Height > groundZ)
                {
                    groundZ = ground.Height;
                }

                found = true;
            }

            return found;
        };
    }

    /// <summary>The roster group a soldier is drawn in.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The group, or <see langword="null"/> when the soldier is in none.</returns>
    public SoldierModelGroup? FindRoster(SoldierView view)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                if (ReferenceEquals(agent.View, view))
                {
                    return group;
                }
            }
        }

        return null;
    }

    /// <summary>The roster entry a soldier is drawn as: its weapon, its clip and its palette.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The agent, or <see langword="null"/> when the soldier is in no group.</returns>
    public SoldierAgent? FindAgent(SoldierView view)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                if (ReferenceEquals(agent.View, view))
                {
                    return agent;
                }
            }
        }

        return null;
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The task, or <see langword="null"/>.</returns>
    public static HumanAiTask? FindMind(SoldierView view)
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

    /// <summary>The point an area counts a soldier at: its origin, <c>Human+32</c>, not its feet.</summary>
    /// <remarks>
    /// <para>
    /// The human criteria callbacks an <c>AreaActivate</c> runs (<c>0x460A10</c> for <c>CRITERIA_HUMAN%d</c>,
    /// <c>0x460A70</c> for <c>CRITERIA_HUMAN</c>) hand the box test <c>0x5042B0</c> each registered human's
    /// <c>+32</c>. For a soldier that is the ground its AI stood it on plus the clip's root height (<c>0x462390</c>
    /// adds <c>Human+1300</c>, <see cref="SoldierView.RootSample"/>): about 0.95 m above the feet standing.
    /// </para>
    /// <para>
    /// Counting the feet dropped mission 4's running squad out of "Team one stop 1" (<c>AreaActivate_150</c>),
    /// whose floor is 0.25 m above the ground they cross. Retail's squad fills it at tick 3818 and opens the
    /// med-warehouse guards (<c>ConditionalContainer_302</c>) on them; the port's never did, so the squad reached
    /// its stops unopposed where retail's is fighting by tick 4548.
    /// </para>
    /// </remarks>
    public static Vector3d OccupantOrigin(SoldierView view) =>
        new(view.Position.X, view.Position.Y, view.Position.Z + view.RootSample.Z);

    /// <summary>Where a soldier's view stands, which its gun fires from (<c>0x462E50</c> reads the view's <c>+224</c>).</summary>
    /// <remarks>
    /// 0x464BD0 places the view at 0x465820's point -- the origin (<c>Human+32</c>) raised 0.7 m, or 0.35 m with the
    /// crouch byte set, as it is in the firing state -- plus offsets a soldier's view leaves at zero.
    /// </remarks>
    public static Vector3d ViewPosition(SoldierView view, Igi1HumanAi brain)
    {
        // The brain has already moved this tick; the view is placed after the fire.
        Vector3d feet = brain.Position;
        double origin = feet.Z + view.RootSample.Z;
        return new Vector3d(feet.X, feet.Y, origin + (brain.Body.CrouchByte ? 1433.6 : 2867.24));
    }

    /// <summary>Gets a soldier's damage state, creating it on the first hit.</summary>
    public HumanHealth HealthOf(SoldierView soldier)
    {
        if (!Health.TryGetValue(soldier, out HumanHealth? health))
        {
            health = NewHealth(soldier);
            Health[soldier] = health;
        }

        return health;
    }

    /// <summary>
    /// A fresh damage state — <c>0x489C30</c> over the class's hit-zone table, with the settings row's
    /// maximum health once <c>0x406F70</c> has configured the soldier.
    /// </summary>
    public HumanHealth NewHealth(SoldierView soldier) => new(
        soldier.Soldier.Class.HitZones,
        Combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.MaximumHealth : HumanHitZoneTable.MaximumHealth);
}
