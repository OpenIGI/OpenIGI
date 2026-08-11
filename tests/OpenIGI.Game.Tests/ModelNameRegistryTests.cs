using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class ModelNameRegistryTests
{
    [Theory]
    [InlineData("001011", "001_01_1")]
    [InlineData("012011", "012_01_1")]
    [InlineData("437011", "437_01_1")]
    [InlineData("001_01_1", "001_01_1")]
    [InlineData("415_01_1", "415_01_1")]
    [InlineData("building_1", "building_1")]
    public void FormatModelId_NormalizesRawSixDigitModelIds(string raw, string expected)
    {
        string actual = ModelNameRegistry.FormatModelId(raw);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "Fast")]
    public void TryGetModelName_ReturnsModelNameFromCatalogFixture()
    {
        bool found = ModelNameRegistry.TryGetModelName("001_01_1", out string name);
        Assert.True(found);
        Assert.Equal("AITYPE_SNIPER", name);
    }

    [Fact]
    [Trait("Category", "Fast")]
    public void DescribeModel_FormatsModelIdAndAppendsModelName()
    {
        string description = ModelNameRegistry.DescribeModel("001011");
        Assert.Equal("001_01_1 (AITYPE_SNIPER)", description);
    }
}
