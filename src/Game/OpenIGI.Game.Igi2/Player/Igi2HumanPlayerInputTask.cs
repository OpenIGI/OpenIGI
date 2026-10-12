using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The player's input, I.G.I. 2's <c>HumanPlayerInput</c> (humanplayerinput.c): the child of the player that reads the controls
/// into the player's channels each tick and sends them up as its update, and that decides when the map computer may be used.
/// </summary>
/// <remarks>
/// <c>HumanPlayerInput_RunHandler</c>: while the <c>Mapcomputer unavailable expression</c> holds, the frame's map-computer
/// channels (map, log, objectives and IGI HQ) are cleared; the <c>Mapcomputer on expression</c>, when there is one, decides whether
/// the computer is enabled (<c>Computer_isEnabled</c>), which it otherwise always is. The controls themselves are read by the
/// host's input (<see cref="Igi2PlayerInputReader"/>); recording and playback of them are not ported.
/// </remarks>
public sealed partial class Igi2HumanPlayerInputTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "HumanPlayerInput";

    private Igi2LevelExpression? _on;
    private Igi2LevelExpression? _unavailable;

    /// <summary>Gets or sets the expression that enables the map computer; blank for always.</summary>
    [ScriptParam(ScriptParamType.VarString, "Mapcomputer on expression")]
    public string OnExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression under which the map computer's controls do nothing.</summary>
    [ScriptParam(ScriptParamType.VarString, "Mapcomputer unavailable expression")]
    public string UnavailableExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the map computer is enabled, <c>Computer_isEnabled</c>.</summary>
    public bool IsComputerEnabled { get; private set; } = true;

    /// <summary>Gets a value indicating whether the map computer's controls were cleared this tick.</summary>
    public bool IsComputerUnavailable { get; private set; }

    /// <summary>The map computer's channels the unavailable expression clears.</summary>
    private static readonly Igi2HumanChannel[] ComputerChannels =
    [
        Igi2HumanChannel.ComputerMap, Igi2HumanChannel.ComputerLog, Igi2HumanChannel.ComputerObjectives, Igi2HumanChannel.ComputerIgiHq,
    ];

    /// <inheritdoc/>
    /// <remarks>Its expressions are evaluated; the player applies them to the frame it read (<see cref="Apply"/>).</remarks>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _on ??= new Igi2LevelExpression(OnExpression, Name, level.Natives);
        _unavailable ??= new Igi2LevelExpression(UnavailableExpression, Name, level.Natives);
        IsComputerUnavailable = !_unavailable.IsEmpty && (uint)(long)level.Evaluate(_unavailable) != 0;
        IsComputerEnabled = _on.IsEmpty || (int)(long)level.Evaluate(_on) != 0;
    }

    /// <summary>Clears the map computer's channels of a frame while it is unavailable.</summary>
    /// <param name="frame">The frame.</param>
    public void Apply(Span<float> frame)
    {
        if (!IsComputerUnavailable)
        {
            return;
        }

        foreach (Igi2HumanChannel channel in ComputerChannels)
        {
            frame[(int)channel] = 0f;
        }
    }
}
