using OpenIGI.Game.Replays;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Host;

/// <content>
/// The level's fingerprint (<see cref="SimulationHash"/>): the ticks it has run, its scripts' variables, and every part of the
/// mission that folds its own state (<see cref="ISimulationDigest"/>) -- the hosted mission, the title's player. Two runs of the
/// same level from the same inputs fold to the same number tick for tick; the first tick they do not is the first the
/// simulation read something the inputs did not carry.
/// </content>
public sealed partial class GameHost
{
    // The level's script variables by name, sorted once for each set the level binds (a name's place never follows a hash).
    private string[] _digestNames = [];
    private int _digestNamesCount = -1;

    /// <summary>
    /// Gets or sets what hears the level's fingerprint after each tick it runs: the tick (counted from the level's load) and the
    /// fingerprint; <see langword="null"/> while nothing listens, when none is folded.
    /// </summary>
    public Action<int, ulong>? TickDigested { get; set; }

    /// <summary>The level's fingerprint as it stands now.</summary>
    /// <returns>The fingerprint; that of nothing while no level is up.</returns>
    public ulong SimulationDigest()
    {
        SimulationHash hash = default;
        if (_session.LevelRoot is null)
        {
            return hash.Value;
        }

        hash.Add(TickOfLevel);
        FoldScriptVariables(ref hash);
        if (_hosted is ISimulationDigest mission)
        {
            mission.Fold(ref hash);
        }

        if (TitlePlayer is ISimulationDigest player && !ReferenceEquals(player, _hosted))
        {
            player.Fold(ref hash);
        }

        return hash.Value;
    }

    // After a tick of the level: its fingerprint to whoever listens.
    private void DigestTick()
    {
        if (TickDigested is { } listener)
        {
            listener(TickOfLevel, SimulationDigest());
        }
    }

    // Every variable the level's scripts can read, by name in ordinal order, with its kind and value.
    private void FoldScriptVariables(ref SimulationHash hash)
    {
        Engine.Tasks.ScriptVarRegistry variables = _loop.Variables;
        if (variables.Count != _digestNamesCount)
        {
            _digestNames = [.. variables.BoundNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            _digestNamesCount = variables.Count;
        }

        hash.Add(_digestNames.Length);
        foreach (string name in _digestNames)
        {
            if (!variables.TryRead(name, out QvmValue value))
            {
                hash.Add(-1);
                continue;
            }

            hash.Add((int)value.Kind);
            switch (value.Kind)
            {
                case QvmValueKind.Float:
                    hash.Add(value.Number);
                    break;
                case QvmValueKind.String:
                    hash.Add(value.Text);
                    break;
                default:
                    hash.Add(value.Integer);
                    break;
            }
        }
    }
}
