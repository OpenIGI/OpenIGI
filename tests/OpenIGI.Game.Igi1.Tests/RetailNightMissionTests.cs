using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Which missions the dynamic renderer opens at night, read off the sky the original paints.</summary>
/// <remarks>
/// <para>
/// Every mission authors its <c>Dirlight</c> at a <c>Time</c> of 12, so the day/night cycle opened all of
/// them at noon -- including the third, which vanilla draws under a slate night dome. The dome is the only
/// thing in a level that says night, and these read it from the retail data rather than from the
/// provider's own constants.
/// </para>
/// </remarks>
public sealed class RetailNightMissionTests
{
    private static readonly int[] NightMissions = [3, 7, 9, 11, 12, 13];
    private static readonly int[] DayMissions = [1, 2, 4, 5, 6, 8, 10];

    /// <summary>The six missions with a dark dome are bound by their night end; the rest are left alone.</summary>
    [RequiresGameDataTheory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, false)]
    public void A_mission_opens_at_night_exactly_when_its_dome_is_painted_dark(int mission, bool night)
    {
        GameTask root = Load(mission);

        DeferredLighting? lighting = new Igi1PresentationProvider().ReadDeferredLighting(root);

        if (!night)
        {
            // Null is what keeps a daylit mission's sweep, and its regression picture, exactly as it was.
            Assert.Null(lighting);
            return;
        }

        DeferredLighting lit = Assert.IsType<DeferredLighting>(lighting);
        AuthoredNight bound = Assert.IsType<AuthoredNight>(lit.Night);
        Assert.Equal(24f, bound.ReferenceHour);
        Assert.False(bound.SunIsMoon, "this game's night levels author a noon sun, which is no moon");
        Assert.False(lit.CompleteBake);
        Assert.Equal(GlobalLightSettings.Read(root).ObjectAmbient, lit.Ambient);
    }

    /// <summary>The threshold sits in a gap in the data, not on a slope.</summary>
    [RequiresGameDataFact]
    public void The_night_threshold_separates_the_domes_with_room_on_both_sides()
    {
        float darkestDay = DayMissions.Min(DomeLuminance);
        float brightestNight = NightMissions.Max(DomeLuminance);

        Assert.True(brightestNight < Igi1PresentationProvider.NightSkyLuminance - 0.04f, $"brightest night {brightestNight}");
        Assert.True(darkestDay > Igi1PresentationProvider.NightSkyLuminance + 0.04f, $"darkest day {darkestDay}");
    }

    /// <summary>The day a night level sweeps up to is the median of the days the other levels author.</summary>
    [RequiresGameDataFact]
    public void The_daylight_a_night_sweeps_up_to_is_the_median_authored_day()
    {
        List<DirectionalLightSettings> suns = [];
        foreach (int mission in DayMissions)
        {
            Assert.True(DirectionalLightSettings.TryRead(Load(mission), out DirectionalLightSettings sun));
            suns.Add(sun);
        }

        AssertClose(Median(suns.Select(static s => s.Colour)), Igi1PresentationProvider.DaylightColour);
        AssertClose(Median(suns.Select(static s => s.Ambient)), Igi1PresentationProvider.DaylightAmbient);

        static Vector3f Median(IEnumerable<Vector3f> values)
        {
            List<Vector3f> all = [.. values];
            return new Vector3f(Mid(all.Select(static v => v.X)), Mid(all.Select(static v => v.Y)), Mid(all.Select(static v => v.Z)));

            static float Mid(IEnumerable<float> channel) => channel.Order().ElementAt(3);
        }

        static void AssertClose(Vector3f expected, Vector3f actual)
        {
            Assert.Equal(expected.X, actual.X, 5);
            Assert.Equal(expected.Y, actual.Y, 5);
            Assert.Equal(expected.Z, actual.Z, 5);
        }
    }

    private static float DomeLuminance(int mission)
    {
        Assert.True(SkyDomeColours.TryRead(Load(mission), out SkyDomeColours dome), $"mission {mission} has no FlatSky");
        return dome.MeanLuminance;
    }

    private static LevelTask Load(int mission)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        PlayerTaskTypes.RegisterAll(loop.Types);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        new MissionLoader(registry, fileSystem, resources, levelLoader, null, null).Load(mission);
        return levelRoot;
    }
}
