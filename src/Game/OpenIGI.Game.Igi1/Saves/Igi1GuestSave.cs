using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Igi1.Saves;

/// <summary>
/// What Project I.G.I.'s soldiers standing in another game's level keep of themselves in that level's save: one call a
/// soldier, named by the level's own human it stands for, holding what the game's own save holds of a soldier.
/// </summary>
/// <remarks>
/// The soldiers' tasks are in a tree of their own, which the level's save never walks; the level's humans they stand for are
/// in the level's, and their ids are what a save made in one run finds the same soldier by in another.
/// </remarks>
/// <param name="soldiers">The soldiers.</param>
/// <param name="hosts">Each soldier, with the level's human it stands for.</param>
internal sealed class Igi1GuestSoldiersSave(SoldierSimulation soldiers, IReadOnlyDictionary<HumanSoldierTask, GameTask> hosts) : ISaveSection
{
    /// <summary>The call a soldier's state is written under.</summary>
    public const string Call = "Igi1GuestSoldier";

    // A save script's line ending, as every section writes it.
    private const string LineEnd = "\r\n";

    /// <inheritdoc/>
    public string CallName => Call;

    /// <inheritdoc/>
    public string Write()
    {
        System.Text.StringBuilder lines = new();
        foreach ((GameTask soldier, ISavedState state) in soldiers.SavedStates(solids: null))
        {
            if (soldier is HumanSoldierTask guest && hosts.TryGetValue(guest, out GameTask? host) && state is IExchangedState kept)
            {
                StateWriter writer = new();
                StateExchange exchange = StateExchange.Writing(writer);
                exchange.Int(host.Id);
                kept.Exchange(exchange);
                string arguments = writer.Arguments;
                lines.Append(Call).Append('(').Append(arguments.Length > 2 ? arguments[2..] : string.Empty).Append(");" + LineEnd);
            }
        }

        return lines.ToString();
    }

    /// <inheritdoc/>
    public void Read(ISaveArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return;
        }

        int hostId = arguments.GetInt32(0);
        foreach ((GameTask soldier, ISavedState state) in soldiers.SavedStates(solids: null))
        {
            if (soldier is HumanSoldierTask guest && hosts.TryGetValue(guest, out GameTask? host) && host.Id == hostId
                && state is IExchangedState kept)
            {
                kept.Exchange(StateExchange.Reading(new StateReader(arguments, 1)));
                return;
            }
        }
    }

    /// <inheritdoc/>
    public void LoadDone()
    {
    }
}
