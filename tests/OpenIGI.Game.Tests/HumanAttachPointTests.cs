using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the derivation of the attachment bones from the palette offsets the original's own handlers use.
/// </summary>
/// <remarks>
/// These four offsets are the whole evidence that the held weapon hangs off a bone rather than off a
/// tuned transform, so the arithmetic that recovers a bone index from each of them is worth a test of
/// its own — an offset that did not factor exactly would mean the reading is wrong.
/// </remarks>
public sealed class HumanAttachPointTests
{
    [Theory]
    [InlineData(420, HumanAttachPoints.FirstPersonWeapon, HumanAttachPoints.PositionOffset)]
    [InlineData(432, HumanAttachPoints.FirstPersonWeapon, HumanAttachPoints.RotationOffset)]
    [InlineData(2928, HumanAttachPoints.ThirdPersonWeapon, HumanAttachPoints.PositionOffset)]
    [InlineData(2940, HumanAttachPoints.ThirdPersonWeapon, HumanAttachPoints.RotationOffset)]
    [InlineData(6888, HumanAttachPoints.FirstPersonSecondary, HumanAttachPoints.PositionOffset)]
    [InlineData(6900, HumanAttachPoints.FirstPersonSecondary, HumanAttachPoints.RotationOffset)]
    [InlineData(7284, HumanAttachPoints.FirstPersonTertiary, HumanAttachPoints.PositionOffset)]
    [InlineData(7296, HumanAttachPoints.FirstPersonTertiary, HumanAttachPoints.RotationOffset)]
    public void EveryPaletteOffsetTheOriginalIndexesFactorsIntoABoneAndAField(
        int offset,
        int expectedBone,
        int expectedField)
    {
        int bone = HumanAttachPoints.BoneAt(offset, out int field);

        Assert.Equal(expectedBone, bone);
        Assert.Equal(expectedField, field);
    }

    [Fact]
    public void TheViewRigBonesAreInRangeForFiftySixBonesAndTheBodyBoneForThirtyThree()
    {
        // 52 and 55 exist only in the 56-bone set, which is an independent check that the first-person
        // handlers read the view's palette rather than the body's.
        Assert.InRange(HumanAttachPoints.FirstPersonWeapon, 0, 55);
        Assert.InRange(HumanAttachPoints.FirstPersonSecondary, 33, 55);
        Assert.InRange(HumanAttachPoints.FirstPersonTertiary, 33, 55);
        Assert.InRange(HumanAttachPoints.ThirdPersonWeapon, 0, 32);
    }

    [Fact]
    public void AnOffsetThatDoesNotLandOnAKnownFieldIsRejected()
    {
        Assert.Equal(-1, HumanAttachPoints.BoneAt(421, out _));
        Assert.Equal(-1, HumanAttachPoints.BoneAt(-24, out _));
    }
}
