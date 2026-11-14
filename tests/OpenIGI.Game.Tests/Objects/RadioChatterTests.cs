using OpenIGI.Core.Numerics;
using OpenIGI.Game.Objects;
using Xunit;

namespace OpenIGI.Game.Tests.Objects;

/// <summary>The radio's chatter both games run (Project I.G.I. <c>0x43AF90</c>, I.G.I. 2 <c>Radio_RunHandler</c>).</summary>
public sealed class RadioChatterTests
{
    [Fact]
    public void A_radio_is_off_once_blown_up_whatever_its_expression_says()
    {
        Assert.False(RadioChatter.Switched(current: true, exploded: true, expression: true));
        Assert.True(RadioChatter.Switched(current: false, exploded: false, expression: true));
        Assert.False(RadioChatter.Switched(current: true, exploded: false, expression: false));
    }

    [Fact]
    public void A_radio_with_no_expression_stays_as_a_script_left_it()
    {
        Assert.True(RadioChatter.Switched(current: true, exploded: false, expression: null));
        Assert.False(RadioChatter.Switched(current: false, exploded: false, expression: null));
    }

    [Fact]
    public void Coming_on_it_starts_a_sound_at_once_and_draws_the_slot_then_the_gap()
    {
        IgiRandom drawn = IgiRandom.Seeded();
        IgiRandom expected = IgiRandom.Seeded();
        RadioChatter radio = new();

        RadioStep step = radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, drawn);

        int slot = (int)expected.NextBounded(RadioChatter.SoundSlots);
        int gap = (int)expected.NextBounded(RadioChatter.SoundGapTicks) + RadioChatter.SoundGapTicks;
        Assert.Equal(slot, step.Play);
        Assert.Equal(gap, radio.Countdown);
        Assert.InRange(radio.Countdown, RadioChatter.SoundGapTicks, (2 * RadioChatter.SoundGapTicks) - 1);
        Assert.False(step.Shutdown);
    }

    [Fact]
    public void While_its_sound_plays_it_neither_counts_nor_draws()
    {
        IgiRandom random = IgiRandom.Seeded();
        RadioChatter radio = new();
        radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, random);
        int countdown = radio.Countdown;
        uint next = Peek(random);

        RadioStep step = radio.Step(isOn: true, holdsVoice: true, voicePlaying: true, random);

        Assert.Equal(default, step);
        Assert.Equal(countdown, radio.Countdown);
        Assert.Equal(next, Peek(random));
    }

    [Fact]
    public void Once_its_sound_ends_it_lets_go_of_it_and_waits_out_the_gap_before_the_next()
    {
        IgiRandom random = IgiRandom.Seeded();
        RadioChatter radio = new();
        radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, random);
        int gap = radio.Countdown;

        RadioStep ended = radio.Step(isOn: true, holdsVoice: true, voicePlaying: false, random);
        Assert.True(ended.Drop);
        Assert.Null(ended.Play);
        Assert.Equal(gap - 1, radio.Countdown);

        for (int i = 1; i < gap; i++)
        {
            Assert.Null(radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, random).Play);
        }

        Assert.Equal(0, radio.Countdown);
        Assert.NotNull(radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, random).Play);
    }

    [Fact]
    public void Going_off_it_cuts_the_sound_it_holds_and_plays_its_shutdown_once()
    {
        IgiRandom random = IgiRandom.Seeded();
        RadioChatter radio = new();
        radio.Step(isOn: true, holdsVoice: false, voicePlaying: false, random);

        RadioStep off = radio.Step(isOn: false, holdsVoice: true, voicePlaying: true, random);
        Assert.True(off.Silence);
        Assert.True(off.Drop);
        Assert.True(off.Shutdown);
        Assert.Null(off.Play);

        RadioStep still = radio.Step(isOn: false, holdsVoice: false, voicePlaying: false, random);
        Assert.Equal(default, still);
    }

    // The stream's next draw, read from a copy so the stream itself is left where it was.
    private static uint Peek(IgiRandom random)
    {
        IgiRandom copy = IgiRandom.Seeded();
        (uint a, uint b, uint c) = random.State;
        copy.Seed(a, b, c);
        return copy.NextRaw();
    }
}
