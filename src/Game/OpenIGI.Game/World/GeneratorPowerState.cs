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
    Func<bool> isInWorld) : Saves.IExchangedState
{
    public GameTask Task { get; } = task;

    public Func<bool> IsInWorld { get; } = isInWorld;

    private bool _explodedWhenTicked;

    private readonly Objects.GeneratorPower _power = new();

    public bool IsOn
    {
        get => _power.IsOn;
        set => _power.IsOn = value;
    }
    public int BackupTimer
    {
        get => _power.BackupTimer;
        set => _power.BackupTimer = value;
    }

    /// <summary>Gets or sets the co-op server's word on the power, which a client's update takes in place of its own.</summary>
    public bool? MirroredOn { get; set; }

    public void Initialize() => IsOn = onCondition?.Invoke() ?? IsOn;

    // As the match began.
    private (bool Exploded, bool On, int Backup, bool? Mirrored, bool Running) _start;

    public void MarkLevelStart() => _start = (_explodedWhenTicked, IsOn, BackupTimer, MirroredOn, _running);

    public void ResetToLevelStart() => (_explodedWhenTicked, IsOn, BackupTimer, MirroredOn, _running) = _start;

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        var levelStart = _start;
        MarkLevelStart();
        _start = (
            exchange.Bool(_start.Exploded),
            exchange.Bool(_start.On),
            exchange.Int(_start.Backup),
            exchange.Bool(_start.Mirrored),
            exchange.Bool(_start.Running));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

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

    private bool _running
    {
        get => _power.IsRunSoundHeld;
        set => _power.IsRunSoundHeld = value;
    }

    private void Run(bool exploded)
    {
        bool wasOn = IsOn;
        Power(exploded);

        // 0x43B500: while on it holds its run sound, started once; as it goes off it lets go of it and, having been on,
        // plays its shutdown sound once, where it stands.
        Objects.GeneratorSounds change = _power.SettleSounds(wasOn, runSound.Length > 0);
        if (change == Objects.GeneratorSounds.None)
        {
            return;
        }

        Vector3d at = task.TryGetValue("Position", out object? placed) && placed is Vector3d position ? position : default;
        if (change.HasFlag(Objects.GeneratorSounds.StartRun))
        {
            sounds(new InteractionSoundEvent(task, InteractionSound.LoopStarted, runSound, at, Matrix3f.Identity, default));
        }

        if (change.HasFlag(Objects.GeneratorSounds.StopRun))
        {
            sounds(new InteractionSoundEvent(task, InteractionSound.LoopStopped, string.Empty, at, Matrix3f.Identity, default));
        }

        if (change.HasFlag(Objects.GeneratorSounds.Shutdown) && shutdownSound.Length > 0)
        {
            sounds(new InteractionSoundEvent(task, InteractionSound.Played, shutdownSound, at, Matrix3f.Identity, default));
        }
    }

    private void Power(bool exploded)
    {
        // 0x43B500: off once exploded; else its On expression, when it has one that compiled -- a generator with none
        // keeps what it had, off from the constructor, and comes on only by its backup.
        if (MirroredOn is bool told)
        {
            IsOn = told;
            return;
        }

        // A generator with no expression that compiled keeps what it had, off from the constructor, and comes on only by
        // its backup.
        _power.Step(exploded, onCondition?.Invoke(), useBackupTimer, backupSeconds);
    }
}
