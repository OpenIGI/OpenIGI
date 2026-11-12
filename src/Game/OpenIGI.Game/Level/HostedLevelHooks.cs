namespace OpenIGI.Game.Level;

/// <summary>What a level hosted in another game's has done at the end of each of its ticks, and as it is left.</summary>
/// <remarks>Each runs in the order it was added.</remarks>
public sealed class HostedLevelHooks
{
    private readonly List<Action> _eachTick = [];
    private readonly List<Action> _onLeave = [];

    /// <summary>Has something done at the end of each of the level's ticks.</summary>
    /// <param name="action">What is done.</param>
    public void EachTick(Action action) => _eachTick.Add(action ?? throw new ArgumentNullException(nameof(action)));

    /// <summary>Has something done as the level is left, before its tasks are deleted.</summary>
    /// <param name="action">What is done.</param>
    public void OnLeave(Action action) => _onLeave.Add(action ?? throw new ArgumentNullException(nameof(action)));

    /// <summary>Does what each tick ends with.</summary>
    public void RunTick()
    {
        foreach (Action action in _eachTick)
        {
            action();
        }
    }

    /// <summary>Does what leaving the level does.</summary>
    public void RunLeave()
    {
        foreach (Action action in _onLeave)
        {
            action();
        }
    }
}
