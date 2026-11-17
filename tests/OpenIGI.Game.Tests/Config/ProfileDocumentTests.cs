using OpenIGI.Game.Config;
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

    /// <summary>A document from a newer build, or not one at all, is not read.</summary>
    [Fact]
    public void A_document_this_build_cannot_read_is_refused()
    {
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("""{"schema":99,"missions":{},"bindings":[]}""").RootElement));
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("""{"name":"x"}""").RootElement));
        Assert.Null(ProfileDocument.FromJson(System.Text.Json.JsonDocument.Parse("[1,2]").RootElement));
    }
}
