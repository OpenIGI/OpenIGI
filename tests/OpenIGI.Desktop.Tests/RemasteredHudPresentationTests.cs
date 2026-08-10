using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks down the remaster HUD's state decisions without requiring a graphics device.</summary>
public sealed class RemasteredHudPresentationTests
{
    [Theory]
    [InlineData(1f, 91, 210, 141)]
    [InlineData(0.55f, 240, 176, 84)]
    [InlineData(0.28f, 235, 92, 82)]
    [InlineData(0f, 235, 92, 82)]
    public void HealthColourUsesClearHealthBands(float fraction, byte red, byte green, byte blue)
    {
        Bgra32 colour = RemasteredHudPresentation.HealthColour(fraction);

        Assert.Equal(red, colour.R);
        Assert.Equal(green, colour.G);
        Assert.Equal(blue, colour.B);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.5707964f, 90f)]
    [InlineData(-1.5707964f, 270f)]
    [InlineData(6.2831855f, 0f)]
    public void HeadingDegreesWrapsThePlayerYawOntoTheCompass(float yawRadians, float expected)
        => Assert.Equal(expected, RemasteredHudPresentation.HeadingDegrees(yawRadians), 3);

    [Fact]
    public void ObjectivesPreferTheFirstIncomplete()
    {
        MissionObjective[] objectives =
        [
            Objective("finished", MissionObjectiveState.Complete),
            Objective("primary", MissionObjectiveState.Incomplete),
            Objective("failed", MissionObjectiveState.Failed),
        ];

        MissionObjective? primary = RemasteredHudPresentation.PrimaryObjective(objectives);

        Assert.NotNull(primary);
        Assert.Equal("primary", primary.Text);
    }

    [Theory]
    [InlineData("a_door.spr", "OPEN DOOR")]
    [InlineData("action_comput.spr", "USE COMPUTER")]
    [InlineData("unknown.spr", "INTERACT")]
    [InlineData(null, "")]
    public void InteractionLabelsUseKnownActionsAndAUsefulFallback(string? sprite, string expected)
        => Assert.Equal(expected, RemasteredHudPresentation.InteractionLabel(sprite));

    [Theory]
    [InlineData(HumanWeaponState.Ready, "")]
    [InlineData(HumanWeaponState.Reloading, "RELOADING")]
    [InlineData(HumanWeaponState.ChangingWeapon, "SWITCHING")]
    [InlineData(HumanWeaponState.DryFire, "EMPTY")]
    public void WeaponStateLabelsOnlyAppearForRelevantTransitions(HumanWeaponState state, string expected)
        => Assert.Equal(expected, RemasteredHudPresentation.WeaponStateLabel(state));

    [Fact]
    public void DedicatedSettingSelectsEitherHudWithoutDependingOnTheMenuStyle()
    {
        Assert.True(RemasteredHudPresentation.UsesRemasteredHud(remasteredHudEnabled: true));
        Assert.False(RemasteredHudPresentation.UsesRemasteredHud(remasteredHudEnabled: false));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TacticalCardsOnlyBelongToTheRemasterHud(
        bool remasteredHudEnabled,
        bool tacticalMessagesEnabled,
        bool expected)
        => Assert.Equal(
            expected,
            RemasteredHudPresentation.UsesTacticalMessages(remasteredHudEnabled, tacticalMessagesEnabled));

    private static MissionObjective Objective(string text, MissionObjectiveState state) => new(
        TextResource: text,
        LinkTaskId: 0,
        Position: null,
        State: state)
    {
        Text = text,
    };
}
