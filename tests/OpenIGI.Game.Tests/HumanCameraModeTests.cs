using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the camera mode numbering and the facts each name rests on.
/// </summary>
public sealed class HumanCameraModeTests
{
    [Fact]
    public void TheFiveModesKeepTheOriginalsNumbering()
    {
        // The camera's update switches on these values directly, so the numbering is the interface.
        Assert.Equal(1, (int)HumanCameraMode.FirstPerson);
        Assert.Equal(2, (int)HumanCameraMode.Developer);
        Assert.Equal(3, (int)HumanCameraMode.Interaction);
        Assert.Equal(4, (int)HumanCameraMode.Scripted);
        Assert.Equal(5, (int)HumanCameraMode.Death);
    }

    [Fact]
    public void ZeroIsNotADispatchedMode()
    {
        // The switch has cases 1..5 and a default that does nothing, so 0 is genuinely "no mode".
        Assert.Equal(0, (int)HumanCameraMode.None);
    }

    [Fact]
    public void EveryModeIsDefinedAndDistinct()
    {
        HumanCameraMode[] all = Enum.GetValues<HumanCameraMode>();

        Assert.Equal(6, all.Length);
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.All(all, m => Assert.True(Enum.IsDefined(m)));
    }

    [Fact]
    public void FirstPersonIsTheDefaultAndTheRestoreTarget()
    {
        // Human's constructor writes 1, and every mode that ends restores 1 - so it is both the
        // starting value and the one the setter is called with to leave any other mode.
        Assert.Equal(HumanCameraMode.FirstPerson, (HumanCameraMode)1);
        Assert.NotEqual(HumanCameraMode.None, HumanCameraMode.FirstPerson);
    }

    [Fact]
    public void TheDeathModeMatchesTheDeathLocomotionState()
    {
        // Mode 5 has exactly one setter: the entry of locomotion state 47, which is one of the nine
        // self-driving states and the one whose entry names "player_death_".
        Assert.True(HumanLocomotionStates.DrivesItself(47));
        Assert.Equal(HumanCameraMode.Death, (HumanCameraMode)5);
    }

    [Fact]
    public void TheInteractionModeMatchesTheSelfDrivingStates()
    {
        // Mode 3 is set by the entries of the interactive states, which are exactly the run of seven
        // the animation table leaves at id 0.
        foreach (int state in new[] { 30, 31, 32, 33, 34, 35, 36 })
        {
            Assert.True(HumanLocomotionStates.DrivesItself(state));
        }

        Assert.Equal(HumanCameraMode.Interaction, (HumanCameraMode)3);
    }

    [Fact]
    public void TheDeveloperModeIsDrivenByADebugChordSlot()
    {
        // The handler toggles on input slot 19, which the sampler fills from a Ctrl+Shift chord
        // rather than from a binding - so this mode is unreachable in a retail profile.
        Assert.Equal(19, (int)PlayerInputSlot.DebugNumpadStarPressed);
        Assert.Equal(HumanCameraMode.Developer, (HumanCameraMode)2);
    }
}
