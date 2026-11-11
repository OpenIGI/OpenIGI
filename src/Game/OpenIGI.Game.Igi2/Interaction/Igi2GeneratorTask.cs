using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Game.Objects;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A generator, I.G.I. 2's <c>Generator</c>: an <c>ExplodeObject</c> that gives power while its expression says so, hums
/// while it does, and comes back on by itself once its backup timer has run.
/// </summary>
/// <remarks>
/// <para>
/// <c>Generator_RunHandler</c> runs the <c>ExplodeObject</c>'s handler, then the power rule both games share
/// (<see cref="GeneratorPower"/>): off once blown up, else what "On expression" gives, then the backup; on, it makes its
/// run sound if it holds none; off, it deletes the sound it holds and, having been on as the tick began, plays its shutdown
/// sound.
/// </para>
/// <para>
/// <c>Generator_RegisterScriptSymbolsHandler</c> adds <c>isOn</c> (a byte) and <c>nBackupTimer</c> to the
/// <c>ExplodeObject</c>'s symbols. The type has no save list of its own, so a save keeps only what an <c>ExplodeObject</c>'s does.
/// </para>
/// </remarks>
public sealed partial class Igi2GeneratorTask : Igi2ExplodeObjectTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public new const string TypeName = "Generator";

    private readonly GeneratorPower _power = new();
    private LevelExpression? _on;
    private Igi2Sound? _run;

    /// <summary>Gets or sets the sound it makes while on.</summary>
    [ScriptParam(ScriptParamType.String16, "Run sound")]
    public string RunSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes as it goes off.</summary>
    [ScriptParam(ScriptParamType.String16, "Shutdown sound")]
    public string ShutdownSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it is on.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it comes back on by itself.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use backup timer")]
    public bool UseBackupTimer { get; set; } = true;

    /// <summary>Gets or sets how long it stays off before it comes back on, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Seconds until power backup")]
    public float BackupSeconds { get; set; } = 10f;

    /// <summary>Gets or sets the level's sounds.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets a value indicating whether it gives power, the script's <c>isOn</c>.</summary>
    public bool IsOn => _power.IsOn;

    /// <summary>Gets the ticks until its backup restores power, the script's <c>nBackupTimer</c>.</summary>
    public int BackupTimer => _power.BackupTimer;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        bool wasOn = _power.IsOn;
        base.Run(level);

        _on ??= Igi2LevelExpressions.Create(OnScript, Name, level.Natives);
        bool? expression = _on.IsEmpty ? null : unchecked((byte)LevelExpression.Truncate(level.Evaluate(_on))) != 0;
        _power.Step(Explosive.IsExploded, expression, UseBackupTimer, BackupSeconds);

        // The run sound is held only once one was made: a name nothing defines is asked for again each tick.
        GeneratorSounds change = _power.SettleSounds(wasOn, RunSound.Length > 0);
        if (change.HasFlag(GeneratorSounds.StartRun))
        {
            _run = Sounds?.New(RunSound, Position);
            _power.IsRunSoundHeld = _run is not null;
        }

        if (change.HasFlag(GeneratorSounds.StopRun))
        {
            _run?.Delete();
            _run = null;
        }

        if (change.HasFlag(GeneratorSounds.Shutdown))
        {
            Sounds?.New(ShutdownSound, Position);
        }
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        base.OnExportVars(variables);
        variables.BindInt(this, "isOn", () => _power.IsOn ? 1 : 0, value => _power.IsOn = unchecked((byte)value) != 0, sizeof(byte));
        variables.BindInt(this, "nBackupTimer", () => _power.BackupTimer, value => _power.BackupTimer = value);
    }
}
