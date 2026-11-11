using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s content read for its soldiers in another game's level opens the packs its bodies are in.</summary>
public sealed class Igi1GuestContentTests
{
    // Bodies only one level's pack holds: level 1's and level 3's.
    private const string LevelOneBody = "350_01_1";
    private const string LevelThreeBody = "231_01_1";

    [RequiresGameDataFact]
    public void The_common_pack_and_each_level_pack_holding_a_body_asked_for_are_opened_and_resolve_it()
    {
        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), [LevelOneBody, LevelThreeBody]);

        Assert.Equal(["location0", "level1", "level3"], content.Mission.PacksInLoadOrder.Select(static pack => pack.Name));
        Assert.Same(content.Mission.PacksInLoadOrder[^1], content.Mission.LevelPack);
        Assert.True(content.Mission.TryGetModel(LevelOneBody, out MefModel? first));
        Assert.NotNull(first);
        Assert.True(content.Mission.TryGetModel(LevelThreeBody, out MefModel? second));
        Assert.NotNull(second);
    }

    [RequiresGameDataFact]
    public void Its_weapons_ammunition_and_sounds_are_the_games_own()
    {
        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), []);

        Assert.Equal(["location0"], content.Mission.PacksInLoadOrder.Select(static pack => pack.Name));
        Assert.True(content.Arsenal.Weapons.TryGetByScriptId("WEAPON_ID_AK47", out _));
        Assert.NotEmpty(content.Arsenal.Ammunition.Types);
        Assert.True(content.Arsenal.MaterialCount > 0);
        Assert.True(content.SoundDefinitions.Count > 0);
    }
}
