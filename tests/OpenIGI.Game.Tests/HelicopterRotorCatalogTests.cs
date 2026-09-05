using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class HelicopterRotorCatalogTests
{
    [Theory]
    [InlineData("711_01_1")]
    [InlineData("711_02_5")]
    [InlineData("700_02_1")]
    [InlineData("700_03_3")]
    [InlineData("700_04_4")]
    [InlineData("700_03_10")]
    public void RecognisesVerifiedRotorFamiliesAndLods(string modelName)
    {
        Assert.True(HelicopterRotorCatalog.IsKnownRotorModel(modelName));
    }

    [Theory]
    [InlineData("709_01_1")]
    [InlineData("700_01_1")]
    [InlineData("711_01_x")]
    [InlineData("999_01_1")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsBodiesUnknownModelsAndMalformedLods(string? modelName)
    {
        Assert.False(HelicopterRotorCatalog.IsKnownRotorModel(modelName));
    }
}
