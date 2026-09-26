namespace OpenIGI.Game.Player;

/// <summary>
/// What clip each state of a human class's body machine plays — the table a class installs at
/// <c>Human+824</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Human</c> class builds its state machine at <c>Human+748</c> over its own pointer table and
/// installs its own <c>{animation, mode}</c> table at <c>Human+824</c>; each state's entry function then
/// calls <c>0x489E70(human, stateId, blend)</c>, which indexes it. The classes do not share state
/// numbering: a soldier has 38 states, a female soldier 13 and an RPG soldier 15, and the same id means
/// a different posture in each. A clip id is always read through the body's own table.
/// </para>
/// <para>
/// <b>Animation id 0 means "this state names its own clip"</b>, not animation 0 — clip ids start at 2.
/// Death, scripted-animation and flinch states carry it.
/// </para>
/// </remarks>
public sealed class HumanBodyStateTable
{
    /// <summary>The animation id that means "this state names its own clip".</summary>
    public const int SelfDriven = 0;

    private readonly (int Animation, int Mode)[] _entries;

    /// <summary>Initializes a table from its transcribed <c>{animation, mode}</c> pairs.</summary>
    /// <param name="entries">One pair per state, in state order.</param>
    /// <exception cref="ArgumentException"><paramref name="entries"/> is empty.</exception>
    public HumanBodyStateTable(params (int Animation, int Mode)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfZero(entries.Length, nameof(entries));
        _entries = [.. entries];
    }

    /// <summary>A copy with some states' clips replaced, their modes kept.</summary>
    /// <param name="clips">The states whose clip differs, and the clip each plays.</param>
    /// <returns>The new table.</returns>
    /// <remarks>How a class's alternative tables relate to its base one: same states, same modes, some clips.</remarks>
    public HumanBodyStateTable WithAnimations(params (int State, int Animation)[] clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var entries = ((int Animation, int Mode)[])_entries.Clone();
        foreach ((int state, int animation) in clips)
        {
            entries[state] = (animation, entries[state].Mode);
        }

        return new HumanBodyStateTable(entries);
    }

    /// <summary>Gets how many states the machine has.</summary>
    public int StateCount => _entries.Length;

    /// <summary>The clip a state plays.</summary>
    /// <param name="state">The body state — <c>Human+764</c>.</param>
    /// <returns>The animation id, or <c>-1</c> for a state outside the table or one that names its own clip.</returns>
    /// <remarks>
    /// A <see cref="SelfDriven"/> state is returned as <c>-1</c> too: the caller has to name the clip
    /// itself, and handing back 0 would play whatever clip happened to be first.
    /// </remarks>
    public int AnimationOf(int state)
    {
        if (!Contains(state))
        {
            return -1;
        }

        int animation = _entries[state].Animation;
        return animation == SelfDriven ? -1 : animation;
    }

    /// <summary>The mode a state's clip plays in.</summary>
    /// <param name="state">The body state.</param>
    /// <returns>The mode, or <c>-1</c> for a state outside the table.</returns>
    /// <remarks>
    /// The second field of each pair is passed by <c>0x489E70</c> through <c>0x489EA0</c> to
    /// <c>0x4D61D0</c> as the animation flag word. Mode one loops; mode zero is a one-shot.
    /// </remarks>
    public int ModeOf(int state) => Contains(state) ? _entries[state].Mode : -1;

    /// <summary>Whether a state's clip loops.</summary>
    /// <param name="state">The body state.</param>
    /// <returns><see langword="true"/> for a mode-one state.</returns>
    public bool Loops(int state) => ModeOf(state) == 1;

    /// <summary>Whether a state names its own clip rather than taking one from the table.</summary>
    /// <param name="state">The body state.</param>
    /// <returns><see langword="true"/> when the table's entry is <see cref="SelfDriven"/>.</returns>
    public bool IsSelfDriven(int state) => Contains(state) && _entries[state].Animation == SelfDriven;

    private bool Contains(int state) => state >= 0 && state < _entries.Length;
}
