using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldiers, the retail nav graph and the draining clip the combat-wiring tests drive a real
/// <see cref="Igi1HumanAi"/> with.
/// </summary>
/// <remarks>
/// xUnit builds a fresh test-class instance for every test, so each test still starts from its own full
/// clip, exactly as it did when these lived in the one class.
/// </remarks>
public abstract class AiSoldierCombatWiringFixture
{
    private protected const double Metre = 4096.0;

    // The clip the fixture's weapon query reports. The desktop drains it when a round comes out, and the
    // AI counts its burst by watching it CHANGE -- a query that never moved would hold the trigger for
    // ever and never leave the firing phase.
    private protected int _clip = 30;

    // What the desktop's firing path does: a round out of the clip, and the clip back when it reloads.
    private protected void Spend(Igi1HumanAi soldier)
    {
        if (soldier.Combat.PullTrigger && _clip > 0)
        {
            _clip--;
        }
        else if (soldier.Combat.PressReload || soldier.Combat.ReloadWeaponDirectly)
        {
            _clip = 30;
        }
    }

    private protected Igi1HumanAi Soldier(out Vector3d position)
    {
        GraphFile graph = Graph();
        position = NodePosition(graph);

        Igi1HumanAi soldier = new(graph, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUARD_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                RandomClipFraction = 0.2f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(_clip, 30, 30, WeaponCategory.Gun));

        return soldier;
    }

    private protected static Igi1HumanAi PatrolOneShotSoldier(Vector3d position, float grenadeProbability)
    {
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                TrackingDistanceMetres = 100f,
                // A soldier with rounds in its clip draws against the "when reload" probability (+15756).
                GrenadeThrowProbability = grenadeProbability,
                GrenadeThrowWhenReloadingProbability = grenadeProbability,
                GrenadeMinimumDistanceMetres = 1f,
                GrenadeMaximumDistanceMetres = 20f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));
        return soldier;
    }

    // Level 1's own nav graph, so the soldier is standing somewhere the game actually puts one.
    private protected static GraphFile Graph()
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");

        return GraphFile.Read(File.ReadAllBytes(path));
    }

    private protected static Vector3d NodePosition(GraphFile graph) => graph.Nodes[0].Position;
}
