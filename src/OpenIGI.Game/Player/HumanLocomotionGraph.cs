namespace OpenIGI.Game.Player;

/// <summary>
/// The locomotion state machine's transition graph — which states each state can move to, in the order
/// its per-tick function tests them.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from all 49 per-tick functions rather than read one at a time: every transition compiles
/// to the same shape, <c>mov [sm+0Ch], currentState; mov [sm+10h], N; call states[N]</c>, so the
/// immediates written to <c>+10h</c> are the targets and their order in the code is the test order.
/// </para>
/// <para>
/// <b>Order is priority.</b> Almost every state begins with the same three — lose the ground, then the
/// two death flags — and the movement states then test the run/walk toggle, the jump edge and the
/// directions.
/// </para>
/// </remarks>
public static class HumanLocomotionGraph
{
    /// <summary>The state a human with no ground under it falls into.</summary>
    public const int Falling = 29;

    /// <summary>The jump state.</summary>
    public const int Jump = 28;

    /// <summary>The two states the death flags lead to.</summary>
    public static readonly int[] DeathStates = [46, 47];

    private static readonly int[][] Targets =
    [
        /* 0  */ [29, 46, 47, 9, 12, 15, 16, 1, 4, 7, 8, 28, 19],
        /* 1  */ [29, 46, 47, 9, 28, 2, 3, 0, 20],
        /* 2  */ [29, 46, 47, 10, 28, 7, 1, 21],
        /* 3  */ [29, 46, 47, 11, 28, 8, 1, 22],
        /* 4  */ [29, 46, 47, 12, 28, 5, 6, 0, 23],
        /* 5  */ [29, 46, 47, 13, 28, 7, 4, 24],
        /* 6  */ [29, 46, 47, 14, 28, 8, 4, 25],
        /* 7  */ [29, 46, 47, 15, 28, 0, 2, 5, 26],
        /* 8  */ [29, 46, 47, 16, 28, 0, 3, 6, 27],
        /* 9  */ [29, 46, 47, 1, 28, 2, 3, 0, 20],
        /* 10 */ [29, 46, 47, 2, 28, 15, 9, 21],
        /* 11 */ [29, 46, 47, 3, 28, 16, 9, 22],
        /* 12 */ [29, 46, 47, 4, 28, 13, 14, 0, 23],
        /* 13 */ [29, 46, 47, 5, 28, 7, 12, 23],
        /* 14 */ [29, 46, 47, 6, 28, 15, 12, 23],
        /* 15 */ [29, 46, 47, 7, 28, 0, 10, 13, 26],
        /* 16 */ [29, 46, 47, 8, 28, 0, 11, 14, 27],
        /* 17 */ [29, 19],
        /* 18 */ [29, 0],
        /* 19 */ [29, 46, 47, 26, 27, 0, 20, 23],
        /* 20 */ [29, 46, 47, 21, 22, 19, 9, 1],
        /* 21 */ [29, 46, 47, 20, 26, 10, 2],
        /* 22 */ [29, 46, 47, 20, 27, 11, 3],
        /* 23 */ [29, 46, 47, 24, 25, 19, 12, 4],
        /* 24 */ [29, 46, 47, 26, 23, 13, 5],
        /* 25 */ [29, 46, 47, 27, 23, 14, 6],
        /* 26 */ [29, 46, 47, 19, 21, 24, 15, 7],
        /* 27 */ [29, 46, 47, 19, 22, 25, 16, 8],
        /* 28 */ [9, 12, 15, 16, 1, 4, 7, 8],
        /* 29 */ [],
        /* 30 */ [0],
        /* 31 */ [0],
        /* 32 */ [0],
        /* 33 */ [0],
        /* 34 */ [30, 31, 32, 33, 34, 35, 36],
        /* 35 */ [0],
        /* 36 */ [0],
        /* 37 */ [39, 0, 40],
        /* 38 */ [37],
        /* 39 */ [0],
        /* 40 */ [],
        /* 41 */ [29],
        /* 42 */ [],
        /* 43 */ [],
        /* 44 */ [],
        /* 45 */ [47],
        /* 46 */ [47, 46, 0],
        /* 47 */ [],
        /* 48 */ [1, 0, 9, 7, 10, 8, 2, 12, 6, 8, 3, 5, 4, 11],
    ];

    /// <summary>Gets the states a state can move to, in test order.</summary>
    /// <param name="state">The locomotion state id.</param>
    /// <returns>The targets; empty for a state whose transitions are computed rather than immediate.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a state.</exception>
    public static IReadOnlyList<int> From(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, HumanLocomotionStates.Count);
        return Targets[state];
    }

    /// <summary>The states 0..27, which share the movement states' branch structure.</summary>
    public const int MovementStateCount = 28;

    /// <summary>Pairs a running movement state with its walking counterpart, or vice versa.</summary>
    /// <param name="state">A state in <c>1..16</c>.</param>
    /// <returns>The run/walk counterpart.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a paired state.</exception>
    /// <remarks>
    /// States <c>1..8</c> and <c>9..16</c> are the same eight directions at run and walk speeds, and the
    /// pairing is not an assumption: each one's <b>fourth</b> target — the run/walk toggle — is exactly
    /// its counterpart.
    /// </remarks>
    public static int RunWalkCounterpart(int state)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(state, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(state, 16);
        return state <= 8 ? state + 8 : state - 8;
    }

    /// <summary>Reports whether a state can be reached from the initial state.</summary>
    /// <param name="state">The state to look for.</param>
    /// <returns><see langword="true"/> when a chain of transitions from state 0 reaches it.</returns>
    public static bool IsReachableFromIdle(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, HumanLocomotionStates.Count);

        HashSet<int> seen = [0];
        Queue<int> pending = new();
        pending.Enqueue(0);
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            if (current == state)
            {
                return true;
            }

            foreach (int next in Targets[current])
            {
                if (seen.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return false;
    }
}
