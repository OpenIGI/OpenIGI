using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Host;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>Mission 14 ends with Anya defusing the bomb herself, once Ekk is dead (issue #42).</summary>
/// <remarks>
/// Ekk's death starts Anya's container (ConditionalContainer_1150). Her AI script (ai/557) patrols route 638 while
/// EditVariable_1556 is 0; she reaches the bomb, AreaActivate_1811 (CRITERIA_HUMAN0) counts her, the counter goes to 1, she
/// plays the defuse animation and LevelTimer_1415 runs to "Anya did it" and the mission's end. After Ekk dies the player only
/// guards her: the completion solver, which stands in the area itself, cannot tell whether she gets there. She runs her
/// route: a female soldier's run is her state 1, clip 3, entered with the walk byte clear. With the walk and run clips the
/// wrong way round she walked it, too slowly to reach the bomb before turning back.
/// </remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class MissionFourteenAnyaTests : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataFact]
    public void Once_ekk_is_dead_anya_walks_to_the_bomb_and_defuses_it()
    {
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.InfiniteHealth.Override(true);
        try
        {
            using RecordingGraphicsDevice device = new() { IsRecording = false };
            GameHost host = _support.Host(device, new HeadlessGameWindow(), "-mission", "14", "-skipcutscene", "-lockstep", "-frames", "100000000");
            host.Start();
            MissionSolver solver = MissionSolver.Over(host, 14);
            Assert.True(solver.RunUntil("HumanSoldierFemale_454.isDead", 30 * 60 * 10), $"Ekk never died:\n{solver.Log}");

            var loop = host.Loop();
            TaskTree tree = host.Mission().LevelRoot!.Tree;
            Assert.True(tree.FromId(1811)!.TryGetScriptValue("Position", out Vector3d bomb));
            StringBuilder trace = new();
            bool counted = false;
            // The player guards her while she works ("Anya says. Guard me!"): once a second, a hostile within 40 m of her is hit
            // by the same rocket blast the solver uses, never one close enough for the blast to reach her.
            int playerTeam = host.Mission().Soldiers!.PlayerTeam;
            solver.Idle(30 * 60 * 5, () =>
            {
                if (loop.Clock.TickCount % 30 == 0
                    && host.Mission().Soldiers!.SoldierViews.FirstOrDefault(v => v.Soldier.Id == 456) is { IsDead: false } guarded)
                {
                    foreach (var hostile in host.Mission().Soldiers!.SoldierViews)
                    {
                        double metres = (hostile.Position - guarded.Position).Length / 4096.0;
                        if (!hostile.IsDead && hostile.Soldier.Team != playerTeam && metres is > 6.0 and < 40.0
                            && OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(hostile.Soldier))
                        {
                            trace.AppendLine(CultureInfo.InvariantCulture, $"{loop.Clock.TickCount}: guarding against {hostile.Soldier.Name}, {metres:0.0} m from her");
                            host.Gameplay().Blasts.Explode(
                                hostile.Position + new Vector3d(0, 0, 2048),
                                OpenIGI.Game.Weapons.MissileExplosion.RadiusUnits,
                                OpenIGI.Game.Weapons.MissileExplosion.FalloffUnits,
                                OpenIGI.Game.Weapons.MissileExplosion.DamageFactor,
                                OpenIGI.Game.Weapons.WeaponCategory.Grenade,
                                sourceWeaponId: OpenIGI.Game.World.DestructibleObjectRegistry.CarDestroyingWeaponId);
                        }
                    }
                }

                counted |= loop.Variables.TryRead("EditVariable_1556.nValue", out QvmValue value) && value.ToInt32() >= 1;
                if (loop.Clock.TickCount % 300 == 0)
                {
                    foreach (var anya in host.Mission().Soldiers!.SoldierViews.Where(v => v.Soldier.Id == 456))
                    {
                        trace.AppendLine(CultureInfo.InvariantCulture, $"{loop.Clock.TickCount}: anya {(anya.Position - bomb).Length / 4096.0:0.0} m from the bomb, dead {anya.IsDead}");
                    }
                }
            });
            foreach (string name in new[] { "EditVariable_1556.nValue", "LevelTimer_1415.nTick", "LevelTimer_1414.nTick", "StatusMessage_2100.isSendt", "StatusMessage_2105.isSendt", "StatusMessage_2103.isSendt", "StatusMessage_2104.isSendt" })
            {
                trace.AppendLine(CultureInfo.InvariantCulture, $"{name} = {(loop.Variables.TryRead(name, out QvmValue v) ? v.ToDouble() : double.NaN)}");
            }

            host.Stop();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "openigi-m14-anya.txt"), solver.Log + trace);

            Assert.True(counted, $"Anya never reached the bomb:\n{trace}");
            Assert.True(solver.Outcome == true, $"The mission did not complete:\n{trace}");
        }
        finally
        {
            PlayerCheatCVars.InfiniteHealth.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }
    }
}
