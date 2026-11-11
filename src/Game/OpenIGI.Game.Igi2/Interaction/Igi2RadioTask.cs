using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Objects;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A radio that chatters while it is on: I.G.I. 2's <c>Radio</c>, built on <c>ExplodeObject</c>.</summary>
/// <remarks>
/// <c>Radio_RunHandler</c> runs the <c>ExplodeObject</c>'s handler first, then the chatter both games share
/// (<see cref="RadioChatter"/>): off once blown up, else as its "On expression" says; one of its four sounds at a drawn gap,
/// from the game's run stream; its "Shutdown sound" as it goes off. It exports <c>isOn</c>.
/// </remarks>
public sealed partial class Igi2RadioTask : Igi2ExplodeObjectTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public new const string TypeName = "Radio";

    private readonly RadioChatter _chatter = new();
    private LevelExpression? _on;
    private Igi2Sound? _voice;

    /// <summary>Gets or sets the first of the four sounds it draws from.</summary>
    [ScriptParam(ScriptParamType.String16, "Radio sound 0")]
    public string Sound0 { get; set; } = string.Empty;

    /// <summary>Gets or sets the second sound.</summary>
    [ScriptParam(ScriptParamType.String16, "Radio sound 1")]
    public string Sound1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the third sound.</summary>
    [ScriptParam(ScriptParamType.String16, "Radio sound 2")]
    public string Sound2 { get; set; } = string.Empty;

    /// <summary>Gets or sets the fourth sound.</summary>
    [ScriptParam(ScriptParamType.String16, "Radio sound 3")]
    public string Sound3 { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes as it goes off.</summary>
    [ScriptParam(ScriptParamType.String16, "Shutdown sound")]
    public string ShutdownSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it is on; empty leaves it as it is.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the level's sounds.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets the game's run stream, <c>Game.tRunRandomState</c>, which the sound and the gap are drawn from.</summary>
    public IgiRandom Random { get; set; } = new();

    /// <summary>Gets a value indicating whether the radio is on.</summary>
    public bool IsOn => _chatter.IsOn;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        base.Run(level);
        _on ??= Igi2LevelExpressions.Create(OnScript, Name, level.Natives);
        bool exploded = Explosive.IsExploded;
        bool? expression = exploded || _on.IsEmpty ? null : unchecked((byte)LevelExpression.Truncate(level.Evaluate(_on))) != 0;
        bool holds = _voice is not null;
        RadioStep step = _chatter.Step(
            RadioChatter.Switched(_chatter.IsOn, exploded, expression), holds, holds && _voice!.IsAlive, Random);
        if (step.Silence)
        {
            _voice!.Delete();
        }

        if (step.Drop)
        {
            _voice = null;
        }

        if (step.Play is int slot)
        {
            _voice = Sounds?.New(slot switch { 0 => Sound0, 1 => Sound1, 2 => Sound2, _ => Sound3 }, Position);
        }

        if (step.Shutdown)
        {
            Sounds?.New(ShutdownSound, Position);
        }
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        base.OnExportVars(variables);
        variables.BindInt(this, "isOn", () => _chatter.IsOn ? 1 : 0, value => _chatter.IsOn = unchecked((byte)value) != 0, sizeof(byte));
    }
}
