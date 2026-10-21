using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// A <c>Generator</c> task's power (<c>0x43B500</c>): its On expression, its backup timer, and the run and shutdown
/// sounds that follow its power, as the world-interaction registry runs it.
/// </summary>
internal sealed class GeneratorPowerState(
    DeclaredTask task,
    string runSound,
    string shutdownSound,
    Action<InteractionSoundEvent> sounds,
    Func<bool?>? onCondition,
    Func<bool> isExploded,
    bool useBackupTimer,
    float backupSeconds,
    Func<bool> isInWorld)
{
    public GameTask Task { get; } = task;

    public Func<bool> IsInWorld { get; } = isInWorld;

    private bool _explodedWhenTicked;

    public bool IsOn { get; set; }
    public int BackupTimer { get; set; } = -1;

    public void Initialize() => IsOn = onCondition?.Invoke() ?? IsOn;

    public void Tick()
    {
        _explodedWhenTicked = isExploded();
        Run(_explodedWhenTicked);
    }

    /// <summary>Runs the tick again for a generator that exploded after it had already ticked.</summary>
    /// <remarks>
    /// Retail's Run calls the ExplodeObject update first, so the explosion and the power cut share a
    /// tick. The port's destructibles tick after the interactions; re-running the whole Run is safe
    /// because every step of it is settled by the state the first pass left.
    /// </remarks>
    public void Settle()
    {
        if (!_explodedWhenTicked && isExploded())
        {
            _explodedWhenTicked = true;
            Run(exploded: true);
        }
    }

    private bool _running;

    private void Run(bool exploded)
    {
        bool wasOn = IsOn;
        Power(exploded);

        // 0x43B500: while on it holds its run sound, started once; as it goes off it lets go of it and, having been on,
        // plays its shutdown sound once, where it stands.
        Vector3d at = task.TryGetValue("Position", out object? placed) && placed is Vector3d position ? position : default;
        if (IsOn)
        {
            if (!_running && runSound.Length > 0)
            {
                _running = true;
                sounds(new InteractionSoundEvent(task, InteractionSound.LoopStarted, runSound, at, Matrix3f.Identity, default));
            }
        }
        else
        {
            if (_running)
            {
                _running = false;
                sounds(new InteractionSoundEvent(task, InteractionSound.LoopStopped, string.Empty, at, Matrix3f.Identity, default));
            }

            if (wasOn && shutdownSound.Length > 0)
            {
                sounds(new InteractionSoundEvent(task, InteractionSound.Played, shutdownSound, at, Matrix3f.Identity, default));
            }
        }
    }

    private void Power(bool exploded)
    {
        // 0x43B500: off once exploded; else its On expression, when it has one that compiled -- a generator with none
        // keeps what it had, off from the constructor, and comes on only by its backup.
        if (exploded)
        {
            IsOn = false;
        }
        else if (onCondition?.Invoke() is bool on)
        {
            IsOn = on;
        }

        if (!useBackupTimer)
        {
            return;
        }

        // Generator::Run (0x43B500) starts the backup only after primary power is lost, then
        // publishes power again when the authored 30 Hz countdown reaches zero; the countdown is
        // (__int64)(seconds * 30.0), taken as it starts.
        if (!IsOn && BackupTimer == -1)
        {
            BackupTimer = (int)(backupSeconds * 30.0);
        }

        if (BackupTimer <= 0)
        {
            IsOn = true;
        }
        else
        {
            BackupTimer--;
        }
    }
}
