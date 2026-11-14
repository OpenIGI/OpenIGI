using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.Flow;

/// <summary>Counts what one criterion finds inside an area.</summary>
/// <param name="area">The area being counted.</param>
/// <param name="mask">The area's whole criteria mask, which a team's criterion reads to tell which teams count.</param>
/// <returns>How many it found.</returns>
public delegate int AreaCriterion(AreaActivateTask area, int mask);

/// <summary>
/// The criteria an <see cref="AreaActivateTask"/> counts by: one bit each, registered by the modules whose things an area
/// can hold.
/// </summary>
/// <remarks>
/// The original's table of 31 criteria (Project I.G.I. <c>AreaActivate_AddCriteria</c>, <c>0x504A30</c>; I.G.I. 2
/// <c>AreaActivateQTask_RegisterCriteria</c>): the humans register one for all of them and one for each team, the vehicles
/// register theirs, and each is a script constant holding its bit, which a level's <c>Criteria</c> slot ors together. An
/// area's run handler calls the criterion of every bit its mask has set and adds up what each counts.
/// </remarks>
public sealed class AreaCriteria
{
    /// <summary>The number of criteria the table holds.</summary>
    public const int Capacity = 31;

    private readonly AreaCriterion?[] _criteria = new AreaCriterion?[Capacity];

    /// <summary>Gets a value indicating whether any criterion has been registered.</summary>
    public bool HasAny { get; private set; }

    /// <summary>Sets the criterion of one bit.</summary>
    /// <param name="bit">The criterion's bit, a single one of the low <see cref="Capacity"/>.</param>
    /// <param name="criterion">What counts for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="criterion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bit"/> is not one bit of the table.</exception>
    public void Set(int bit, AreaCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        if (bit <= 0 || !int.IsPow2(bit) || int.Log2(bit) >= Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(bit), bit, "A criterion is one bit of the table.");
        }

        _criteria[int.Log2(bit)] = criterion;
        HasAny = true;
    }

    /// <summary>Counts what an area holds: the sum of what the criterion of each bit in its mask counts.</summary>
    /// <param name="area">The area.</param>
    /// <param name="mask">Its criteria mask.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="area"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The mask names a criterion nothing registered, which the original calls through a null pointer.</exception>
    public int Count(AreaActivateTask area, int mask)
    {
        ArgumentNullException.ThrowIfNull(area);
        int sum = 0;
        for (int index = 0; index < Capacity; index++)
        {
            if ((mask & (1 << index)) == 0)
            {
                continue;
            }

            AreaCriterion criterion = _criteria[index]
                ?? throw new EngineFatalException($"{area.Name} counts by criterion {index}, which nothing registered.");
            sum += criterion(area, mask);
        }

        return sum;
    }
}
