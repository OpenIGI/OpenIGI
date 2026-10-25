using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Sectors;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// I.G.I. 2's sector cull on mission 1: which buildings, rooms and interior objects a camera's place in the level leaves drawn.
/// </summary>
/// <remarks>
/// Mission 1 lays two chain fences, <c>EditRigidObj</c>s, along the outside of an office so that they cross its walls. Their
/// centres stand in no room, so <c>RigidDynCubeObj_EnterHandler</c> returns early for them while the camera is in the building
/// and no opening shows the outside.
/// </remarks>
public sealed class RetailIgi2SectorVisibilityTests
{
    private const int OfficeLevelId = 3914;
    private const int FenceLevelId = 3909;

    private static readonly Lazy<Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2MagicObjTypes magic = Igi2Session.Start().Session.Services.Require<Igi2MagicObjTypes>();
        WorldScene scene = Igi2LevelScene.Build(level, magic).Built.Scene;
        Igi2CollisionWorld collision = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner, magic: magic);
        Igi2SectorGraph graph = Igi2SectorGraph.Build(level.Scene, level.ModelFor, magic);
        Igi2SectorLocator locator = new(collision, graph);
        return new Setup(level, scene, graph, locator);
    });

    /// <summary>Every part of a building is drawn by its shell or by one of its rooms, and every room has its own key.</summary>
    /// <remarks>
    /// The parts' keys are made as the drawable scene walks the attach points (<c>SectorObject_CreateAttachObjects</c>), the
    /// rooms by the same reading the sector graph makes, so each room's key resolves to the room the graph holds.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_part_of_a_building_is_drawn_by_its_shell_or_one_of_its_rooms()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility();

        HashSet<int> rooms = [];
        foreach (WorldPlacement placement in m.Scene.Placements)
        {
            if (placement.VisibilityKey is not Igi2VisibilityKey { Kind: Igi2VisibilityKind.Shell or Igi2VisibilityKind.Room } key)
            {
                continue;
            }

            Igi2Sector sector = Assert.IsType<Igi2Sector>(visibility.SectorOf(key));
            Assert.Equal(key.Kind == Igi2VisibilityKind.Shell, sector.IsShell);
            if (!sector.IsShell)
            {
                Assert.Equal(key.Room, sector.Room);
                rooms.Add(sector.Index);
            }
        }

        Assert.Equal(m.Graph.Sectors.Count(static s => !s.IsShell), rooms.Count);
    }

    /// <summary>The fence through the office stands in no room; the props inside the office stand in its room.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_fence_stands_in_no_room_and_the_offices_props_stand_in_its_room()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility();
        Assert.Null(visibility.SectorOf(m.Fence));

        Igi2Sector office = m.OfficeRoom;
        int inside = Igi2SectorVisibility.KeysOf(m.Scene)
            .Count(k => k.Kind == Igi2VisibilityKind.Object && visibility.SectorOf(k)?.Index == office.Index);
        Assert.True(inside > 0, "no interior object stands in the office");
    }

    /// <summary>
    /// From inside the office with every opening shut, nothing outside is drawn: not the fence, not the other buildings, not the
    /// ground -- only the room the camera stands in.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void From_inside_with_no_opening_the_outside_is_not_drawn()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility(static (_, _) => false);
        Camera3D eye = m.InsideOfficeLookingAt(m.Fence.Centre);

        visibility.Decide(eye, Projection());

        Assert.Equal(m.OfficeRoom, visibility.EyeRoom);
        Assert.False(visibility.IsOutsideVisible);
        Assert.False(visibility.IsVisible(m.Fence));
        Assert.True(visibility.IsVisible(m.KeyOf(m.OfficeRoom)));
        Assert.False(visibility.IsVisible(m.KeyOf(m.Graph.Objects[m.OfficeRoom.ObjectIndex].Shell)));
        Assert.All(
            Igi2SectorVisibility.KeysOf(m.Scene).Where(k => k.Kind == Igi2VisibilityKind.Shell && k.LevelId != OfficeLevelId),
            k => Assert.False(visibility.IsVisible(k)));
    }

    /// <summary>From outside, the cull leaves every building's shell and the fence to the frustum, and the ground drawn.</summary>
    [RequiresTitleDataFact("igi2")]
    public void From_outside_everything_outside_is_drawn()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility();
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Vector3d eye = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 7000.0);

        visibility.Decide(Looking(eye, m.Fence.Centre), Projection());

        Assert.Null(visibility.EyeRoom);
        Assert.True(visibility.IsOutsideVisible);
        Assert.True(visibility.IsVisible(m.Fence));
        Assert.All(
            Igi2SectorVisibility.KeysOf(m.Scene).Where(static k => k.Kind == Igi2VisibilityKind.Shell),
            k => Assert.True(visibility.IsVisible(k)));
    }

    /// <summary>
    /// <c>Direct3DRender_DrawRigidMesh</c>: from a room that cannot see out, every mesh standing in no room is left out -- a fence,
    /// a wire, a crate -- except the doors and the conveyor belts, which stay to the frustum.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Unseen_from_inside_only_doors_and_conveyor_belts_stand_outside_the_rooms()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility(static (_, _) => false);
        visibility.Decide(m.InsideOfficeLookingAt(m.Fence.Centre), Projection());

        List<Igi2VisibilityKey> unroomed = [.. Igi2SectorVisibility.KeysOf(m.Scene)
            .Where(k => k.Kind == Igi2VisibilityKind.Object && visibility.SectorOf(k) is null)];
        Assert.Contains(unroomed, static k => k.IsShownUnseen);
        Assert.Contains(unroomed, static k => !k.IsShownUnseen && !k.RegistersInRooms);
        Assert.All(unroomed, k => Assert.Equal(k.IsShownUnseen, visibility.IsVisible(k)));
        Assert.Null(visibility.OutsideClip);
    }

    /// <summary>
    /// Once the camera's room sees out, the terrain and the forests are clipped to the rectangle enclosing the outside's windows: a
    /// part of the view, and never the whole of it from inside.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Seen_out_of_a_room_the_ground_is_clipped_to_the_enclosing_window()
    {
        Setup m = Mission1.Value;
        Igi2SectorVisibility visibility = m.Visibility();
        Vector3d p = m.OfficeRoom.Position;
        Vector3d eye = new(p.X, p.Y, p.Z + 4096.0);
        bool seen = false;
        for (int yaw = 0; yaw < 360 && !seen; yaw += 15)
        {
            visibility.Decide(new Camera3D(eye, Camera3D.LookRotation(yaw * MathF.PI / 180f, 0f)), Projection());
            seen = visibility.EyeRoom is not null && visibility.OutsideClip is not null;
        }

        Assert.True(seen, "the office never sees out from a metre above its origin");
        ViewClip clip = visibility.OutsideClip!.Value;
        Assert.False(clip.IsEmpty);
        Assert.NotEqual(ViewClip.Full, clip);
        Assert.True(visibility.IsOutsideVisible);
    }

    /// <summary>
    /// <c>Graph_UpdateInternalDataAfterLoadHandler</c>: a node laid indoors stands in a room, one laid on the terrain in none -- which
    /// is what a soldier's current sector, and the room its body is drawn by, are read from.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Graph_nodes_laid_indoors_stand_in_rooms()
    {
        Setup m = Mission1.Value;
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(m.Level.Directory, "objects.qvm"), loadTables: true);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
        OpenIGI.Engine.Vfs.VirtualFileSystem files = new();
        files.RegisterDevice(new OpenIGI.Engine.Vfs.LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(OpenIGI.Engine.Vfs.VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        Ai.Igi2AiGraphs graphs = Ai.Igi2AiGraphs.Build(
            harness.LevelRoot, new OpenIGI.Engine.Resources.ResourceRegistry(files), harness.Loop.Natives,
            harness.Session.Services.Require<Scripting.Igi2ScriptFunctions>());

        Ai.Runtime.Igi2LevelAi.AssignNodeSectors(graphs, point => m.Locator.SectorAt(point, lift: true));

        Ai.Igi2AiGraphNode[] nodes = [.. graphs.Graphs.Values.SelectMany(static g => g.Nodes.Values)];
        Assert.Contains(nodes, static n => n.Sector is Igi2Sector);
        Assert.All(nodes.Where(static n => n.Terrain != 0), static n => Assert.Null(n.Sector));
        Assert.All(nodes.Select(static n => n.Sector).OfType<Igi2Sector>(), static s => Assert.False(s.IsShell));
    }

    private static SectorScreenProjection Projection() =>
        SectorScreenProjection.FromViewport(new SectorScreenRect(0f, 0f, 640f, 480f), 1f);

    private static Camera3D Looking(Vector3d eye, Vector3d at)
    {
        Vector3f forward = new Vector3f((float)(at.X - eye.X), (float)(at.Y - eye.Y), (float)(at.Z - eye.Z)).Normalized();
        return new Camera3D(eye, Camera3D.LookRotation(MathF.Atan2(forward.X, forward.Y), MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))));
    }

    private sealed record Setup(Igi2Level Level, WorldScene Scene, Igi2SectorGraph Graph, Igi2SectorLocator Locator)
    {
        public Igi2VisibilityKey Fence => Igi2SectorVisibility.KeysOf(Scene)
            .Single(static k => k.Kind == Igi2VisibilityKind.Object && k.LevelId == FenceLevelId);

        public Igi2Sector OfficeRoom => Graph.Objects.Single(static o => o.Shell.LevelId == OfficeLevelId).Rooms[0];

        public Igi2SectorVisibility Visibility(Func<int, int, bool>? isPortalOpen = null) =>
            new(Graph, Locator, Igi2SectorVisibility.KeysOf(Scene), isPortalOpen);

        public Igi2VisibilityKey KeyOf(Igi2Sector sector)
        {
            Igi2SectorVisibility visibility = Visibility();
            return Igi2SectorVisibility.KeysOf(Scene).First(k => visibility.SectorOf(k) is { } s && s.Index == sector.Index && k.Kind != Igi2VisibilityKind.Object);
        }

        // A metre above the room's origin, under its roof, which the locator answers with the room.
        public Camera3D InsideOfficeLookingAt(Vector3d at)
        {
            Vector3d p = OfficeRoom.Position;
            Vector3d eye = new(p.X, p.Y, p.Z + 4096.0);
            Assert.Equal(OfficeRoom, Locator.SectorAt(eye));
            return Looking(eye, at);
        }
    }
}
