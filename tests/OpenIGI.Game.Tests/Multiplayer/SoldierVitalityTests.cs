using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A soldier's health as a co-op server's snapshots carry it.</summary>
public sealed class SoldierVitalityTests
{
    [Fact]
    public void Vitality_carries_the_share_of_a_soldiers_own_health_whatever_scaled_it()
    {
        // A soldier of 0.8 made one and a half times as tough for a squad, half its scaled health gone: 0.6 left, three quarters
        // of its own.
        byte sent = SoldierVitality.Encode(0.6f, 0.8f, foreseeable: true);
        Assert.Equal(48, sent);
        Assert.Equal(0.6f, SoldierVitality.Decode(sent, 0.8f)!.Value, 3);
        Assert.Null(SoldierVitality.Decode(SoldierVitality.Encode(0.6f, 0.8f, foreseeable: false), 0.8f));
        Assert.Null(SoldierVitality.Decode(SoldierVitality.Encode(100f, 0.8f, foreseeable: true), 0.8f));
    }

    /// <summary>
    /// What a soldier has left is never sent as less than it is.
    /// </summary>
    [Fact]
    public void A_soldiers_health_is_never_told_as_less_than_it_has()
    {
        for (float left = 0.001f; left < 2.5f; left += 0.0137f)
        {
            float told = SoldierVitality.Decode(SoldierVitality.Encode(left, 1.3f, foreseeable: true), 1.3f)!.Value;
            Assert.True(told >= left - 0.0001f && told < left + (1.3f * SoldierVitality.Step) + 0.0001f, $"{left} left was told as {told}");
        }

        Assert.Equal(0f, SoldierVitality.Decode(SoldierVitality.Encode(0f, 1.3f, foreseeable: true), 1.3f)!.Value);
    }
}
