using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
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
    public void NearestContactRangeIgnoresInvalidAndZeroContacts()
    {
        IReadOnlyList<EnemyOverlayBox> targets =
        [
            new(1, 0f, 0f, 1f, 1f, 0f, 1f),
            new(2, 0f, 0f, 1f, 1f, float.NaN, 1f),
            new(3, 0f, 0f, 1f, 1f, 18f, 1f),
            new(4, 0f, 0f, 1f, 1f, 7.5f, 1f),
        ];

        Assert.Equal(7.5f, RemasteredHudPresentation.NearestContactRange(targets));
        Assert.Null(RemasteredHudPresentation.NearestContactRange([]));
    }

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
    public void BinocularsFollowTheTacticalComputerOnlyWhenTheirRendererIsReady(
        bool tacticalComputerEnabled,
        bool opticsRendererReady,
        bool expected) =>
        Assert.Equal(expected, RemasteredHudPresentation.UsesRemasteredOptics(tacticalComputerEnabled, opticsRendererReady));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    public void RetailAndEnhancedBinocularLayersAreMutuallyExclusive(
        bool remasteredHudEnabled,
        bool binocularsOpen,
        bool remasteredOpticsEnabled,
        bool expectedRetailLayer)
        => Assert.Equal(
            expectedRetailLayer,
            RemasteredHudPresentation.UsesRetailWeaponHud(
                remasteredHudEnabled,
                binocularsOpen,
                remasteredOpticsEnabled));

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

    [Fact]
    public void DragunovScopeReplacesTheOrdinaryCrosshair()
    {
        var crosshairWeapon = new WeaponType { SightDisplay = SightDisplayType.Crosshair };

        Assert.True(RemasteredHudPresentation.ShowsCrosshair(crosshairWeapon, sniperScoped: false));
        Assert.False(RemasteredHudPresentation.ShowsCrosshair(crosshairWeapon, sniperScoped: true));
    }

    private static MissionObjective Objective(string text, MissionObjectiveState state) => new(
        TextResource: text,
        LinkTaskId: 0,
        Position: null,
        State: state)
    {
        Text = text,
    };
}
