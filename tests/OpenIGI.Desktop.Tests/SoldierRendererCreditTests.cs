using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The player's rounds credit what they do to a soldier on the tally they carry, for the remastered crosshair's marks, and the
/// crosshair finds an enemy under it as a round would.
/// </summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierRendererCreditTests
{
    [RequiresGameDataFact]
    public void The_players_rounds_count_their_hits_and_the_kill_on_his_tally()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => view.Soldier.Team != scenario.Renderer.Simulation.PlayerTeam && !CinematicActor.IsStaged(view.Soldier));
        KeepOnlyTarget(scenario.Renderer, MemberFor(scenario.Renderer, ray.View));

        // The crosshair looking along the ray finds him; looking straight up from the same eye finds nobody.
        Vector3d along = ray.End - ray.Origin;
        along /= along.Length;
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        Assert.True(soldiers.EnemyUnderAim(ray.Origin, new Vector3f((float)along.X, (float)along.Y, (float)along.Z)));
        Assert.False(soldiers.EnemyUnderAim(ray.Origin, new Vector3f(0f, 0f, 1f)));

        ShotTally tally = new();
        int shots = 0;
        while (!ray.View.IsDead && shots < 64)
        {
            ray = FindAnyMeshHitRay(scenario.Renderer, view => ReferenceEquals(view, ray.View));
            Vector3d origin = ray.Origin, end = ray.End;
            Assert.True(LevelBodies.Shoot(
                [scenario.Renderer],
                origin,
                end,
                soldiers.PlayerTask,
                double.PositiveInfinity,
                alreadyHeard: false,
                struck => new Igi1WeaponHit(struck, origin, end, 0.25f, WeaponCategory.Gun, Shooter: null, FromPlayer: true, tally).ToShared(),
                out _));
            shots++;
        }

        Assert.True(ray.View.IsDead);
        Assert.Equal(shots, tally.Hits);
        Assert.Equal(1, tally.Kills);
        Assert.False(soldiers.EnemyUnderAim(ray.Origin, new Vector3f((float)along.X, (float)along.Y, (float)along.Z)));
    }
}
