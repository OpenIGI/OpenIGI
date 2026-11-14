using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The level's own clock and its end, I.G.I. 2's <c>LevelFlow</c>, exporting <c>isComplete</c>, <c>isFailed</c>, <c>nTick</c>,
/// <c>nTimeOfDayTick</c> and <c>eDifficulty</c>.
/// </summary>
/// <remarks>
/// <c>LevelFlow_RunHandler</c> (<c>igi2 0x521ED0</c>): its children run; its <c>Complete</c> script's value is <c>isComplete</c>,
/// and when it holds the game is completed; its <c>Failed</c> script's value is <c>isFailed</c>, and when it holds the game is
/// failed and not completed. Then both clocks count the tick.
/// </remarks>
public sealed partial class Igi2LevelFlowTask : OpenIGI.Game.Flow.LevelFlowTask
{
    /// <summary>The game's frequency, <c>GAME_FREQUENCY</c>: the ticks in a second.</summary>
    public const int Frequency = 30;

    /// <summary>Gets the ticks of the day's clock, <c>nTimeOfDayTick</c>, which a script may set apart from the level's.</summary>
    public int TimeOfDayTick { get; private set; }

    /// <summary>Gets the hour of the day the camera shows, <c>LevelFlow_GetCameraTime</c>: the day's clock in hours, past the start time.</summary>
    public float CameraTime => (((Fpu)TimeOfDayTick / (Frequency * 3600.0)) + StartTime).F;

    /// <summary>Gets the hour of the day a level starts at, from its tree.</summary>
    /// <param name="levelRoot">The level's root.</param>
    /// <returns>The hour, or nought for a level with no flow.</returns>
    public static float StartingCameraTime(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        return Find(levelRoot)?.CameraTime ?? 0f;

        static Igi2LevelFlowTask? Find(GameTask task)
        {
            if (task is Igi2LevelFlowTask flow)
            {
                return flow;
            }

            foreach (GameTask child in task.Children)
            {
                if (Find(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>Sets the day's clock to an hour, <c>LevelFlow_SetTimeOfDay</c>.</summary>
    /// <param name="hours">The hour.</param>
    public void SetTimeOfDay(double hours) => TimeOfDayTick = Fpu.ToInt32((Fpu)hours * (Frequency * 3600.0));

    /// <inheritdoc/>
    /// <remarks><c>LevelFlow_ptSerializeParameterList</c>: its tick, then its time of day.</remarks>
    public override void Exchange(OpenIGI.Game.Saves.StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Tick = exchange.Int(Tick);
        TimeOfDayTick = exchange.Int(TimeOfDayTick);
    }

    /// <inheritdoc/>
    /// <remarks>Both clocks count the tick.</remarks>
    protected override void OnTickCounted() => TimeOfDayTick++;

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        base.OnExportVars(variables);
        variables.BindInt(this, "nTimeOfDayTick", () => TimeOfDayTick, value => TimeOfDayTick = value);
    }
}
