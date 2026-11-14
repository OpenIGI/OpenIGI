using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>The soldiers of one <c>(model, skeleton, stand animation)</c> triple, as <see cref="SoldierSimulation.Load"/> stood them.</summary>
/// <param name="Roster">The group, its agents in the order they were stood.</param>
/// <param name="Model">The model's name, as the soldiers' tasks spell it.</param>
/// <param name="Posed">The first soldier's view, whose stand pose the group's body was measured in.</param>
/// <param name="Geometry">The model's unskinned geometry, for whatever draws the group.</param>
/// <param name="StandSkin">The geometry skinned in <paramref name="Posed"/>'s unrotated stand pose, for whatever draws the group.</param>
public sealed record SoldierLoadedGroup(SoldierModelGroup Roster, string Model, SoldierView Posed, MeshGeometry Geometry, Vertex3D[] StandSkin);

public sealed partial class SoldierSimulation
{
    /// <summary>Stands a level's soldiers: each the model, skeleton and stand animation its own task declares.</summary>
    /// <param name="mission">The packs the bodies and their animation sets are read from.</param>
    /// <param name="levelRoot">The level's tree, whose <see cref="HumanSoldierTask"/>s are stood.</param>
    /// <param name="terrain">The ground a body is set down on.</param>
    /// <param name="cast">The placed collision a body is set down on.</param>
    /// <returns>The groups, in the order their first soldier was met; each soldier's agent in its group's roster.</returns>
    /// <remarks>
    /// <para>
    /// The spawn is <c>0x456E50</c> + <c>0x45F890</c>: the bone object on the declared <c>Bone Heirachy</c> skeleton, the
    /// declared <c>Stand Animation</c> (or 2) on bone 0, the ordinary <c>Model</c> parameter resolved by name, and the facing a
    /// pure <c>Z(Gamma)</c>. A soldier another game drives (<see cref="HumanTask.Driver"/>) is that game's, and is left.
    /// </para>
    /// <para>
    /// Whatever the simulation held of an earlier level is forgotten first (<see cref="Clear"/>). What draws the soldiers makes
    /// its meshes from the groups returned; the simulation runs them whether anything draws them or not.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SoldierLoadedGroup> Load(LoadedMission mission, GameTask levelRoot, IGroundHeightField? terrain = null, WorldRayCast? cast = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);

        Clear();
        Terrain = terrain;
        Cast = cast;

        List<SoldierLoadedGroup> loaded = [];
        Dictionary<(string Model, int Skeleton, int Stand), SoldierLoadedGroup> groups = [];
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
                    SoldierContext.LogCategory,
                    $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, which no pack loaded a set for.");
                continue;
            }

            SoldierView view = new(soldier, set);
            if (soldier.StandAnimation >= 0 && !view.DeclaredStandAnimationFound)
            {
                // The original's own wording (0x456E50).
                _logger.Warning(SoldierContext.LogCategory, $"Animation #{soldier.StandAnimation} not found");
            }

            (string, int, int) key = (soldier.Model, soldier.BoneHierarchy, view.StandAnimation);
            if (!groups.TryGetValue(key, out SoldierLoadedGroup? group))
            {
                if (!SoldierBodyModel.TryPose(mission, soldier.Model, view, _logger, out SoldierBodyModel? body, out MeshGeometry? geometry, out Vertex3D[]? skin))
                {
                    continue;
                }

                group = new SoldierLoadedGroup(new SoldierModelGroup(body), soldier.Model, view, geometry, skin);
                groups[key] = group;
                loaded.Add(group);
                Roster.Add(group.Roster);
            }

            // A human staged beside a CutScene keeps the authored object origin. Gameplay humans are grounded by their mover on
            // the first ticks, but cinematic performers deliberately sit, crouch or ride at offsets from nearby geometry: running
            // the gameplay stand probe over Anya, for example, put the lowest vertex of her seated clip on the control-room floor
            // and lifted the entire actor through the camera framing. The fallback standing clip's root height is not added either:
            // the task authors the cinematic pelvis origin, and the real seated clip is selected only after the timeline starts.
            bool cutsceneActor = CinematicActor.IsStaged(soldier);
            Vector3d placement = cutsceneActor
                ? soldier.Position / WorldScene.UnitsPerMetre
                : Stand(soldier, group.Roster, terrain, cast);
            group.Roster.Add(new SoldierAgent(view, placement) { IsCutsceneActor = cutsceneActor });
            if (group.Roster.Body.Model is { NativeMarkerBound: > 0f } model && float.IsFinite(model.NativeMarkerBound))
            {
                view.DetectorMarkerScaleMetres = model.NativeMarkerBound / (float)WorldScene.UnitsPerMetre;
                _context.Keep(view);
            }
        }

        _logger.Information(
            SoldierContext.LogCategory,
            $"{loaded.Sum(static group => group.Roster.Agents.Count)} soldier(s) over {loaded.Count} distinct (model, skeleton, animation) triple(s).");
        return loaded;
    }

    /// <summary>Forgets the soldiers of the level the simulation last ran, and what it saw and cast against there.</summary>
    public void Clear()
    {
        Roster.Clear();
        _context.ForgetHealth();
        _context.ForgetMarkerScales();
        BinocularDetected.Clear();
        BinocularOverlay.Clear();
        BinocularVisibility.Clear();
        BinocularVisibilityTick = int.MinValue;
        Cast = null;
        Sight = null;
        ForgetRagdollRigs();
        RagdollWorld = null;
    }
}
