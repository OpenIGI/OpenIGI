using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>I.G.I. 2's own <c>DefineMission</c>, and the four packs a mission opens.</summary>
/// <remarks>
/// <para>
/// Fourteen arguments where the first game takes eleven, and the difference is not a suffix: the first
/// game's fourth argument is the id of the mission that follows, and this one has four
/// single-character strings spelling a game mode instead.
/// </para>
/// <para>
/// The pack list is the point. <c>Game_CreateHandler</c> reads the descriptor at <c>+0x550</c>,
/// <c>+0x750</c>, <c>+0x650</c> and <c>+0x770</c> — the common directory, its pack name, the level
/// directory and its pack name — and loads two global packs before them. Neither the order nor the
/// names are guessed from the directory tree.
/// </para>
/// </remarks>
public sealed class RetailIgi2MissionCatalogueTests
{
    /// <summary>Every shipped mission registers, and the modes are the two the game defines.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_mission_registers_under_one_of_the_two_modes()
    {
        (Igi2MissionCatalogue catalogue, _, _) = Scan();

        Assert.True(catalogue.Count > 0, "no I.G.I. 2 mission script registered a mission.");

        int campaign = catalogue.Campaign.Count();
        int multiplayer = catalogue.Missions.Count(static m =>
            string.Equals(m.GameMode, Igi2MissionCatalogue.MultiplayerMode, StringComparison.Ordinal));

        Assert.Equal(catalogue.Count, campaign + multiplayer);
        Assert.True(campaign > 0, "not one mission declared the single-player mode.");
        Assert.True(multiplayer > 0, "not one map declared the multiplayer mode.");

        // No mission names the one that follows it; Game_RunHandler takes the next node of the mission list, so each
        // campaign mission leads to the next one defined, the last to none, and no multiplayer map leads anywhere.
        MissionDescriptor[] singlePlayer = [.. catalogue.Missions.Where(static m => m.GameMode == Igi2MissionCatalogue.SinglePlayerMode)];
        Assert.Equal(
            [.. singlePlayer.Skip(1).Select(static m => m.Id), MissionDescriptor.NextMissionUndefined],
            singlePlayer.Select(static m => m.NextMissionId));
        Assert.All(
            catalogue.Missions.Where(static m => m.GameMode != Igi2MissionCatalogue.SinglePlayerMode),
            m => Assert.Equal(MissionDescriptor.NextMissionUndefined, m.NextMissionId));

        // And every id is distinct, which the original enforces by aborting.
        Assert.Equal(catalogue.Count, catalogue.Missions.Select(static m => m.Id).Distinct().Count());
    }

    /// <summary>Every mission names four packs, in the order the game opens them.</summary>
    /// <remarks>
    /// The two global ones first — <c>common</c> then <c>new</c> — and then the two the descriptor
    /// itself names. Earlier shadows later, so the order is behaviour rather than presentation.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_mission_names_four_packs_in_load_order()
    {
        (Igi2MissionCatalogue catalogue, _, _) = Scan();

        foreach (MissionDescriptor mission in catalogue.Missions)
        {
            IReadOnlyList<PackReference> packs = mission.PacksToOpen;

            Assert.Equal(4, packs.Count);
            Assert.Equal(Igi2MissionCatalogue.GlobalCommonPack, packs[0]);
            Assert.Equal(Igi2MissionCatalogue.GlobalNewPack, packs[1]);

            // The third and fourth are the descriptor's own, which is what the binary reads at +0x550/
            // +0x750 and +0x650/+0x770.
            Assert.Equal($"LOCAL:{mission.CommonPackDirectory}/", packs[2].Directory);
            Assert.Equal(mission.CommonPackName, packs[2].Name);
            Assert.Equal($"LOCAL:{mission.LevelPackDirectory}/", packs[3].Directory);
            Assert.Equal(mission.LevelPackName, packs[3].Name);
        }
    }

    /// <summary>A campaign mission loads through the shared loader with all four packs open.</summary>
    /// <remarks>
    /// End to end through <see cref="MissionLoader"/> rather than a fixture, because the point of the
    /// catalogue is that the loader can now open what a title names. Four packs also has to mean four
    /// packs <em>with models in them</em> — a pack opened under the wrong format profile refuses every
    /// model it holds and still counts as opened.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_campaign_mission_loads_with_all_four_packs_open()
    {
        (Igi2MissionCatalogue catalogue, VirtualFileSystem files, ResourceRegistry resources) = Scan();
        MissionDescriptor mission = catalogue.Campaign.First();


        // Through the title, as a host does: its structural task types and its content modules. Without
        // the first, Container is registered with no parameters and every I.G.I. 2 level refuses to load
        // -- the level's own editor declares it with one, which is the whole reason the set is the
        // title's to supply.
        Igi2Runtime title = new Igi2Runtime();
        GameLoop loop = new(new ManualTimeSource(1_000), structuralTypes: title.Layout.StructuralTaskTypes);
        TitleSession session = TitleSession.Start(title.Modules, loop, files, resources, loadTables: false);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);

        LoadedMission loaded = new MissionLoader(
            catalogue,
            files,
            resources,
            levelLoader,
            logger: null,
            terrainLoader: null,
            soundBank: null,
            formats: AssetFormatProfile.Igi2)
            .Load(mission.Id);

        IReadOnlyList<LoadedPack> packs = loaded.PacksInLoadOrder;
        Assert.Equal(4, packs.Count);
        Assert.Equal("common", packs[0].Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("new", packs[1].Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(mission.CommonPackName, packs[2].Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(mission.LevelPackName, packs[3].Name, StringComparer.OrdinalIgnoreCase);

        // Opened is not enough: a pack read under the wrong format profile refuses every model it holds
        // and still counts as opened. Three of the four have to come back with instances in them.
        //
        // The fourth is "new", and it is empty of models on purpose: it is a FORM/MTP whose one chunk is
        // BANM -- bone animation names, "stand_lowerbody" and its siblings. The second global pack is an
        // ANIMATION pack, which is why it contributes nothing to the model table and why the load order
        // that has it shadowing every mission's models costs nothing.
        Assert.Empty(packs[1].Instances);
        Assert.All(
            packs.Where((_, i) => i != 1),
            pack => Assert.NotEmpty(pack.Instances));

        // That it was read at all is the animation names in it -- nineteen kilobytes of BANM. Without
        // this the "four packs" above would pass just as well against a pack that was never found, since
        // a missing one opens as an empty one rather than failing.
        Assert.NotEmpty(packs[1].Pack.AnimationSetNames);

        // The sets themselves do NOT bind, and that is a gap rather than a surprise: the pack loader
        // looks for each under LOCAL:common/anims/<name>.iff, which is the first game's arrangement, and
        // I.G.I. 2 ships no .iff anywhere -- its animation data is in new.dat beside new.mtp. Reading it
        // belongs with the player, which is the milestone that needs an animation at all.
        Assert.Empty(packs[1].AnimationSets);

        // And the first two really are the global ones rather than the mission's own repeated.
        Assert.NotEqual(packs[0].Name, packs[3].Name);
    }

    /// <summary>The two orderings answer two different questions.</summary>
    /// <remarks>
    /// <para>
    /// A progression is what a mission-select menu offers; a browser order is everything the install
    /// holds. They differ here because the multiplayer maps take ids <b>1, 5 and 100</b> — below and
    /// above the campaign's 11 to 36 — so a single list sorted by id would put two deathmatch maps ahead
    /// of the first mission of the game and one after the last.
    /// </para>
    /// <para>
    /// Neither is asserted by the data. This title's <c>DefineMission</c> names no next mission, so
    /// ascending id is the campaign's own grouping — a mission's id is its location and level read
    /// together — rather than an order the game states.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_progression_is_the_campaign_and_the_browser_order_is_everything()
    {
        (Igi2MissionCatalogue catalogue, _, _) = Scan();

        List<MissionDescriptor> progression = [.. catalogue.InProgressionOrder()];
        List<MissionDescriptor> browser = [.. catalogue.InCampaignOrder()];

        // The progression is the campaign and only the campaign.
        Assert.Equal(catalogue.Campaign.Count(), progression.Count);
        Assert.All(
            progression,
            m => Assert.Equal(Igi2MissionCatalogue.SinglePlayerMode, m.GameMode));

        // The browser lists everything, campaign first, and loses nothing.
        Assert.Equal(catalogue.Count, browser.Count);
        Assert.Equal(catalogue.Count, browser.Select(static m => m.Id).Distinct().Count());
        Assert.Equal(progression.Select(static m => m.Id), browser.Take(progression.Count).Select(static m => m.Id));

        // Both ascend within their own run.
        Assert.Equal(progression.Select(static m => m.Id).Order(), progression.Select(static m => m.Id));

        // And the two really are different lists here, which is what makes the distinction worth having:
        // a multiplayer map sorts below the campaign's first mission.
        Assert.True(browser.Count > progression.Count, "every mission was in the campaign.");
        Assert.Contains(browser, m => m.Id < progression[0].Id);
    }

    /// <summary>The mode is the four characters the script spells, in order.</summary>
    [Fact]
    public void The_mode_is_the_four_characters_the_script_spells()
    {
        Assert.Equal("SNGL", Igi2MissionCatalogue.ModeOf("S", "N", "G", "L"));
        Assert.Equal("MULT", Igi2MissionCatalogue.ModeOf("M", "U", "L", "T"));
        Assert.Equal(Igi2MissionCatalogue.SinglePlayerMode, Igi2MissionCatalogue.ModeOf("S", "N", "G", "L"));

        // A script that passed a longer string takes only its first character, which is what the
        // original's two-byte buffer per argument amounts to.
        Assert.Equal("SNGL", Igi2MissionCatalogue.ModeOf("Sx", "Ny", "Gz", "Lw"));
        Assert.Equal("NGL", Igi2MissionCatalogue.ModeOf(string.Empty, "N", "G", "L"));
    }

    private static (Igi2MissionCatalogue Catalogue, VirtualFileSystem Files, ResourceRegistry Resources) Scan()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        ResourceRegistry resources = new(files);
        GameLoop loop = new(new ManualTimeSource());

        Igi2MissionCatalogue catalogue = new(NullLogger.Instance);
        catalogue.Register(loop.Natives);
        catalogue.Scan(files, resources, loop.Natives);

        return (catalogue, files, resources);
    }
}
