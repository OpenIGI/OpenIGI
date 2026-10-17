using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.Config;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Game.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks down the world-anchored interaction marker's wording, method and geometry.</summary>
public sealed class InteractionPromptPresentationTests
{
    [Fact]
    public void TheWorldPromptBelongsToTheRemasterHud()
    {
        Assert.True(InteractionPromptPresentation.UsesWorldPrompt(
            remasteredHudEnabled: true, interactionPromptEnabled: true));
        Assert.False(InteractionPromptPresentation.UsesWorldPrompt(
            remasteredHudEnabled: false, interactionPromptEnabled: true));
        Assert.False(InteractionPromptPresentation.UsesWorldPrompt(
            remasteredHudEnabled: true, interactionPromptEnabled: false));
    }

    [Theory]
    [InlineData(InteractionActivateType.Cabinet, 60, false, InteractionPromptMethod.Hold)]
    [InlineData(InteractionActivateType.Terminal, 120, false, InteractionPromptMethod.Hold)]
    [InlineData(InteractionActivateType.Generic, 45, false, InteractionPromptMethod.Hold)]
    [InlineData(InteractionActivateType.PlaceExplosive, 90, false, InteractionPromptMethod.Hold)]
    [InlineData(InteractionActivateType.Door, 150, true, InteractionPromptMethod.Hold)]
    [InlineData(InteractionActivateType.Door, 150, false, InteractionPromptMethod.Press)]
    [InlineData(InteractionActivateType.Switch, 0, false, InteractionPromptMethod.Press)]
    [InlineData(InteractionActivateType.Ladder, 0, false, InteractionPromptMethod.Press)]
    [InlineData(InteractionActivateType.Wire, 0, false, InteractionPromptMethod.Press)]
    public void OnlyTheStatesTheRetailTickCancelsOnReleaseAreHeld(
        InteractionActivateType type,
        int durationTicks,
        bool locked,
        InteractionPromptMethod expected)
    {
        InteractionPromptTarget target = Target(type, durationTicks: durationTicks, locked: locked);

        Assert.Equal(expected, InteractionPromptPresentation.Method(target));
    }

    [Fact]
    public void AFenceIsCommittedRatherThanHeldEvenThoughItRunsForSeconds()
    {
        InteractionPromptTarget fence = Target(InteractionActivateType.Fence, durationTicks: 180);

        Assert.Equal(InteractionPromptMethod.Press, InteractionPromptPresentation.Method(fence));
    }

    [Theory]
    [InlineData(false, false, "Open Door")]
    [InlineData(false, true, "Close Door")]
    [InlineData(true, false, "Pick Lock")]
    public void ADoorIsNamedByWhatUsingItWouldDo(bool locked, bool open, string expected)
    {
        InteractionPromptTarget door = Target(
            InteractionActivateType.Door, sprite: "a_door.spr", locked: locked, doorOpen: open);

        Assert.Equal(expected, InteractionPromptPresentation.Title(door));
    }

    [Theory]
    [InlineData("a_ladder.spr", "Climb Ladder")]
    [InlineData("a_wire.spr", "Ride Zipline")]
    [InlineData("a_fence.spr", "Climb Fence")]
    [InlineData("action_pressbutton.spr", "Use Switch")]
    [InlineData("action_comput.spr", "Use Computer")]
    [InlineData("action_searchcab.spr", "Search Cabinet")]
    [InlineData("stationary.spr", "Mount Gun")]
    [InlineData("a_vehicle.spr", "Drive")]
    public void TheTasksOwnActionSpriteNamesTheAction(string sprite, string expected)
    {
        InteractionPromptTarget target = Target(InteractionActivateType.Generic, sprite: sprite);

        Assert.Equal(expected, InteractionPromptPresentation.Title(target));
    }

    [Theory]
    [InlineData(InteractionActivateType.Cabinet, "Search Cabinet")]
    [InlineData(InteractionActivateType.Terminal, "Use Computer")]
    [InlineData(InteractionActivateType.StationaryGun, "Mount Gun")]
    [InlineData(InteractionActivateType.PlaceExplosive, "Plant Explosive")]
    [InlineData(InteractionActivateType.Generic, "Interact")]
    public void AnAuthoredSpriteNobodyKnowsFallsBackToTheActivationType(
        InteractionActivateType type,
        string expected)
    {
        InteractionPromptTarget target = Target(type, sprite: "some_level_sprite.spr");

        Assert.Equal(expected, InteractionPromptPresentation.Title(target));
    }

    [Fact]
    public void ThePressActionsCarryNoQualifier()
    {
        InteractionPromptTarget ladder = Target(InteractionActivateType.Ladder, sprite: "a_ladder.spr");

        Assert.Equal(string.Empty, InteractionPromptPresentation.Detail(ladder));
    }

    [Fact]
    public void AHeldActionSaysSoAndHowLongItTakes()
    {
        InteractionPromptTarget terminal = Target(InteractionActivateType.Terminal, durationTicks: 90);

        Assert.Equal("Hold 3s", InteractionPromptPresentation.Detail(terminal));
    }

    [Fact]
    public void AHoldTooShortToCountSecondsJustSaysHold()
    {
        InteractionPromptTarget cabinet = Target(InteractionActivateType.Cabinet, durationTicks: 12);

        Assert.Equal("Hold", InteractionPromptPresentation.Detail(cabinet));
    }

    [Fact]
    public void ALockedDoorIsMarkedLockedBeforeItsHold()
    {
        InteractionPromptTarget door = Target(
            InteractionActivateType.Door, sprite: "a_door.spr", durationTicks: 150, locked: true);

        Assert.Equal("Locked · Hold 5s", InteractionPromptPresentation.Detail(door));
    }

    [Fact]
    public void TheRingOnlyReadsWhileATimedStateIsRunning()
    {
        Assert.Null(InteractionPromptPresentation.RingProgress(Target(InteractionActivateType.Terminal)));
        Assert.Equal(
            0.5f,
            InteractionPromptPresentation.RingProgress(
                Target(InteractionActivateType.Terminal, progress: 0.5f)));
        Assert.Equal(
            1f,
            InteractionPromptPresentation.RingProgress(
                Target(InteractionActivateType.Terminal, progress: 4f)));
    }

    [Theory]
    [InlineData("E", "E")]
    [InlineData("F", "F")]
    [InlineData("Space", "SPACE")]
    [InlineData("Return", "ENTER")]
    [InlineData("LeftControl", "CTRL")]
    [InlineData("Numpad0", "N0")]
    [InlineData("Mouse Button 2", "M2")]
    [InlineData("Backspace", "BKSP")]
    [InlineData("SomeAbsurdlyLongKeyName", "SOMEA")]
    [InlineData(null, "USE")]
    [InlineData("   ", "USE")]
    public void TheBadgeShowsTheControlTheGameIsActuallyBoundTo(string? bound, string expected)
        => Assert.Equal(expected, InteractionPromptPresentation.BadgeLabel(bound));

    [Fact]
    public void TheDefaultProfileBindsUseToE()
    {
        string? bound = PlayerProfileInputBindings.BoundKeyName(profile: null, "Activate");

        Assert.Equal("E", InteractionPromptPresentation.BadgeLabel(bound));
        Assert.Null(PlayerProfileInputBindings.BoundKeyName(profile: null, "NotAnAction"));
    }

    [Theory]
    [InlineData("E", false)]
    [InlineData("M2", false)]
    [InlineData("USE", true)]
    [InlineData("SPACE", true)]
    public void OnlyALabelTooWideForACircleGetsTheKeyCapShape(string label, bool expected)
        => Assert.Equal(expected, InteractionPromptPresentation.IsWideBadge(label));

    private static InteractionPromptTarget Target(
        InteractionActivateType type,
        string? sprite = null,
        int durationTicks = 0,
        bool locked = false,
        bool doorOpen = false,
        float? progress = null)
        => new(
            Id: 1,
            type,
            sprite,
            AnchorUnits: new Vector3d(4096.0, 8192.0, 0.0),
            durationTicks,
            locked,
            doorOpen,
            progress);
}
