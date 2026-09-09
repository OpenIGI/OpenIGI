using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class PoseUploadLatchTests
{
    [Fact]
    public void AStoppedPoseUploadsTwiceThenDoesNoMoreWork()
    {
        PoseUploadLatch latch = new();
        BonePose pose = Pose(0f);

        Assert.True(latch.ShouldUpload(pose));
        Assert.True(latch.ShouldUpload(pose));
        Assert.False(latch.ShouldUpload(pose));
        Assert.False(latch.ShouldUpload(pose));
    }

    [Fact]
    public void AChangedPoseRearmsTheTwoBufferSequence()
    {
        PoseUploadLatch latch = new();
        BonePose first = Pose(0f);
        BonePose second = Pose(1f);

        Assert.True(latch.ShouldUpload(first));
        Assert.True(latch.ShouldUpload(first));
        Assert.False(latch.ShouldUpload(first));

        Assert.True(latch.ShouldUpload(second));
        Assert.True(latch.ShouldUpload(second));
        Assert.False(latch.ShouldUpload(second));
    }

    private static BonePose Pose(float x)
    {
        BoneSkeleton skeleton = BoneSkeleton.Create([BoneSkeleton.NoParent], [default(Vector3f)]);
        return BonePose.Evaluate(skeleton, [Quaternion32.Identity], new Vector3f(x, 0f, 0f));
    }
}
