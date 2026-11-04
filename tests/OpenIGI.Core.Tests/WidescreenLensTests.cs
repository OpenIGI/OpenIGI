using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

public sealed class WidescreenLensTests
{
    [Fact]
    public void FourByThreeIsUntouched()
    {
        Assert.Equal(1f, WidescreenLens.HorizontalTangent(1f, 4f / 3f), 6);
        Assert.Equal(0.75f, WidescreenLens.VerticalTangent(1f, 4f / 3f), 6);
    }

    [Fact]
    public void WideScreenKeepsTheVerticalAngleAndAddsAtTheSides()
    {
        Assert.Equal(4f / 3f, WidescreenLens.HorizontalTangent(1f, 16f / 9f), 5);
        Assert.Equal(0.75f, WidescreenLens.VerticalTangent(1f, 16f / 9f), 5);
        Assert.Equal(0.75f, WidescreenLens.VerticalTangent(1f, 21f / 9f), 5);
    }

    [Fact]
    public void NarrowScreenKeepsTheHorizontalAngleAndAddsAboveAndBelow()
    {
        Assert.Equal(1f, WidescreenLens.HorizontalTangent(1f, 5f / 4f), 6);
        Assert.Equal(0.8f, WidescreenLens.VerticalTangent(1f, 5f / 4f), 5);
    }

    [Theory]
    [InlineData(4f / 3f)]
    [InlineData(16f / 9f)]
    [InlineData(5f / 4f)]
    [InlineData(32f / 9f)]
    public void LevelsOfDetailReadTheAuthoredLens(float aspect)
    {
        const float authored = 0.2f;
        float tanX = WidescreenLens.HorizontalTangent(authored, aspect);
        float vertical = 2f * MathF.Atan(tanX / aspect);
        Matrix4f projection = Matrix4f.CreatePerspective(vertical, aspect, 0.1f, 1000f);

        Assert.Equal(authored, WidescreenLens.AuthoredTangent(projection), 4);
    }
}
