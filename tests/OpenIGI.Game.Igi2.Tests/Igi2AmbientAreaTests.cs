using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Ambient areas and the microphone's sector, as ambientarea.c and sectorobject.c work them.</summary>
public sealed class Igi2AmbientAreaTests
{
    /// <summary>
    /// A listener at the centre of a box fades its volume up by one over the fade time in ticks plus one, to one; outside the box
    /// and its falloff it fades back down; within the falloff the volume is held to one less the distance past the box over the
    /// falloff.
    /// </summary>
    [Fact]
    public void The_volume_fades_toward_where_the_listener_stands()
    {
        Listener listener = new() { Position = new Vector3d(0, 0, 0) };
        Igi2AmbientAreaTask area = new() { Size = new Vector3d(2, 2, 2), Falloff = 10, FadeTime = 1.0 / 3.0, Listener = listener };
        area.Prepare();
        Flow.Igi2LevelRun level = Level();

        area.Run(level);
        Assert.Equal(1.0 / 11.0, area.Volume, 6);
        for (int tick = 0; tick < 20; tick++)
        {
            area.Run(level);
        }

        Assert.Equal(1.0, area.Volume);

        // Seven metres along X: five past the box, so the volume is held to one half.
        listener.Position = new Vector3d(7 * 4096.0, 0, 0);
        area.Run(level);
        Assert.Equal(0.5, area.Volume, 6);

        listener.Position = new Vector3d(20 * 4096.0, 0, 0);
        area.Run(level);
        Assert.Equal(0.0, area.Volume);
    }

    /// <summary>An area that sounds only outside falls silent while the listener stands in a building.</summary>
    [Fact]
    public void An_outside_area_is_silent_indoors()
    {
        Listener listener = new() { Position = default, Inside = true };
        Igi2AmbientAreaTask area = new() { OutsideBuildings = true, InsideBuildings = false, Listener = listener };
        area.Prepare();
        area.Run(Level());
        Assert.Equal(0.0, area.Volume);
    }

    /// <summary>Mission 1's areas load as the class, each with its list of sounds.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_areas_load()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AmbientAreaTask[] areas = [.. m.Tree.OfType<Igi2AmbientAreaTask>()];
        Assert.NotEmpty(areas);
        foreach (Igi2AmbientAreaTask area in areas)
        {
            area.Prepare();
        }

        Assert.Contains(areas, static a => a.Sounds.Count > 0);
    }

    /// <summary>
    /// The player starts in the open; a point a metre above a room's floor, under its roof, stands in that room -- and a
    /// building's shell is never the answer, because <c>SectorObject_GetSectorHandler</c> traces only the rooms.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_locator_finds_a_buildings_room()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2SectorGraph graph = Igi2SectorGraph.Build(m.Level.Scene, m.Level.ModelFor);
        Igi2SectorLocator locator = new(m.Collision!, graph);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Assert.Null(locator.SectorAt(spawn.Position, lift: true));

        int rooms = 0;
        int inside = 0;
        foreach (Igi2Sector room in graph.Sectors.Where(static s => !s.IsShell))
        {
            rooms++;
            Vector3d p = room.Position;
            if (locator.SectorAt(new Vector3d(p.X, p.Y, p.Z + 4096.0)) is { } found)
            {
                Assert.False(found.IsShell);
                if (found.Index == room.Index)
                {
                    inside++;
                }
            }
        }

        Assert.True(inside > 0, $"no room holds a point above its own origin out of {rooms}");
    }

    private static Flow.Igi2LevelRun Level()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }

    private sealed class Listener : IIgi2Listener
    {
        public Vector3d Position { get; set; }

        public bool Inside { get; set; }

        bool IIgi2Listener.IsInside => Inside;
    }
}
