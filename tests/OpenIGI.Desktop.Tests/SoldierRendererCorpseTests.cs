using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers a shot soldier's lifetime in the renderer: the corpse stays drawable where it fell, and a
/// generated guard's body is replaced, armed, once it has gone.
/// </summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierRendererCorpseTests
{
    [RequiresGameDataFact]
    public void LethalDragunovHitKeepsTheLevelFourCorpseDrawable()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => view.Soldier.Team != scenario.Renderer.Simulation.PlayerTeam
                && !CinematicActor.IsStaged(view.Soldier));
        SoldierMember targetMember = MemberFor(scenario.Renderer, ray.View);
        MeshHandle bodyMesh = targetMember.Mesh;
        KeepOnlyTarget(scenario.Renderer, targetMember);

        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !ray.View.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, ray.View));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }

        Assert.True(ray.View.IsDead);
        Assert.Contains(
            scenario.Renderer.Simulation.Occupants(),
            // An area counts a human at its origin (Human+32), straight above where it stands.
            occupant => occupant.IsDead
                && occupant.Position.X == ray.View.Position.X
                && occupant.Position.Y == ray.View.Position.Y);

        for (int tick = 0; tick < 180; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        scenario.Renderer.Draw(
            new Camera3D(ray.Origin, Camera3D.LookRotation(0f, 0f)),
            Matrix4f.Identity);

        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == bodyMesh);
    }

    [RequiresGameDataTheory]
    [InlineData(417)]
    [InlineData(418)]
    [InlineData(419)]
    public void DragunovKillKeepsAnActivatedBehindWarehouseGuardAboveItsFloor(int taskId)
    {
        using RagdollScope ragdoll = new(enabled: false);
        using Scenario scenario = CreateScenario(prepareContainers: true);
        SoldierView target = Assert.Single(
            RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Id == taskId);
        Assert.False(ConditionalContainerGate.IsTaskActive(target.Soldier));

        Assert.True(scenario.Loop.Variables.TryWrite(
            "EditVariable_752.nValue",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(scenario.Loop.Variables.TryWrite(
            "EditVariable_4000.nValue",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(scenario.Loop.Variables.TryRead(
            "EditVariable_752.nValue",
            out OpenIGI.Scripting.Runtime.QvmValue activation));
        Assert.Equal(1, activation.ToInt32());
        WriteInt(scenario, "ConditionalContainer_3900.isRun", 0);
        WriteInt(scenario, "ConditionalContainer_3901.isRun", 1);
        WriteInt(scenario, "ConditionalContainer_3902.isRun", 0);
        scenario.Cutscenes!.Tick();
        ConditionalContainerTask behindWarehouse = Assert.IsType<ConditionalContainerTask>(target.Soldier.Parent);
        Assert.True(behindWarehouse.IsRun, $"Container {behindWarehouse.Id} did not run: {behindWarehouse.Condition}");
        Assert.True(
            ConditionalContainerGate.IsTaskActive(target.Soldier),
            DescribeAncestry(target.Soldier));

        SoldierMember targetMember = MemberFor(scenario.Renderer, target);
        Assert.Equal("WEAPON_ID_AK47", Assert.IsType<WeaponType>(targetMember.Agent.WeaponType).ScriptId);
        Assert.NotNull(targetMember.Weapon);

        scenario.Renderer.Tick(enemyAlive: false);
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        Assert.True(targetMember.HasHandState);
        Assert.True(targetMember.HasWeaponFrameState);

        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => ReferenceEquals(view, target));
        MeshHandle bodyMesh = targetMember.Mesh;
        Vector3d spawnMetres = target.Position / WorldScene.UnitsPerMetre;

        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !target.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, target));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));

            scenario.Cutscenes.Tick();
            scenario.Renderer.Tick(enemy: ray.Origin, enemyAlive: true);
            scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
            Assert.True(ConditionalContainerGate.IsTaskActive(target.Soldier));
            AssertBodyRemainsDrawableNearSpawn(scenario, targetMember, bodyMesh, spawnMetres, shot + 1);
        }

        Assert.True(target.IsDead);
        for (int tick = 0; tick < 180; tick++)
        {
            scenario.Cutscenes.Tick();
            scenario.Renderer.Tick(enemyAlive: false);
        }

        Assert.True(ConditionalContainerGate.IsTaskActive(target.Soldier));
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        AssertBodyRemainsDrawableNearSpawn(scenario, targetMember, bodyMesh, spawnMetres, maxHitsToKill + 180);
    }

    [RequiresGameDataTheory]
    [InlineData(429)]
    [InlineData(430)]
    [InlineData(431)]
    public void PostRescueGeneratedGuardIsReplacedArmedOnceItsBodyHasGone(int taskId)
    {
        using RagdollScope ragdoll = new(enabled: false);
        using Scenario scenario = CreateScenario(prepareContainers: true);
        SoldierView firstLife = Assert.Single(
            RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Id == taskId);
        Assert.False(ConditionalContainerGate.IsTaskActive(firstLife.Soldier));

        scenario.Cutscenes!.Tick();
        Assert.True(scenario.Loop.Variables.TryWrite(
            "StatusMessage_977.isSendt",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        ForceContainer(scenario, 307, run: true);
        ForceContainer(scenario, 3901, run: true);
        scenario.Renderer.Tick(enemyAlive: false);
        Assert.True(
            ConditionalContainerGate.IsTaskActive(firstLife.Soldier),
            DescribeAncestry(firstLife.Soldier));

        SoldierMember firstMember = MemberFor(scenario.Renderer, firstLife);
        Assert.Equal("WEAPON_ID_AK47", Assert.IsType<WeaponType>(firstMember.Agent.WeaponType).ScriptId);
        Assert.NotNull(firstMember.Weapon);

        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => ReferenceEquals(view, firstLife));
        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !firstLife.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, firstLife));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }
        Assert.True(firstLife.IsDead);
        MeshHandle corpseMesh = firstMember.Mesh;

        // Retail GuardGenerator replaces its guard only once he is dead AND out of the world, and deletes the
        // body as it clones (0x487EE0). His own HumanAI takes him out (0x44F410): it waits for the death clip
        // (Human+828), counts AI+15680 = 300 down past nought, and unregisters him because his parent is a
        // GuardGenerator. So with the clip ending on tick E: body gone on E+302, replacement on E+303.
        List<int> soundEmitters = [];
        scenario.Renderer.Simulation.SetWeaponSound((id, _, _, _) => soundEmitters.Add(id));
        int soldiersAlive = scenario.Renderer.Count;
        int clipEnded = -1;
        int bodyGone = -1;
        SoldierView? replacement = null;
        int replacedAt = -1;
        for (int tick = 0; tick < 2000 && replacement is null; tick++)
        {
            soundEmitters.Clear();
            scenario.Renderer.Tick(enemyAlive: false);
            if (clipEnded < 0 && firstLife.ClipEndedThisTick)
            {
                clipEnded = tick;
            }

            if (bodyGone < 0 && scenario.Renderer.Count < soldiersAlive)
            {
                bodyGone = tick;
            }

            replacement = RendererSoldiers(scenario.Renderer)
                .FirstOrDefault(view => view.Soldier.Id == taskId && !view.IsDead);
            if (replacement is not null)
            {
                replacedAt = tick;
            }
        }

        Assert.True(clipEnded >= 0, "the death clip never ended");
        Assert.Equal(clipEnded + SoldierSimulation.GeneratedBodyLingerTicks + 2, bodyGone);
        Assert.NotNull(replacement);
        Assert.Equal(clipEnded + SoldierSimulation.GeneratedBodyLingerTicks + 3, replacedAt);
        AssertDistinctRuntimeLifetimes(scenario.Renderer, soundEmitters);

        // One live instance under the authored id, the old one gone: its brain and its body.
        Assert.Single(RendererSoldiers(scenario.Renderer), view => view.Soldier.Id == taskId);
        Assert.Equal(soldiersAlive, scenario.Renderer.Count);

        SoldierMember replacementMember = MemberFor(scenario.Renderer, replacement);
        Assert.Equal("WEAPON_ID_AK47", Assert.IsType<WeaponType>(replacementMember.Agent.WeaponType).ScriptId);
        SoldierHeldWeapon? replacementWeaponValue = replacementMember.Weapon;
        Assert.NotNull(replacementWeaponValue);
        MeshHandle replacementWeaponMesh = replacementWeaponValue.Mesh;

        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        Assert.True(replacementMember.HasHandState);
        Assert.True(replacementMember.HasWeaponFrameState);
        Assert.Contains(scenario.Renderer.Simulation.MapContacts(), position => position == replacement.Position);
        Camera3D drawCamera = new(ray.Origin, Camera3D.LookRotation(0f, 0f));
        Matrix4f replacementWeaponTransform = drawCamera.CreateModelView(
            replacementMember.DrawnWeaponOrigin,
            replacementMember.DrawnWeaponRotation);
        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(
            drawCamera,
            Matrix4f.Identity);
        Assert.DoesNotContain(scenario.Device.MeshDraws, draw => draw.Mesh == corpseMesh);
        Assert.Contains(
            scenario.Device.MeshDraws,
            draw => draw.Mesh == replacementWeaponMesh && draw.ModelView == replacementWeaponTransform);

        // The replacement is a real life of its own: shot, it dies where it stands, and the cap still counts.
        for (int shot = 0; shot < maxHitsToKill && !replacement.IsDead; shot++)
        {
            MeshHitRay replacementRay = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, replacement));
            Assert.True(scenario.Renderer.TryShoot(
                replacementRay.Origin,
                replacementRay.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }
        Assert.True(replacement.IsDead);
    }

    private static void AssertBodyRemainsDrawableNearSpawn(
        Scenario scenario,
        SoldierMember targetMember,
        MeshHandle bodyMesh,
        Vector3d spawnMetres,
        int tick)
    {
        Vector3d drawn = targetMember.DrawnWhere;
        double dx = spawnMetres.X - drawn.X;
        double dy = spawnMetres.Y - drawn.Y;
        double dz = spawnMetres.Z - drawn.Z;
        double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.True(distance < 20.0, $"tick {tick}: guard moved {distance:0.000}m from {spawnMetres} to {drawn}");

        (Vertex3D[] vertices, ushort[] indices) = scenario.Device.Meshes[bodyMesh.Id];
        float maximumLocalZ = float.MinValue;
        Assert.All(indices.Distinct(), index =>
        {
            Vertex3D vertex = vertices[index];
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.True(float.IsFinite(vertex.Position.Y));
            Assert.True(float.IsFinite(vertex.Position.Z));
            Assert.InRange(vertex.Position.X, -20f, 20f);
            Assert.InRange(vertex.Position.Y, -20f, 20f);
            Assert.InRange(vertex.Position.Z, -20f, 20f);
            maximumLocalZ = Math.Max(maximumLocalZ, vertex.Position.Z);
        });
        Assert.True(
            drawn.Z + maximumLocalZ > spawnMetres.Z,
            $"tick {tick}: corpse is entirely below its authored floor; root={drawn.Z:0.000}, localMax={maximumLocalZ:0.000}, floor={spawnMetres.Z:0.000}");

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(
            new Camera3D(spawnMetres + new Vector3d(0.0, -10.0, 2.0), Camera3D.LookRotation(0f, 0f)),
            Matrix4f.Identity);
        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == bodyMesh);
    }

    private static void AssertDistinctRuntimeLifetimes(SoldierRenderer renderer, List<int> soundEmitters)
    {
        Assert.NotEmpty(soundEmitters);
        Assert.Equal(soundEmitters.Count, soundEmitters.Distinct().Count());
        Dictionary<SoldierView, Igi1HumanAi> brains = RendererBrains(renderer);
        Assert.Equal(brains.Count, brains.Values.Select(brain => brain.RuntimeId).Distinct().Count());
        Dictionary<int, Igi1HumanAi> recipients = renderer.Simulation.BrainsByRuntimeId;
        foreach (Igi1HumanAi brain in brains.Values)
        {
            Assert.Same(brain, recipients[brain.RuntimeId]);
        }
    }

    private sealed class RagdollScope : IDisposable
    {
        private readonly bool _previous = GameplayCVars.Ragdoll.Value;

        public RagdollScope(bool enabled) => GameplayCVars.Ragdoll.Value = enabled;

        public void Dispose() => GameplayCVars.Ragdoll.Value = _previous;
    }
}
