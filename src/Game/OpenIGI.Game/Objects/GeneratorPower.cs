namespace OpenIGI.Game.Objects;

/// <summary>What a generator's tick did to the sounds it makes.</summary>
[Flags]
public enum GeneratorSounds
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary>It is on and holds no run sound: start one.</summary>
    StartRun = 1,

    /// <summary>It is off: let go of the run sound, if it holds one.</summary>
    StopRun = 2,

    /// <summary>It went off this tick, having been on: play the shutdown sound once.</summary>
    Shutdown = 4,
}

/// <summary>
/// A generator's power, as both games' <c>Generator</c> task runs it: off once blown up, else as its expression says, and
/// back on by itself when its backup timer runs out.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the same function in both titles (Project I.G.I. <c>0x43B500</c>; I.G.I. 2 <c>Generator_RunHandler</c>), run
/// after the <c>ExplodeObject</c> the type derives from has run:
/// </para>
/// <list type="number">
/// <item>blown up, it is off; otherwise an expression that compiled sets it, and one that did not leaves it as it was;</item>
/// <item>
/// with the backup in use: off and the timer idle (<c>-1</c>), the timer starts at the seconds times thirty, cut to a whole
/// number; a timer above zero counts down, and at zero or below the generator is on -- so a generator
/// with a backup and no expression, off from its constructor, comes on once the backup time has passed;
/// </item>
/// <item>on, it starts its run sound if it holds none; off, it lets go of it, and plays its shutdown sound if it was on.</item>
/// </list>
/// <para>
/// The timer is never put back to idle: a generator's backup restores it once, and after that it holds only as long as its
/// expression says, the timer standing at zero.
/// </para>
/// </remarks>
public sealed class GeneratorPower
{
    /// <summary>The ticks in a second of backup time.</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>The backup timer's value while it has not been started.</summary>
    public const int BackupIdle = -1;

    /// <summary>Gets or sets a value indicating whether the generator gives power, the script's <c>isOn</c>.</summary>
    public bool IsOn { get; set; }

    /// <summary>Gets or sets the ticks until the backup restores power, the script's <c>nBackupTimer</c>.</summary>
    public int BackupTimer { get; set; } = BackupIdle;

    /// <summary>Gets or sets a value indicating whether the run sound is held.</summary>
    public bool IsRunSoundHeld { get; set; }

    /// <summary>Runs one tick's power, without touching the sounds.</summary>
    /// <param name="exploded">Whether the object has blown up.</param>
    /// <param name="expression">What the "On expression" gives this tick, or <see langword="null"/> when there is none that compiled.</param>
    /// <param name="useBackup">The "Use backup timer" parameter.</param>
    /// <param name="backupSeconds">The "Seconds until power backup" parameter.</param>
    public void Step(bool exploded, bool? expression, bool useBackup, float backupSeconds)
    {
        if (exploded)
        {
            IsOn = false;
        }
        else if (expression is bool on)
        {
            IsOn = on;
        }

        if (!useBackup)
        {
            return;
        }

        if (!IsOn && BackupTimer == BackupIdle)
        {
            BackupTimer = (int)(backupSeconds * TicksPerSecond);
        }

        if (BackupTimer > 0)
        {
            BackupTimer--;
        }
        else
        {
            IsOn = true;
        }
    }

    /// <summary>Settles the sounds after a tick's power, and says what the caller is to play or stop.</summary>
    /// <param name="wasOn">Whether the generator was on as the tick began.</param>
    /// <param name="hasRunSound">Whether a run sound is named.</param>
    /// <returns>What to start, stop and play.</returns>
    public GeneratorSounds SettleSounds(bool wasOn, bool hasRunSound)
    {
        if (IsOn)
        {
            if (IsRunSoundHeld || !hasRunSound)
            {
                return GeneratorSounds.None;
            }

            IsRunSoundHeld = true;
            return GeneratorSounds.StartRun;
        }

        GeneratorSounds sounds = GeneratorSounds.None;
        if (IsRunSoundHeld)
        {
            IsRunSoundHeld = false;
            sounds |= GeneratorSounds.StopRun;
        }

        return wasOn ? sounds | GeneratorSounds.Shutdown : sounds;
    }
}
