using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Cross-checks the locomotion state table against the retail animation sets.
/// </summary>
/// <remarks>
/// The two halves were read independently — the state table out of <c>0x538870</c>, the animation ids
/// out of the <c>.iff</c> files — so every id lining up is a real check on both.
/// </remarks>
public sealed class RetailLocomotionStateTests
{
    /// <summary>The skeleton <c>HumanPlayer</c>'s constructor asks for.</summary>
    private const string PlayerSkeleton = "000";

    [Fact]
    public void ThereAreFortyNineStates()
    {
        Assert.Equal(49, HumanLocomotionStates.Count);
        Assert.Equal(49, HumanLocomotionStates.Bindings.Count);
    }

    [Fact]
    public void TheSelfDrivingStatesAreTheScriptedOnes()
    {
        // Animation id 0 marks a state that names its clip by string instead. They are not scattered:
        // 30..36 are a solid run, which is the interactive set - picklock, type on a computer, cut a
        // wire - plus 46 and 47, the death states.
        List<int> selfDriving = [];
        for (int state = 0; state < HumanLocomotionStates.Count; state++)
        {
            if (HumanLocomotionStates.DrivesItself(state))
            {
                selfDriving.Add(state);
            }
        }

        Assert.Equal([30, 31, 32, 33, 34, 35, 36, 46, 47], selfDriving);
    }

    [Fact]
    public void TheDeveloperStateIsTheLastOne()
    {
        // State 48 is entered by the Ctrl+Shift+F chord and is the last entry in the table, which is
        // what fixes the table's length against the tables that follow it in .rdata.
        Assert.Equal(HumanLocomotionStates.Count - 1, HumanLocomotionStates.DeveloperState);
        Assert.False(HumanLocomotionStates.DrivesItself(HumanLocomotionStates.DeveloperState));
    }

    [Fact]
    public void ModesAreOnlyEverZeroOneOrTwo() =>
        Assert.All(HumanLocomotionStates.Bindings, b => Assert.InRange(b.Mode, 0, 2));

    [RequiresGameDataFact]
    public void EveryAnimationTheStatesNameExistsInThePlayersSet()
    {
        // The real cross-check. HumanPlayer's constructor passes skeleton 0, so every non-zero id in
        // the state table has to be a defined animation in 000.iff.
        BoneAnimationSet set = LoadSet(PlayerSkeleton);
        int checkedIds = 0;

        for (int state = 0; state < HumanLocomotionStates.Count; state++)
        {
            HumanLocomotionState binding = HumanLocomotionStates.For(state);
            if (binding.AnimationId == HumanLocomotionStates.NoAnimation)
            {
                continue;
            }

            Assert.True(
                set.TryGet(binding.AnimationId, out _),
                $"state {state} names animation {binding.AnimationId}, which {PlayerSkeleton}.iff does not define");
            checkedIds++;
        }

        Assert.Equal(40, checkedIds);
    }

    [RequiresGameDataFact]
    public void TheStatesNeverNameTheReservedIdsZeroOrOne()
    {
        // Animation ids start at 2 in every retail set, and the state table respects that: the only
        // value below 2 it uses is 0, and that means "no animation" rather than "animation 0".
        BoneAnimationSet set = LoadSet(PlayerSkeleton);

        Assert.False(set.TryGet(0, out _));
        Assert.False(set.TryGet(1, out _));
        Assert.DoesNotContain(HumanLocomotionStates.Bindings, b => b.AnimationId == 1);
    }

    [RequiresGameDataFact]
    public void TheStatesUseOnlyAFractionOfTheAvailableAnimations()
    {
        // 000.iff defines 122 clips; locomotion names 35 distinct ones. The rest belong to the AI,
        // weapons and cutscenes - so a state table that covered them all would be the wrong reading.
        BoneAnimationSet set = LoadSet(PlayerSkeleton);
        HashSet<int> named = [];
        foreach (HumanLocomotionState binding in HumanLocomotionStates.Bindings)
        {
            if (binding.AnimationId != HumanLocomotionStates.NoAnimation)
            {
                named.Add(binding.AnimationId);
            }
        }

        Assert.Equal(35, named.Count);
        Assert.Equal(122, set.Animations.Count);
        Assert.True(named.Count < set.Animations.Count);
    }

    private static BoneAnimationSet LoadSet(string name)
    {
        string path = Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff");
        return BoneAnimationSet.Read(File.ReadAllBytes(path));
    }
}
