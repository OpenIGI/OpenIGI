using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>The level's flow, its sound, its areas and its messages: what the player's screens and ears are bound to.</summary>
internal static class Igi2FlowSystems
{
    /// <summary>The flow's reset, the in-game menu, the cutscene shake, and the player bound to its task.</summary>
    public static void Flow(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2LevelRun level = b.Level;
        if (b.Services.TryGet(out Igi2FlowState? flow))
        {
            flow.Reset();
            player.Flow = flow;
        }

        // GameScreen_CreateHandler: the in-game menu, made of ingamemenu.qsc as the level's screen is; the shell presents it.
        if (b.Services.TryGet(out Menus.Igi2InGameMenu? menu) && menu.Load(b.Root, b.Request.World.Resources, b.Natives))
        {
            menu.LevelFailed = () => level.LevelFlow?.IsFailed == true;
            menu.PlayerDead = () => player.Human.IsDead;
            player.Menu = menu;
        }

        if (b.Services.TryGet(out Igi2CutsceneShake? shake))
        {
            level.CutsceneShake = shake;
        }

        level.Bind(b.PlayerTask, player.RunPlayer);
        if (b.PlayerTask is Igi2HumanPlayerTask playerTask)
        {
            playerTask.Human = player.Human;
            player.Human.Task = playerTask;
        }

        player.Input = b.PlayerTask.Children.OfType<Igi2HumanPlayerInputTask>().FirstOrDefault();
    }

    /// <summary>The microphone hears from the game's camera, inside whatever sector it stands in; each ambient area plays to it.</summary>
    public static void Ambience(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2LevelRun level = b.Level;
        Func<string, Formats.World.MefModel?> models = b.Models;
        b.Sectors = Igi2SectorGraph.Build(Igi2WorldScene.Build(b.Root, models), models, b.Magic);
        Igi2SectorLocator locator = new(player.CollisionWorld, b.Sectors);
        b.SectorLocator = locator;
        b.Listener = new Audio.Igi2CameraListener(
            () => level.CutsceneView?.Position ?? player.Human.View.Position,
            locator,
            () => level.Ticks);
        foreach (Audio.Igi2AmbientAreaTask area in b.Root.Descendants().OfType<Audio.Igi2AmbientAreaTask>())
        {
            area.Listener = b.Listener;
            area.Random = player.Human.Random;
            area.Prepare();
            b.Ambience.Add(area);
        }
    }

    /// <summary>The map computer's tasks join it as they are made: the last limit, every highlight with its markers read.</summary>
    public static void MapComputer(Igi2LevelBuild b)
    {
        if (!b.Services.TryGet(out Computer.Igi2Computer? computer))
        {
            return;
        }

        computer.BeginLevel();
        b.Player.Computer = computer;
        foreach (GameTask node in b.Root.Descendants())
        {
            switch (node)
            {
                case Computer.Igi2DefineComputerObjectiveTask objectives:
                    objectives.Computer = computer;
                    break;
                case Computer.Igi2DefineComputerLimitTask limit:
                    limit.Attach(computer);
                    break;
                case Computer.Igi2ComputerPlanTask plan:
                    plan.Attach(computer);
                    break;
                case Computer.Igi2ComputerHilightTask hilight:
                    hilight.Attach(computer);
                    hilight.ReadMarkers(b.Natives, b.Functions);
                    break;
            }
        }
    }

    /// <summary>The areas that count humans and cars: the level's humans in <c>Human_ptQTaskList</c>'s order, its cars as made.</summary>
    public static void Areas(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        IIgi2AreaOccupant[] humans = [.. b.LevelHumans()];
        Vehicles.Igi2CarTask[] levelCars = [.. b.Root.Descendants().OfType<Vehicles.Igi2CarTask>()];
        player.MapVehicles = levelCars;
        player.MapGunHolders = [.. b.Root.Descendants().OfType<Weapons.Igi2AiStationaryGunHolderTask>()];
        foreach (Igi2AreaActivateTask area in b.Root.Descendants().OfType<Igi2AreaActivateTask>())
        {
            area.Humans = humans;
            area.Players.Set(player.Human);
            area.CameraPosition = () => player.EyePosition;
            area.VehiclePositions = () => levelCars.Select(static car => car.Position);
        }
    }

    /// <summary>Each line's pieces are counted once its parameters are read, in the language the screens load.</summary>
    public static void StatusMessages(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2Strings strings = new(b.Request.World.Resources);
        player.Strings = strings;
        foreach (Igi2StatusMessageTask message in b.Root.Descendants().OfType<Igi2StatusMessageTask>())
        {
            message.Strings = strings;
            message.Screen = player.StatusMessages;
            message.Listener = () => player.Human.View.Position;
            message.TaskById = id => b.PlacedById.GetValueOrDefault(id);
            message.InitText();
        }
    }

    /// <summary>The player and its grenades read the level flow's clock.</summary>
    public static void LevelClock(Igi2LevelBuild b)
    {
        Igi2LevelRun level = b.Level;
        b.Player.Human.LevelTick = () => level.LevelFlow?.Tick ?? 0;
        if (b.Player.Grenades is { } launched)
        {
            launched.LevelTick = () => level.LevelFlow?.Tick ?? 0;
        }
    }
}
