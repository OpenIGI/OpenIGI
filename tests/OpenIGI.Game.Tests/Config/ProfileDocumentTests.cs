using OpenIGI.Game.Config;
using OpenIGI.Game.Scoring;
using Xunit;

namespace OpenIGI.Game.Tests.Config;

/// <summary>
/// What of a profile follows the player through their account: each game's progress, the difficulty, the mouse and the
/// keys; never the name its saves are filed under. Two computers merge to the furthest mission in each game, and everything
/// else from whichever changed last.
/// </summary>
public sealed class ProfileDocumentTests
{
    /// <summary>A profile goes into a document and back unchanged, through JSON too, and its name stays behind.</summary>
    [Fact]
    public void A_profile_round_trips_through_its_document()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        profile.Name = "Joe";
        profile.ActiveMission = 6;
        profile.SetMission("igi2", 3);
        profile.Difficulty = 2;
        profile.InvertMouse = true;
        profile.MouseSensitivity = 0.7f;
        profile.RebindKeyboard(0, OpenIGI.Input.KeyCode.J);

        ProfileDocument? read = ProfileDocument.FromJson(ProfileDocument.From(profile).ToJson());
        Assert.NotNull(read);
        Assert.Equal(ProfileDocument.From(profile).Fingerprint(), read.Fingerprint());

        PlayerProfile other = PlayerProfile.CreateDefault();
        other.Name = "Elsewhere";
        read.ApplyTo(other);
        Assert.Equal("Elsewhere", other.Name);
        Assert.Equal(6, other.ActiveMission);
        Assert.Equal(3, other.MissionOf("igi2"));
        Assert.Equal(2, other.Difficulty);
        Assert.True(other.InvertMouse);
        Assert.Equal(0.7f, other.MouseSensitivity);
        Assert.Equal(profile.Bindings, other.Bindings);
    }

    /// <summary>Merging keeps the furthest mission in each game, and the settings of the side that changed last.</summary>
    [Fact]
    public void Merging_keeps_the_furthest_progress_and_the_newest_settings()
    {
        PlayerProfile desk = PlayerProfile.CreateDefault();
        desk.ActiveMission = 9;
        desk.Difficulty = 0;
        PlayerProfile browser = PlayerProfile.CreateDefault();
        browser.ActiveMission = 4;
        browser.SetMission("igi2", 5);
        browser.Difficulty = 2;

        ProfileDocument local = ProfileDocument.From(desk);
        ProfileDocument remote = ProfileDocument.From(browser);

        ProfileDocument mine = ProfileDocument.Merge(local, remote, localIsNewer: true);
        Assert.Equal(9, mine.Missions[PlayerProfile.RetailTitle]);
        Assert.Equal(5, mine.Missions["igi2"]);
        Assert.Equal(0, mine.Difficulty);

        ProfileDocument theirs = ProfileDocument.Merge(local, remote, localIsNewer: false);
        Assert.Equal(9, theirs.Missions[PlayerProfile.RetailTitle]);
        Assert.Equal(2, theirs.Difficulty);
        Assert.Equal(mine.Missions, theirs.Missions);
    }

    /// <summary>
    /// A finished campaign follows the player through their account: through the document and back, merged to the furthest
    /// either side finished on, and left out -- so the fingerprint is as before -- by a profile that has finished none.
    /// </summary>
    [Fact]
    public void A_finished_campaign_follows_the_player()
    {
        PlayerProfile desk = PlayerProfile.CreateDefault();
        desk.ActiveMission = 14;
        desk.SetFinale(PlayerProfile.RetailTitle, 14);
        PlayerProfile browser = PlayerProfile.CreateDefault();
        browser.ActiveMission = 6;
        browser.SetFinale(PlayerProfile.RetailTitle, 6);
        browser.SetFinale("igi2", 36);

        ProfileDocument? read = ProfileDocument.FromJson(ProfileDocument.From(desk).ToJson());
        Assert.NotNull(read);
        PlayerProfile other = PlayerProfile.CreateDefault();
        read.ApplyTo(other);
        Assert.Equal(14, other.FinaleOf(PlayerProfile.RetailTitle));

        ProfileDocument merged = ProfileDocument.Merge(ProfileDocument.From(desk), ProfileDocument.From(browser), localIsNewer: false);
        Assert.Equal(14, merged.Finales![PlayerProfile.RetailTitle]);
        Assert.Equal(36, merged.Finales["igi2"]);

        PlayerProfile fresh = PlayerProfile.CreateDefault();
        Assert.Null(ProfileDocument.From(fresh).Finales);
        Assert.DoesNotContain("finales", ProfileDocument.From(fresh).ToJson().GetRawText(), StringComparison.Ordinal);

        // An older build's document knows none, and takes nothing away.
        ProfileDocument? old = ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse(
            """{"schema":1,"missions":{"igi1":14},"difficulty":1,"invertMouse":false,"mouseSensitivity":0.4,"bindings":[]}""").RootElement);
        Assert.NotNull(old);
        Assert.Equal(14, ProfileDocument.Merge(ProfileDocument.From(desk), old, localIsNewer: false).Finales![PlayerProfile.RetailTitle]);
    }

    /// <summary>A document from a newer build, or not one at all, is not read.</summary>
    [Fact]
    public void A_document_this_build_cannot_read_is_refused()
    {
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("""{"schema":99,"missions":{},"bindings":[]}""").RootElement));
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("""{"name":"x"}""").RootElement));
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("[1,2]").RootElement));
    }

    /// <summary>
    /// Each mission's best follows the player: through the document and back, merged to the better of two, and absent -- not an
    /// error -- from a document an older build wrote.
    /// </summary>
    [Fact]
    public void Mission_records_follow_the_player_and_merge_to_the_better()
    {
        MissionRankRequirements requirements = MissionRankRequirements.FromRows(
            [900f, 5f, 3f, 20f, 10f], [600f, 2f, 1f, 50f, 30f], [300f, 0f, 0f, 80f, 60f]);
        TitleMissionRecord operative = MissionScoreRecord.Encode(new MissionScore(30 * 250, 0, 90, 100, 70) { Difficulty = 1, MissionId = 11 }, requirements);
        TitleMissionRecord bodyguard = MissionScoreRecord.Encode(new MissionScore(30 * 800, 4, 25, 0, 0) { Difficulty = 1, MissionId = 11 }, requirements);

        PlayerProfile desk = PlayerProfile.CreateDefault();
        desk.SetMissionRecord("igi2", 11, bodyguard);
        desk.SetMissionRecord("igi1", 1, bodyguard);
        PlayerProfile browser = PlayerProfile.CreateDefault();
        browser.SetMissionRecord("igi2", 11, operative);

        ProfileDocument? read = ProfileDocument.FromJson(ProfileDocument.From(browser).ToJson());
        Assert.NotNull(read);
        Assert.Equal(operative.Counts, read.MissionRecords![0].Counts);

        PlayerProfile merged = PlayerProfile.CreateDefault();
        ProfileDocument.Merge(ProfileDocument.From(desk), read, localIsNewer: true).ApplyTo(merged);
        Assert.Equal(operative.Counts, merged.MissionRecordOf("igi2", 11)!.Counts);
        Assert.Equal(bodyguard.Counts, merged.MissionRecordOf("igi1", 1)!.Counts);

        ProfileDocument? old = ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse(
            """{"schema":1,"missions":{"igi1":3},"difficulty":1,"invertMouse":false,"mouseSensitivity":0.4,"bindings":[]}""").RootElement);
        Assert.NotNull(old);
        Assert.Null(old.MissionRecords);
        Assert.Single(ProfileDocument.Merge(ProfileDocument.From(desk), old, localIsNewer: false).MissionRecords!, static r => r.Title == "igi2");
    }
}
