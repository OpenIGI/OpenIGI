using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A host game's player seen as I.G.I. 2 sees its own (<c>HumanPlayer_UpdateVisibilityFactor</c>).</summary>
public sealed class Igi2HostPlayerVisibilityTests
{
    private static readonly Igi2VisibilityTuning MissionOne = new();

    [Fact]
    public void Half_the_light_to_the_gamma_less_crouched_more_moving_and_kept_between_the_bounds()
    {
        float still = MathF.Pow(0.5f, 0.8f);
        Assert.Equal(still, Igi2HostPlayerVisibility.Measure(lit: false, low: false, speed: 0.0, MissionOne), 5);
        Assert.Equal(still * 0.8f, Igi2HostPlayerVisibility.Measure(lit: false, low: true, speed: 0.0, MissionOne), 5);
        Assert.Equal(still + 0.10f, Igi2HostPlayerVisibility.Measure(lit: false, low: false, speed: 1.5, MissionOne), 5);
        Assert.Equal(still + 0.15f, Igi2HostPlayerVisibility.Measure(lit: false, low: false, speed: 4.9, MissionOne), 5);

        // A loud shot: the light does not count, and the maximum holds.
        Assert.Equal(0.95f, Igi2HostPlayerVisibility.Measure(lit: true, low: false, speed: 0.0, MissionOne));

        // In the dark, the minimum.
        Assert.Equal(0.2f, Igi2HostPlayerVisibility.Measure(lit: false, low: true, speed: 0.0, MissionOne with { Light = 0f }));
    }

    [Fact]
    public void The_player_s_own_loud_gunshot_lights_it_for_fifteen_ticks()
    {
        HumanPlayerTask player = new() { Position = new Vector3d(0, 0, 0) };
        HumanPlayerTask someoneElse = new();
        Igi2HostPlayerVisibility visibility = new(player, MissionOne);
        float still = MathF.Pow(0.5f, 0.8f);
        Assert.Equal(still, visibility.Update(), 5);

        // Another's shot, and a shot heard no further than I.G.I. 2's loud range, do not.
        visibility.SendEvent(new HumanAiEvent((int)AiEvent.Gunshot, 0x46, 0, 409600f, Vector3d.Zero, someoneElse, someoneElse, null));
        visibility.SendEvent(new HumanAiEvent((int)AiEvent.Gunshot, 0x46, 0, 4096f, Vector3d.Zero, player, player, null));
        Assert.Equal(still, visibility.Update(), 5);

        visibility.SendEvent(new HumanAiEvent((int)AiEvent.Gunshot, 0x46, 0, 409600f, Vector3d.Zero, player, player, null));
        for (int tick = 0; tick < 15; tick++)
        {
            Assert.Equal(0.95f, visibility.Update());
            Assert.Equal(0.95f, player.VisibilityFactor);
        }

        Assert.Equal(still, visibility.Update(), 5);
    }
}
