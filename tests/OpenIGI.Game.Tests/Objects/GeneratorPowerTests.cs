using Xunit;
using OpenIGI.Game.Objects;

namespace OpenIGI.Game.Tests.Objects;

/// <summary>The power rule both games' <c>Generator</c> runs (Project I.G.I. <c>0x43B500</c>, I.G.I. 2 <c>Generator_RunHandler</c>).</summary>
public sealed class GeneratorPowerTests
{
    [Fact]
    public void An_expression_sets_it_and_none_leaves_it_as_it_was()
    {
        GeneratorPower power = new();

        power.Step(exploded: false, expression: true, useBackup: false, backupSeconds: 10f);
        Assert.True(power.IsOn);

        power.Step(exploded: false, expression: null, useBackup: false, backupSeconds: 10f);
        Assert.True(power.IsOn);

        power.Step(exploded: false, expression: false, useBackup: false, backupSeconds: 10f);
        Assert.False(power.IsOn);
        Assert.Equal(GeneratorPower.BackupIdle, power.BackupTimer);
    }

    [Fact]
    public void Blown_up_it_is_off_whatever_its_expression_says()
    {
        GeneratorPower power = new() { IsOn = true };

        power.Step(exploded: true, expression: true, useBackup: false, backupSeconds: 10f);

        Assert.False(power.IsOn);
    }

    [Fact]
    public void With_a_backup_and_no_expression_it_starts_its_timer_on_its_first_tick()
    {
        GeneratorPower power = new();

        power.Step(exploded: false, expression: null, useBackup: true, backupSeconds: 10f);

        // Off, the timer starts at 300 and counts to 299; it is not yet on.
        Assert.False(power.IsOn);
        Assert.Equal(299, power.BackupTimer);
    }

    [Fact]
    public void The_backup_brings_it_back_when_its_timer_has_run()
    {
        GeneratorPower power = new();

        // 0.1 s is three ticks: started at 3, it counts 2, 1, 0 and is on at the tick that finds it at zero.
        for (int tick = 0; tick < 3; tick++)
        {
            power.Step(exploded: false, expression: false, useBackup: true, backupSeconds: 0.1f);
            Assert.False(power.IsOn);
        }

        power.Step(exploded: false, expression: false, useBackup: true, backupSeconds: 0.1f);
        Assert.True(power.IsOn);
        Assert.Equal(0, power.BackupTimer);
    }

    [Fact]
    public void A_timer_that_is_idle_while_it_is_on_keeps_it_on()
    {
        GeneratorPower power = new();

        power.Step(exploded: false, expression: true, useBackup: true, backupSeconds: 10f);

        Assert.True(power.IsOn);
        Assert.Equal(GeneratorPower.BackupIdle, power.BackupTimer);
    }

    [Fact]
    public void Its_run_sound_starts_once_and_stops_with_a_shutdown_sound()
    {
        GeneratorPower power = new() { IsOn = true };

        Assert.Equal(GeneratorSounds.StartRun, power.SettleSounds(wasOn: false, hasRunSound: true));
        Assert.Equal(GeneratorSounds.None, power.SettleSounds(wasOn: true, hasRunSound: true));

        power.IsOn = false;
        Assert.Equal(GeneratorSounds.StopRun | GeneratorSounds.Shutdown, power.SettleSounds(wasOn: true, hasRunSound: true));
        Assert.Equal(GeneratorSounds.None, power.SettleSounds(wasOn: false, hasRunSound: true));
    }

    [Fact]
    public void Without_a_run_sound_it_still_plays_its_shutdown()
    {
        GeneratorPower power = new() { IsOn = true };

        Assert.Equal(GeneratorSounds.None, power.SettleSounds(wasOn: false, hasRunSound: false));

        power.IsOn = false;
        Assert.Equal(GeneratorSounds.Shutdown, power.SettleSounds(wasOn: true, hasRunSound: false));
    }
}
