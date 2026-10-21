using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The magic object types, as <c>magicobjconfig.qvm</c> defines them and <c>ModelObj_CreateAttachObject</c> reads them.</summary>
public sealed class Igi2MagicObjTypesTests
{
    /// <summary>A name the table does not hold is a model of its own; a shadow volume and a mark wear nothing; a later config replaces.</summary>
    [Fact]
    public void An_attach_point_wears_its_types_model()
    {
        Igi2MagicObjTypes types = new();
        types.Define(new Igi2MagicObjType("100_04_1", "104_04_1", "Glass", []));
        types.Define(new Igi2MagicObjType("000_02_1", "000_02_1", Igi2MagicObjTypes.ShadowVolumeType, []));
        types.Define(new Igi2MagicObjType("weapon", Igi2MagicObjTypes.NoModel, "WeaponMagicObj", []));
        types.Define(new Igi2MagicObjType(string.Empty, "ignored", "Glass", []));

        Assert.Equal("104_04_1", types.ModelOf("100_04_1"));
        Assert.Null(types.ModelOf("000_02_1"));
        Assert.Null(types.ModelOf("weapon"));
        Assert.Equal("123_45_1", types.ModelOf("123_45_1"));
        Assert.Equal(3, types.Count);

        types.Define(new Igi2MagicObjType("100_04_1", "100_04_1", "Ladder", []));
        Assert.Equal("100_04_1", types.ModelOf("100_04_1"));
    }

    /// <summary>The session reads the shipped table at start-up, its task types resolved by name, the tag items kept as settings.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_shipped_table_loads()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(root, "MISSIONS", "location1", "level1", "objects.qvm"), loadTables: true);
        Igi2MagicObjTypes types = harness.Session.Services.Require<Igi2MagicObjTypes>();

        // 176 configs, three of them naming a type an earlier one did: the later replaces it.
        Assert.Equal(173, types.Count);
        Assert.Equal(Igi2MagicObjTypes.ShadowVolumeType, types.Find("000_02_1")?.TaskType);
        Assert.Null(types.ModelOf("100_04_1"));
        Assert.Equal("Glass", types.Find("231_20_1")?.TaskType);
        Assert.Equal("231_20_1", types.ModelOf("231_20_1"));
        Assert.Equal("DeathZone", types.Find("killbox")?.TaskType);
        Assert.Null(types.ModelOf("primary"));
        Igi2MagicObjType? door = types.Find("610_02_1");
        Assert.Equal("CarDoor", door?.TaskType);
        Assert.Equal(3, door?.Settings.Count);
    }
}
