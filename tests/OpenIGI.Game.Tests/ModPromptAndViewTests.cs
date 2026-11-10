using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A mod's prompt, what the use key does with it, and a mod's taking of the view.</summary>
public sealed class ModPromptAndViewTests
{
    private static readonly PromptOffer Nothing = new("Nothing", default, () => { });

    [Fact]
    public void The_first_source_with_a_prompt_is_offered()
    {
        MissionWorld mission = new();
        PromptOffer second = new("Second", default, () => { });
        mission.PromptOffers.Add(() => null);
        mission.PromptOffers.Add(() => second);
        mission.PromptOffers.Add(() => Nothing);

        Assert.Same(second, Igi1MissionTick.FindModPrompt(mission, new PlayerCamera(), NullLogger.Instance));
        Assert.Same(second, mission.OfferedPrompt);
    }

    [Fact]
    public void A_body_a_mod_has_taken_is_offered_nothing()
    {
        MissionWorld mission = new();
        mission.PromptOffers.Add(() => Nothing);

        Assert.Null(Igi1MissionTick.FindModPrompt(mission, new PlayerCamera { ControlTaken = true }, NullLogger.Instance));
        Assert.Null(mission.OfferedPrompt);
    }

    [Fact]
    public void A_source_that_throws_is_logged_and_the_next_is_asked()
    {
        MissionWorld mission = new();
        MemorySink log = new();
        mission.PromptOffers.Add(() => throw new InvalidOperationException("broken"));
        mission.PromptOffers.Add(() => Nothing);

        Assert.Same(Nothing, Igi1MissionTick.FindModPrompt(mission, new PlayerCamera(), log));
        Assert.Equal("A mod's prompt source failed: broken", Assert.Single(log.Snapshot()).Message);
    }

    [Fact]
    public void The_use_key_acts_on_the_prompt_as_it_goes_down_and_not_while_it_is_held()
    {
        int used = 0;
        PromptOffer door = new("Door", default, () => used++);
        PlayerCamera player = new();
        PlayerInputFrame up = default;
        PlayerInputFrame down = default;
        down[(int)PlayerInputSlot.Activate] = 1f;

        Igi1MissionTick.ActOnModPrompt(door, player, up, NullLogger.Instance);
        Assert.Equal(0, used);

        Igi1MissionTick.ActOnModPrompt(door, player, down, NullLogger.Instance);
        Assert.Equal(1, used);

        // The player's update has now run on the key down: held, it does not act again.
        player.TickFrame(down, null);
        Igi1MissionTick.ActOnModPrompt(door, player, down, NullLogger.Instance);
        Assert.Equal(1, used);
    }

    [Fact]
    public void A_prompt_that_throws_is_logged()
    {
        MemorySink log = new();
        PromptOffer broken = new("Broken", default, () => throw new InvalidOperationException("broken"));
        PlayerInputFrame down = default;
        down[(int)PlayerInputSlot.Activate] = 1f;

        Igi1MissionTick.ActOnModPrompt(broken, new PlayerCamera(), down, log);

        Assert.Equal("A mod's prompt failed: broken", Assert.Single(log.Snapshot()).Message);
    }

    [Fact]
    public void A_taken_view_is_seen_through_the_mods_eye_at_the_frames_blend()
    {
        MissionWorld mission = new();
        float asked = -1f;
        Camera3D eye = default;
        mission.ViewTakeover = new ViewTakeover(blend =>
        {
            asked = blend;
            return eye;
        });

        Assert.Equal(eye, Igi1Viewpoint.TakenEye(mission, 0.25f, NullLogger.Instance));
        Assert.Equal(0.25f, asked);
        Assert.Null(Igi1Viewpoint.TakenEye(new MissionWorld(), 0.25f, NullLogger.Instance));
    }

    [Fact]
    public void An_eye_that_throws_loses_the_view_to_the_players_own()
    {
        MissionWorld mission = new() { ViewTakeover = new ViewTakeover(_ => throw new InvalidOperationException("broken")) };
        MemorySink log = new();

        Assert.Null(Igi1Viewpoint.TakenEye(mission, 1f, log));
        Assert.Null(mission.ViewTakeover);
        Assert.Equal("A view takeover's eye failed: broken", Assert.Single(log.Snapshot()).Message);
    }
}
