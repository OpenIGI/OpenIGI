using System.Globalization;
using System.Text;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>Mission 14's sentry guns: AIStationaryGunHolder 2500..2504 and the AIStationaryGun each carries.</summary>
/// <remarks>
/// They watch while EditVariable_1451.nValue is 2 and are on the guards' side (team 1) until Terminal_301 is hacked, when
/// their team expression goes to 0 and the guards become their enemies (0x46E5B0, 0x489750, 0x46F390). The Generator Room's
/// guards 415..418 stand in their cones.
/// </remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class MissionFourteenGunHolderTests : IDisposable
{
    private static readonly int[] RoomGuns = [2500, 2501, 2502, 2503, 2504];
    private static readonly int[] RoomGuards = [415, 416, 417, 418];

    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataFact]
    public void The_room_guns_watch_on_their_variable_and_kill_the_guards_once_the_terminal_is_hacked()
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(device, new HeadlessGameWindow(), "-mission", "14", "-skipcutscene", "-lockstep", "-frames", "100000000");
        host.Start();
        StringBuilder trace = new();
        try
        {
            var mission = host.Mission();
            CutsceneRuntime runtime = mission.Cutscenes!;
            TaskTree tree = mission.LevelRoot!.Tree;
            GameTask[] holders = [.. RoomGuns.Select(id => tree.FromId(id)!)];

            // Each is armed from its holder model's AIStationaryGun, on the guards' side and off.
            Step(host, 300);
            foreach (GameTask holder in holders)
            {
                Assert.NotNull(runtime.Vehicles.GunTurret(holder, 0));
                Assert.Equal((1, false), runtime.Vehicles.GunHolderState(holder));
            }

            // Each is an object of its own (0x46E7F0): the base its "Holder model" names, and under it the dome that is the
            // gun's mount, an attachment of that base. Without the base there was no dome, and the weapon hung in the air.
            WorldScene scene = host.Scene()!;
            foreach (GameTask holder in holders)
            {
                WorldPlacement[] own = [.. scene.Placements.Where(placement => ReferenceEquals(placement.Owner, holder))];
                Assert.Contains(own, placement => placement.Instance.Name == "456_11_1");
                WorldPlacement dome = Assert.Single(own, placement => placement.Instance.Name == "456_12_1");
                Assert.True(
                    runtime.TryGetPlacementDrawTransform(dome, out _, out _),
                    $"holder {holder.Id}'s dome is not drawn on its gun's mount");
            }

            // On, still the guards' side: the guards are friends, the player (far off) its only enemy, and nothing fires.
            Assert.True(host.Loop().Variables.TryWrite("EditVariable_1451.nValue", QvmValue.FromInt32(2)));
            for (int tick = 0; tick < 30 * 3; tick++)
            {
                Step(host, 1);
                foreach (GameTask holder in holders)
                {
                    Assert.Equal((1, true), runtime.Vehicles.GunHolderState(holder));
                    Assert.False(runtime.Vehicles.GunTurret(holder, 0)!.Firing, $"holder {holder.Id} fired while on the guards' side");
                    Assert.All(runtime.Vehicles.GunHolderRecord(holder)!.Enemies, enemy => Assert.IsType<PlayerCamera>(enemy.Key));
                }
            }

            // Terminal_301.isHacked is bound read-only off the interaction: hack it as its completion does.
            mission.Interactions!.Items.Single(item => item.Task.Id == 301).CompleteForTesting();
            int fired = 0;
            HashSet<GameTask> domeTurned = new(ReferenceEqualityComparer.Instance);
            for (int tick = 0; tick < 30 * 20; tick++)
            {
                Step(host, 1);
                foreach (GameTask holder in holders)
                {
                    Assert.Equal(0, runtime.Vehicles.GunHolderState(holder)!.Value.Team);
                    VehicleGunTurret turret = runtime.Vehicles.GunTurret(holder, 0)!;
                    fired += turret.Firing ? 1 : 0;
                    // The dome turns with the gun: its draw pose is the base's turned by the gun's traverse (0x46FBA0).
                    if (turret.Yaw != 0f && !domeTurned.Contains(holder))
                    {
                        WorldPlacement dome = scene.Placements.Single(placement =>
                            ReferenceEquals(placement.Owner, holder) && placement.Instance.Name == "456_12_1");
                        Assert.True(runtime.TryGetPlacementDrawTransform(dome, out _, out OpenIGI.Core.Numerics.Matrix3f turned));
                        Assert.NotEqual(dome.Orientation, turned);
                        domeTurned.Add(holder);
                    }

                    if (tick % 30 == 0)
                    {
                        trace.AppendLine(CultureInfo.InvariantCulture,
                            $"{tick} holder {holder.Id}: seen [{string.Join(", ", runtime.Vehicles.GunHolderRecord(holder)!.Enemies.Where(e => e.Seen).Select(e => Describe(e.Key)))}] yaw {turret.Yaw:0.00} pitch {turret.Pitch:0.00} firing {turret.Firing}");
                    }
                }
            }

            string guards = string.Join(", ", mission.Soldiers!.SoldierViews
                .Where(view => RoomGuards.Contains(view.Soldier.Id))
                .Select(view => $"{view.Soldier.Name} dead {view.IsDead}"));
            trace.AppendLine(guards);
            Assert.True(fired > 0, $"No hacked gun fired on a guard:\n{trace}");
            Assert.NotEmpty(domeTurned);
            Assert.All(
                mission.Soldiers.SoldierViews.Where(view => RoomGuards.Contains(view.Soldier.Id)),
                view => Assert.True(view.IsDead, $"{view.Soldier.Name} survived the hacked guns:\n{trace}"));
        }
        finally
        {
            host.Stop();
        }
    }

    private static string Describe(object key) => key switch
    {
        SoldierView view => view.Soldier.Name,
        _ => key.GetType().Name,
    };

    private static void Step(GameHost host, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            Assert.True(host.Step());
        }
    }
}
