using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What the player and the soldiers use, climb, open, ride and blow up.</summary>
internal static class Igi2InteractionSystems
{
    /// <summary>Fences hum while live with an ambient area of their own, are told where the player is, and are climbed while dead.</summary>
    public static void Fences(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Audio.Igi2CameraListener? listener = b.Listener;
        foreach (Igi2FenceTask fence in b.Root.Descendants().OfType<Igi2FenceTask>())
        {
            fence.EquipHum = hum =>
            {
                hum.Audio = player.Sounds;
                hum.Listener = listener;
                hum.Random = player.Human.Random;
            };
            fence.Prepare();
            b.Activatables.Add(fence);
            b.Notified.Add(fence);
        }
    }

    /// <summary><c>Human_Notify</c>'s list, which every human tells its place to; the mines laid while the level runs join it.</summary>
    public static void Notify(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        player.Notified = b.Notified;
        foreach (Igi2Human human in b.LevelHumans())
        {
            human.Notified = b.Notified.Items;
        }

        Igi2LevelMines mines = new(player, player.CollisionWorld, b.Models, b.Notified);
        player.Mines = mines;
        if (player.Weapons is { } layer)
        {
            layer.Firing.Mines = mines;
        }
    }

    /// <summary>Cabinets are searched from their first magic vertex, their drawers sliding on the search clip's triggers.</summary>
    public static void Cabinets(Igi2LevelBuild b)
    {
        foreach (Igi2CabinetTask cabinet in b.Root.Descendants().OfType<Igi2CabinetTask>())
        {
            cabinet.Prepare(b.Magic);
            b.Activatables.Add(cabinet);
        }
    }

    /// <summary>Wires are strung between their ends and climbed or ridden down.</summary>
    public static void Wires(Igi2LevelBuild b)
    {
        foreach (Igi2WireTask wire in b.Root.Descendants().OfType<Igi2WireTask>())
        {
            wire.Prepare();
            b.Activatables.Add(wire);
        }
    }

    /// <summary>
    /// Ladders read their steps off their models' magic vertices, and look down their lines of sight before they are taken; and the
    /// ladders the buildings' attach points make (<c>Ladder_CreateHandler</c> with a magic object's context).
    /// </summary>
    public static void Ladders(Igi2LevelBuild b)
    {
        foreach (Igi2LadderTask ladder in b.Root.Descendants().OfType<Igi2LadderTask>())
        {
            ladder.Sight = b.Sight;
            ladder.Prepare();
            b.Activatables.Add(ladder);
        }

        if (b.Magic is { } magicTypes)
        {
            Igi2WorldScene ladderScene = Igi2WorldScene.Build(b.Root, b.Models);
            foreach (Igi2MagicPlacement point in Igi2CollisionWorld.MagicObjects(ladderScene, b.Models, magicTypes))
            {
                if (point.Type.TaskType == Igi2LadderTask.TypeName)
                {
                    Igi2LadderTask ladder = Igi2LadderTask.FromAttachPoint(point, b.Models);
                    ladder.Sight = b.Sight;
                    b.Activatables.Add(ladder);
                }
            }
        }
    }

    /// <summary>Switches, pressed by the player.</summary>
    public static void Switches(Igi2LevelBuild b)
    {
        foreach (Igi2SwitchTask button in b.Root.Descendants().OfType<Igi2SwitchTask>())
        {
            b.Activatables.Add(button);
        }
    }

    /// <summary>Lifts carry what stands on them.</summary>
    public static void Lifts(Igi2LevelBuild b)
    {
        foreach (Igi2LiftTask lift in b.Root.Descendants().OfType<Igi2LiftTask>())
        {
            lift.Bind(b.Player.CollisionWorld.SolidsOf(Igi2LiftTask.TypeName, lift.Id));
            b.Movers.Add(lift);
        }
    }

    /// <summary>Doors swing or slide, and carry what stands in them.</summary>
    public static void Doors(Igi2LevelBuild b)
    {
        foreach (Igi2DoorTask door in b.Root.Descendants().OfType<Igi2DoorTask>())
        {
            door.Bind(b.Player.CollisionWorld.SolidsOf(Igi2DoorTask.TypeName, door.Id));
            b.Activatables.Add(door);
            b.Movers.Add(door);
        }
    }

    /// <summary>
    /// Each door finds the sector portal it stands in once every door has its solid: <c>Door_UpdateInternalDataHandler</c>'s
    /// <c>Door_AttachToPortal</c>, against the stationary world.
    /// </summary>
    public static void DoorPortals(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Flow.Igi2LevelRun level = b.Level;
        Igi2SectorGraph sectors = b.Sectors ?? throw new InvalidOperationException("The door portals need the level's sectors, which the ambience system reads.");
        Igi2SectorPortals portals = new(player.CollisionWorld, sectors);
        level.SectorPortals = portals;
        foreach (Igi2DoorTask door in b.Root.Descendants().OfType<Igi2DoorTask>())
        {
            door.Portals = portals;
            door.Camera = () => level.CutsceneView?.Position ?? player.Human.View.Position;
            door.AttachToPortal(player.CollisionWorld);
        }
    }

    /// <summary>Props moved along spline guides: they move, and carry what stands on them.</summary>
    public static void Props(Igi2LevelBuild b)
    {
        foreach (Igi2EditRigidObjTask prop in b.Root.Descendants().OfType<Igi2EditRigidObjTask>())
        {
            if (prop.Guide is not null)
            {
                prop.Models = b.Models;
                prop.Magic = b.Magic;
                prop.Bind(b.Player.CollisionWorld.SolidsOf(Igi2EditRigidObjTask.TypeName, prop.Id));
                b.Movers.Add(prop);
            }
        }
    }

    /// <summary>
    /// What can be blown up, destroyed or picked up: every ExplodeObject armed with its models and blasts, its solids answering
    /// bullets, a target for blasts and drawn destroyed once it is; the generic pickups; the terminals; the alarm controls.
    /// </summary>
    public static void Destructibles(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        List<IIgi2Destructible> destructibles = [];
        foreach (Igi2ExplodeObjectTask explosive in b.Root.Descendants().OfType<Igi2ExplodeObjectTask>())
        {
            Arm(explosive, Igi2ExplodeObjectTask.TypeName);
        }

        foreach (Igi2GenericTbaTask action in b.Root.Descendants().OfType<Igi2GenericTbaTask>())
        {
            Arm(action, Igi2GenericTbaTask.TypeName);
            b.Activatables.Add(action);
        }

        List<Pickups.Igi2GenericPickupTask> genericPickups = [];
        foreach (Pickups.Igi2GenericPickupTask pickup in b.Root.Descendants().OfType<Pickups.Igi2GenericPickupTask>())
        {
            pickup.PlaySound = name => player.Human.Surroundings.PlaySound(name);
            genericPickups.Add(pickup);
            destructibles.Add(pickup);
        }

        player.Human.GenericPickups = genericPickups;

        foreach (Igi2TerminalTask terminal in b.Root.Descendants().OfType<Igi2TerminalTask>())
        {
            Arm(terminal, Igi2TerminalTask.TypeName);
            b.Activatables.Add(terminal);
        }

        foreach (Igi2AlarmControlTask alarm in b.Root.Descendants().OfType<Igi2AlarmControlTask>())
        {
            Arm(alarm, Igi2AlarmControlTask.TypeName);
            alarm.AlarmRaised = () => player.Human.TimesAlarmTriggered++;
            alarm.AiEvents = e => player.Ai?.SendEvent(e);
            b.Activatables.Add(alarm);
        }

        player.Destruction = destructibles.Count > 0 || player.Glass.Panes.Count > 0
            ? new Igi2PlacementDestruction(destructibles) { Glass = player.Glass }
            : null;

        void Arm(IIgi2ExplodingTask task, string typeName)
        {
            task.Explosive.Models = b.Models;
            task.Explosive.Explosions = player.Explosions;
            task.Explosive.Bind(player.CollisionWorld.SolidsOf(typeName, task.Id));
            player.Explosions?.AddTarget(task);
            destructibles.Add(task);
        }
    }

    /// <summary>A minefield's blasts go off at a task of the level, put on its ground.</summary>
    public static void MineFields(Igi2LevelBuild b)
    {
        Dictionary<int, object> placed = b.PlacedById;
        foreach (Igi2MineFieldTask field in b.Root.Descendants().OfType<Igi2MineFieldTask>())
        {
            field.Collision = b.Player.CollisionWorld;
            field.TaskById = id => placed.GetValueOrDefault(id);
        }
    }

    /// <summary>What the player can activate; and soldiers, which answer the activate search with a silent kill.</summary>
    public static void Activation(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        player.Activatables.AddRange(b.Activatables);
        if (player.Soldiers is not { } soldiers)
        {
            return;
        }

        player.Activatables.AddRange(soldiers.Bodies);
        Dictionary<int, IIgi2Activatable> byId = [];
        foreach (IIgi2Activatable thing in b.Activatables)
        {
            if (thing is GameTask { Id: >= 0 } placed)
            {
                byId.TryAdd(placed.Id, thing);
            }
        }

        foreach (Soldiers.Igi2HumanSoldier soldier in soldiers.Bodies)
        {
            if (soldier.Surroundings is Igi2LevelSurroundings surroundings)
            {
                surroundings.ById = byId;
            }
        }
    }

    /// <summary>The cameras the player sees, and everything that moves what stands on it.</summary>
    public static void Motions(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        player.Cameras = [.. b.Root.Descendants().OfType<Igi2SecurityCameraTask>()];
        List<IIgi2PartMover> partMovers = [.. b.Root.Descendants().OfType<Igi2CabinetTask>(), .. b.Turrets, .. b.Cars, .. player.Cameras];
        player.Motions = b.Movers.Count > 0 || partMovers.Count > 0 ? new Igi2MoverMotions(b.Movers, partMovers) : null;
    }
}
