using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>How Project I.G.I.'s explosion throws its fragments (<c>0x47F200</c>).</summary>
public sealed class RetailExplosionFragmentTests
{
    /// <summary>A fragment leaves at its tip angle off the blast's axis, not square to it.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.4f)]
    [InlineData(1.2f)]
    [InlineData(1.5707964f)]
    public void AFragmentLeavesAtItsTipOffTheAxis(float off)
    {
        Vector3f axis = new Vector3f(0.3f, -0.2f, 0.6f).Normalized();
        for (float around = 0f; around <= MathF.PI; around += 0.25f)
        {
            Vector3f direction = RetailExplosionEffects.FragmentDirection(axis, around, off);
            Assert.Equal(1f, direction.Length, 4);
            Assert.Equal(MathF.Cos(off), Vector3f.Dot(direction, axis), 4);
        }
    }

    /// <summary>The turn about the axis covers half a turn, so a blast throws everything to one side of it.</summary>
    [Fact]
    public void EveryFragmentOfABlastFliesToTheSameSide()
    {
        Vector3f axis = new(0f, 0f, 1f);
        Vector3f level = new(0f, 1f, 0f);
        Vector3f side = Vector3f.Cross(level, axis);
        for (float around = 0.05f; around < MathF.PI; around += 0.1f)
        {
            Vector3f direction = RetailExplosionEffects.FragmentDirection(axis, around, 1f);
            Assert.True(Vector3f.Dot(direction, side) > 0f);
        }
    }
}
