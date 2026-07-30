using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>How an attacker is aiming at a shared target.</summary>
/// <remarks>
/// Written by <c>sub_4511F0(record, ai, mode)</c> into the record's per-attacker array at <c>+144</c>,
/// and counted by <c>sub_451270</c>.
/// </remarks>
public enum AiAimMode
{
    /// <summary>Not aiming. The value a freshly-added attacker starts on.</summary>
    None = -1,

    /// <summary>Aiming to shoot.</summary>
    Aiming = 0,

    /// <summary>Aiming while reloading.</summary>
    Reloading = 1,

    /// <summary>
    /// Aiming as the fifth or later attacker — what <see cref="AiCombatTarget.MaximumAimers"/> pushes
    /// an AI into when four are already aiming in <see cref="Aiming"/>.
    /// </summary>
    Crowded = 2,
}

/// <summary>
/// One human being fought, shared by every AI fighting it — the original's 304-byte record from the
/// pool at <c>0x57D838</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>sub_451080(ai, task)</c> finds the record already naming a target or takes a free slot, and
/// <c>HumanAI+15016</c> points at it. Because the record is shared, an AI can ask <i>how many others
/// are already shooting at this person</i> without a search — which is exactly what the guard per-tick
/// does before it opens fire.
/// </para>
/// </remarks>
public sealed class AiCombatTarget
{
    /// <summary>How many records the original's pool holds.</summary>
    /// <remarks>Eight, from the 2432-byte span at <c>0x57D838</c> divided by the 304-byte record.</remarks>
    public const int PoolSize = 8;

    /// <summary>How many attackers may aim in <see cref="AiAimMode.Aiming"/> before the rest crowd.</summary>
    /// <remarks>
    /// The test is <c>sub_451270(target, 0) &gt;= 4</c>, so the fifth AI to engage takes
    /// <see cref="AiAimMode.Crowded"/> instead. It is the game's own answer to a whole squad lining up
    /// the same shot.
    /// </remarks>
    public const int MaximumAimers = 4;

    private readonly List<object> _attackers = [];
    private readonly List<AiAimMode> _modes = [];

    /// <summary>Initializes a new instance of the <see cref="AiCombatTarget"/> class.</summary>
    /// <param name="target">Whatever identifies the human being fought.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public AiCombatTarget(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    /// <summary>Gets the human being fought.</summary>
    public object Target { get; }

    /// <summary>Gets the attackers, in the order they joined.</summary>
    public IReadOnlyList<object> Attackers => _attackers;

    /// <summary>Gets the target's position as the last attacker to look refreshed it — record <c>+272</c>.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets how many attackers have been removed with the flag set — record <c>+300</c>.</summary>
    public int FlaggedRemovals { get; private set; }

    /// <summary>Gets whether the record still has attackers, and so is still in use.</summary>
    public bool IsLive => _attackers.Count > 0;

    /// <summary>Adds an attacker, as <c>sub_451080</c>'s tail does.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <exception cref="ArgumentNullException"><paramref name="attacker"/> is <see langword="null"/>.</exception>
    /// <remarks>A new attacker starts on <see cref="AiAimMode.None"/>, not on <see cref="AiAimMode.Aiming"/>.</remarks>
    public void Add(object attacker)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        if (_attackers.Contains(attacker))
        {
            return;
        }

        _attackers.Add(attacker);
        _modes.Add(AiAimMode.None);
    }

    /// <summary>Sets an attacker's aim mode — <c>sub_4511F0</c>.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="mode">The mode.</param>
    /// <remarks>An attacker the record does not hold is ignored, as the original's search is.</remarks>
    public void Aim(object attacker, AiAimMode mode)
    {
        int index = _attackers.IndexOf(attacker);
        if (index >= 0)
        {
            _modes[index] = mode;
        }
    }

    /// <summary>Gets an attacker's aim mode.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <returns>The mode, or <see cref="AiAimMode.None"/> when the record does not hold it.</returns>
    public AiAimMode ModeOf(object attacker)
    {
        int index = _attackers.IndexOf(attacker);
        return index >= 0 ? _modes[index] : AiAimMode.None;
    }

    /// <summary>Counts the attackers holding a mode — <c>sub_451270</c>.</summary>
    /// <param name="mode">The mode to count.</param>
    /// <returns>How many attackers hold it.</returns>
    public int Count(AiAimMode mode)
    {
        int count = 0;
        foreach (AiAimMode held in _modes)
        {
            if (held == mode)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Picks the mode an attacker about to open fire should take.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <returns>
    /// <see cref="AiAimMode.Crowded"/> when <see cref="MaximumAimers"/> others are already aiming,
    /// otherwise <see cref="AiAimMode.Aiming"/>.
    /// </returns>
    /// <remarks>
    /// The original counts <i>every</i> attacker in mode 0 including this one, so an AI that is already
    /// aiming counts toward its own limit — which means the fifth AI never leaves
    /// <see cref="AiAimMode.Crowded"/> while four others hold theirs.
    /// </remarks>
    public AiAimMode ModeToTake(object attacker)
    {
        _ = attacker;
        return Count(AiAimMode.Aiming) >= MaximumAimers ? AiAimMode.Crowded : AiAimMode.Aiming;
    }

    /// <summary>Refreshes the shared position — what <c>sub_48A920</c> does every tick.</summary>
    /// <param name="position">Where the target is.</param>
    public void Refresh(Vector3d position) => Position = position;

    /// <summary>Removes an attacker — <c>sub_4512A0</c>.</summary>
    /// <param name="attacker">The attacker leaving.</param>
    /// <param name="flag">The AI's <c>+15034</c> flag, which increments the record's own counter.</param>
    /// <returns><see langword="true"/> when that was the last attacker and the record is now free.</returns>
    /// <remarks>
    /// The removal is a <b>swap with the last entry</b>, so the attacker order is not preserved — worth
    /// keeping because anything iterating the list sees the same reordering the original does.
    /// </remarks>
    public bool Remove(object attacker, bool flag = false)
    {
        int index = _attackers.IndexOf(attacker);
        if (index < 0)
        {
            return !IsLive;
        }

        if (_attackers.Count == 1)
        {
            _attackers.Clear();
            _modes.Clear();
            return true;
        }

        int last = _attackers.Count - 1;
        _attackers[index] = _attackers[last];
        _modes[index] = _modes[last];
        _attackers.RemoveAt(last);
        _modes.RemoveAt(last);

        if (flag)
        {
            FlaggedRemovals++;
        }

        return false;
    }
}
