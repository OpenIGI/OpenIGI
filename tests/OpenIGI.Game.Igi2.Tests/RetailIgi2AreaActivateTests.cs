using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's areas (<c>AreaActivateQTask_RunHandler</c>, <c>igi2 0x592FC0</c>): what is inside their boxes, what their criteria
/// count, and the play area that keeps the mission from failing.
/// </summary>
public sealed class RetailIgi2AreaActivateTests
{
    /// <summary>The play area, <c>AreaActivate_178</c>, whose emptiness fails the mission.</summary>
    private const int PlayArea = 178;

    /// <summary>The upper conveyor, <c>AreaActivate_149</c>, a small box turned by nothing.</summary>
    private const int Conveyor = 149;

    /// <summary>All twenty are typed with their boxes; mission 1 counts the player's team, <c>CRITERIA_HUMAN0</c>, everywhere.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_areas_are_read()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AreaActivateTask[] areas = [.. m.Tree.OfType<Igi2AreaActivateTask>()];
        Assert.Equal(20, areas.Length);
        Igi2AreaActivateTask play = areas.Single(static a => a.Id == PlayArea);
        Assert.Equal(new Vector3f(3800096f, 3500096f, 2000096f), play.Dimensions);
        Assert.Equal("CRITERIA_HUMAN0", play.Criteria);

        m.Step();
        Assert.All(areas, static a => Assert.Equal(Igi2AreaCriteria.Team(0), a.Mask));
        Assert.Equal(1 << 3, Igi2AreaCriteria.Team(0));
    }

    /// <summary>A point is inside when within half the box along each of its axes, its faces included.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_box_holds_its_faces()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AreaActivateTask area = m.Tree.OfType<Igi2AreaActivateTask>().Single(static a => a.Id == Conveyor);
        Vector3d c = area.Position;
        float half = ((Fpu)area.Dimensions.X * 0.5).F;
        Assert.True(area.IsInside(c));
        Assert.True(area.IsInside(new Vector3d(c.X + half, c.Y, c.Z)));
        Assert.True(area.IsInside(new Vector3d(c.X - half, c.Y, c.Z)));
        Assert.False(area.IsInside(new Vector3d(c.X + half + 1.0, c.Y, c.Z)));
        Assert.False(area.IsInside(new Vector3d(c.X, c.Y, c.Z + area.Dimensions.Z)));
    }

    /// <summary>
    /// A team's criterion counts each human of its teams, a dead one too unless an AI drives it; the player's counts the player
    /// alone; the active humans' counts the active; and detection off holds the count.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_criteria_count_as_retails_do()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AreaActivateTask area = m.Tree.OfType<Igi2AreaActivateTask>().Single(static a => a.Id == Conveyor);
        Vector3d inside = area.Position;
        Vector3d outside = new(inside.X, inside.Y, inside.Z + area.Dimensions.Z);
        Occupant player = new(0, false, true, false, inside);
        area.Humans =
        [
            player,
            new Occupant(0, true, true, true, inside),
            new Occupant(0, true, true, false, inside),
            new Occupant(1, false, false, true, inside),
            new Occupant(0, false, true, true, outside),
        ];
        area.Players.Set(player);

        Assert.Equal(2, Count(m, area, "CRITERIA_HUMAN0"));
        // Each team bit runs the team's count again, over every team in the mask: two bits count the three twice.
        Assert.Equal(6, Count(m, area, "CRITERIA_HUMAN0 + CRITERIA_HUMAN1"));
        Assert.Equal(9, Count(m, area, "CRITERIA_HUMAN0 + CRITERIA_HUMAN1 + CRITERIA_HUMAN2"));
        Assert.Equal(1, Count(m, area, "CRITERIA_HUMANPLAYER"));
        Assert.Equal(3, Count(m, area, "CRITERIA_HUMAN"));
        Assert.Equal(0, Count(m, area, "CRITERIA_VEHICLE + CRITERIA_CAMERA + CRITERIA_HUMANAI_SIDEKICK"));

        area.IsDetectionDisabled = true;
        area.Humans = [];
        area.Run(m.Run);
        Assert.Equal(0, area.Active);
    }

    /// <summary>
    /// The camera counts once when inside (<c>GameScreen_CameraInAreaCB</c>); each vehicle inside counts (<c>Vehicle_AeraActivateCB</c>);
    /// each side-kick inside counts, dead or alive, at the root it drops things from (<c>HumanAI_AreaActivateSideKickCB</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_camera_the_vehicles_and_the_side_kicks_count()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AreaActivateTask area = m.Tree.OfType<Igi2AreaActivateTask>().Single(static a => a.Id == Conveyor);
        Vector3d inside = area.Position;
        Vector3d outside = new(inside.X, inside.Y, inside.Z + area.Dimensions.Z);
        area.Humans =
        [
            new Occupant(0, true, true, true, outside) { IsSideKick = true, SideKickPosition = inside },
            new Occupant(0, false, true, true, inside) { IsSideKick = true, SideKickPosition = outside },
            new Occupant(0, false, true, true, inside),
        ];

        Assert.Equal(1, Count(m, area, "CRITERIA_HUMANAI_SIDEKICK"));
        Assert.Equal(1, Count(m, area, "CRITERIA_CAMERA", camera: inside));
        Assert.Equal(0, Count(m, area, "CRITERIA_CAMERA", camera: outside));
        Assert.Equal(2, Count(m, area, "CRITERIA_VEHICLE", vehicles: [inside, outside, inside]));
    }

    /// <summary>
    /// The player standing in the play area keeps it occupied, so the level's "left the area" line is not sent; stood outside,
    /// it is.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_in_the_play_area_keeps_the_mission()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, null, spawn.Position, 0f);
        Assert.Equal(0, player.Human.Team);
        Bind(m, player.Human);
        Igi2StatusMessageTask left = m.Tree.OfType<Igi2StatusMessageTask>().Single(static s => s.Id == 2906);
        Igi2AreaActivateTask play = m.Tree.OfType<Igi2AreaActivateTask>().Single(static a => a.Id == PlayArea);

        m.Step();
        Assert.Equal(1, play.Active);
        Assert.False(left.IsSent);

        player.Human.Position = new Vector3d(play.Position.X + play.Dimensions.X, play.Position.Y, play.Position.Z);
        m.Step();
        m.Step();
        Assert.Equal(0, play.Active);
        Assert.True(left.IsSent);
    }

    private static void Bind(RetailIgi2Mission1 m, Igi2HumanPlayer human)
    {
        foreach (Igi2AreaActivateTask area in m.Tree.OfType<Igi2AreaActivateTask>())
        {
            area.Humans = [human];
            area.Players.Set(human);
        }
    }

    private static int Count(RetailIgi2Mission1 m, Igi2AreaActivateTask area, string criteria, Vector3d? camera = null, Vector3d[]? vehicles = null)
    {
        Igi2AreaActivateTask probe = new()
        {
            Position = area.Position,
            Orientation = area.Orientation,
            Dimensions = area.Dimensions,
            Criteria = criteria,
            Humans = area.Humans,
            CameraPosition = () => camera,
            VehiclePositions = () => vehicles ?? [],
        };
        probe.Players.Set(area.Players.Primary);
        probe.Prepare();
        probe.Run(m.Run);
        return probe.Active;
    }

    private sealed record Occupant(int Team, bool IsDead, bool IsActive, bool IsAi, Vector3d RootPosition) : IIgi2AreaOccupant
    {
        public bool IsSideKick { get; init; }

        public Vector3d SideKickPosition { get; init; } = RootPosition;
    }
}
