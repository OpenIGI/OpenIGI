namespace OpenIGI.Game.Ai;

/// <summary>
/// A running patrol — the 28-byte cursor <c>0x4537E0</c> initialises, and the walk over a path's commands.
/// </summary>
/// <remarks>
/// <para>
/// A patrolling AI does not index its path by step number. It keeps a cursor holding the current command,
/// where the loop starts, the last two movement commands, the gait, the crouch flag and a delay deadline.
/// For a human this lives inside <c>HumanAI</c> at <c>+5372 + 28n</c> (patrols nest — the per-tick reads
/// the last pushed, and <c>+5396</c> is the count); for <c>CarAI</c> it is a plain member at <c>+48</c>.
/// The same 28 bytes either way.
/// </para>
/// </remarks>
public sealed class AiPatrolRoute
{
    /// <summary>The size of the cursor in bytes in the original.</summary>
    public const int Size = 28;

    /// <summary>The angular tolerance <see cref="AiPatrolCommandKind.LookAtNode"/> accepts — 3°.</summary>
    /// <remarks>The literal <c>0.052359879f</c> the walker passes, which is 3° in radians as a float.</remarks>
    public const float LookAtToleranceRadians = 0.052359879f;

    /// <summary>The value <see cref="Deadline"/> holds while the current command has not begun.</summary>
    public const int NoDeadline = -1;

    /// <summary>The value the command indices hold when nothing has been recorded.</summary>
    public const int NoCommand = -1;

    private AiPatrolRoute(AiPatrolPath path)
    {
        Path = path;
    }

    /// <summary>Gets the path being walked — <c>+0</c>.</summary>
    public AiPatrolPath Path { get; }

    /// <summary>Gets the index of the current command, or <see cref="NoCommand"/> — <c>+4</c>.</summary>
    public int CurrentIndex { get; private set; } = NoCommand;

    /// <summary>Gets the index of the <see cref="AiPatrolCommandKind.End"/> once one has been passed — <c>+8</c>.</summary>
    /// <remarks>
    /// This field does double duty and that is what makes §7.3's behaviour what it is: it is both the
    /// target the peek falls back to when the command list runs out, <b>and</b> the marker that says the
    /// loop has already been entered once.
    /// </remarks>
    public int EndIndex { get; private set; } = NoCommand;

    /// <summary>Gets the index of the most recent movement command — <c>+12</c>.</summary>
    public int LastMoveIndex { get; private set; } = NoCommand;

    /// <summary>Gets the index of the movement command before that — <c>+16</c>.</summary>
    public int PreviousMoveIndex { get; private set; } = NoCommand;

    /// <summary>Gets a value indicating whether the AI is crouching — <c>+20</c>.</summary>
    /// <remarks>
    /// Set by <see cref="AiPatrolCommandKind.Crouch"/> and cleared by <em>both</em> movement commands, so
    /// moving stands the AI up and nothing else does. The walker applies it on every tick, before the
    /// switch runs — not only on the tick that set it.
    /// </remarks>
    public bool IsCrouching { get; private set; }

    /// <summary>Gets a value indicating whether the AI is walking rather than running — <c>+21</c>.</summary>
    /// <remarks>
    /// <b>Starts <see langword="true"/></b>, is written only by <see cref="AiPatrolCommandKind.WalkTo"/>
    /// and <see cref="AiPatrolCommandKind.RunTo"/>, and is read on every tick. So a path that opens with a
    /// <see cref="AiPatrolCommandKind.RunTo"/> and then only delays keeps running, and a path that never
    /// names a gait walks.
    /// </remarks>
    public bool IsWalking { get; private set; } = true;

    /// <summary>Gets the tick a <see cref="AiPatrolCommandKind.Delay"/> ends on, or <see cref="NoDeadline"/> — <c>+24</c>.</summary>
    /// <remarks>
    /// Doubles as "has the current command begun": advancing resets it to <see cref="NoDeadline"/>, which
    /// is how a delay computes its deadline once rather than every tick.
    /// </remarks>
    public int Deadline { get; private set; } = NoDeadline;

    /// <summary>Gets a value indicating whether the patrol has stopped advancing.</summary>
    /// <remarks>
    /// Distinct from having no current command: a stopped patrol keeps executing whatever command it
    /// stopped on, because <c>sub_453760</c>'s early returns leave the cursor untouched.
    /// </remarks>
    public bool HasStopped { get; private set; }

    /// <summary>Gets the current command, or <see langword="null"/> when the cursor is off the end.</summary>
    public AiPatrolCommand? Current =>
        CurrentIndex >= 0 && CurrentIndex < Path.Commands.Count ? Path.Commands[CurrentIndex] : null;

    /// <summary>Starts a patrol at a command index — <c>0x4537E0</c>.</summary>
    /// <param name="path">The path to walk.</param>
    /// <param name="startIndex">The command to start on; the original's third argument, always 0 so far.</param>
    /// <returns>The cursor, or <see langword="null"/> when the path has no command at that index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex"/> is negative.</exception>
    /// <remarks>
    /// The scan to <paramref name="startIndex"/> (<c>0x453880</c>) records the most recent movement command
    /// and the most recent <see cref="AiPatrolCommandKind.End"/> on the way, so a patrol entered part-way
    /// through still knows where its loop starts and where it was last heading. If the command it lands on
    /// is an <see cref="AiPatrolCommandKind.End"/> it steps past it at once — which is what stops a path
    /// that <em>opens</em> with one (as level 1's does) from hanging on it.
    /// </remarks>
    public static AiPatrolRoute? Start(AiPatrolPath path, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

        AiPatrolRoute route = new(path);

        // 0x453880: walk to the start index, remembering what was passed. Note the original records only
        // the last movement command here, never the pair -- PreviousMoveIndex stays unset until the first
        // advance past a movement command.
        int index = 0;
        for (; index < path.Commands.Count && index < startIndex; index++)
        {
            AiPatrolCommandKind kind = path.Commands[index].Kind;
            if (kind.IsMovement())
            {
                route.LastMoveIndex = index;
            }
            else if (kind == AiPatrolCommandKind.End)
            {
                route.EndIndex = index;
            }
        }

        if (index >= path.Commands.Count)
        {
            return null;
        }

        route.CurrentIndex = index;
        if (path.Commands[index].Kind == AiPatrolCommandKind.End)
        {
            int next = route.Peek();
            if (next == NoCommand)
            {
                return null;
            }

            route.CurrentIndex = next;
        }

        return route;
    }

    /// <summary>Advances to the next command — <c>sub_453760</c>.</summary>
    /// <returns>
    /// <see langword="true"/> when the cursor moved; <see langword="false"/> when the patrol stopped, in
    /// which case the current command is left as it was.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The command list runs to its end and then wraps back to the command after the
    /// <see cref="AiPatrolCommandKind.End"/>. Two <see cref="AiPatrolCommandKind.End"/>s in a row are
    /// fatal in the original (<c>"END command following END command"</c>); here they stop the patrol
    /// rather than halting the process, which is the one deliberate divergence in this class.
    /// </para>
    /// <para>
    /// <b>The wrap happens once.</b> <see cref="EndIndex"/> is both the wrap target and the
    /// already-wrapped marker, and the guard that reads it is tested <em>before</em> it is written — so on
    /// the second time the list runs out, the peek returns the recorded
    /// <see cref="AiPatrolCommandKind.End"/>, the guard matches, and the cursor stops where it is. A patrol
    /// therefore runs its prologue, runs its loop body once more, and then repeats its final command. This
    /// is read behaviour, not an interpretation: see §7.3.
    /// </para>
    /// </remarks>
    public bool Advance()
    {
        Deadline = NoDeadline;

        int next = Peek();
        if (next == NoCommand)
        {
            HasStopped = true;
            return false;
        }

        AiPatrolCommandKind kind = Path.Commands[next].Kind;
        if (kind == AiPatrolCommandKind.End)
        {
            if (next == EndIndex)
            {
                // Already wrapped through this END once; the cursor is left untouched.
                HasStopped = true;
                return false;
            }

            int before = next;
            CurrentIndex = next;
            EndIndex = next;

            next = Peek();
            if (next == NoCommand || next == before)
            {
                HasStopped = true;
                return false;
            }

            kind = Path.Commands[next].Kind;
            if (kind == AiPatrolCommandKind.End)
            {
                // "END command following END command" -- fatal in the original.
                HasStopped = true;
                return false;
            }
        }

        if (kind.IsMovement())
        {
            PreviousMoveIndex = LastMoveIndex;
            LastMoveIndex = next;
        }

        CurrentIndex = next;
        return true;
    }

    /// <summary>Looks at where the cursor would go next without moving it — <c>sub_453730</c>.</summary>
    /// <returns>The command index, or <see cref="NoCommand"/>.</returns>
    /// <remarks>
    /// Three sources in order: the next command in the list; failing that the recorded
    /// <see cref="AiPatrolCommandKind.End"/>; failing that the path's first command. In the original the
    /// first test is <c>next != 0 &amp;&amp; *next != 0</c>, where the second half is not "the next command
    /// has a successor" but "the next pointer is not the list's end marker" — the engine terminates a child
    /// list with a pointer to a slot holding zero, so <c>*p == 0</c> is how it recognises the end.
    /// Mistaking that for a successor test reads as the last command being unreachable.
    /// </remarks>
    private int Peek()
    {
        if (CurrentIndex >= 0 && CurrentIndex + 1 < Path.Commands.Count)
        {
            return CurrentIndex + 1;
        }

        if (EndIndex != NoCommand)
        {
            return EndIndex;
        }

        return Path.Commands.Count > 0 ? 0 : NoCommand;
    }

    /// <summary>Applies the current command's own effect on the cursor's state.</summary>
    /// <param name="tick">The engine tick counter, which a delay's deadline is measured against.</param>
    /// <returns><see langword="true"/> when the command finishes on the tick it starts.</returns>
    /// <remarks>
    /// Only the state the cursor owns is here — the gait, the crouch flag and the delay deadline. The
    /// movement, animation and turning a command drives belong to the AI, because the walker reaches out
    /// to the body for those.
    /// </remarks>
    public bool Begin(int tick)
    {
        if (Current is not { } command)
        {
            return false;
        }

        switch (command.Kind)
        {
            case AiPatrolCommandKind.WalkTo:
                IsCrouching = false;
                IsWalking = true;
                return false;

            case AiPatrolCommandKind.RunTo:
                IsCrouching = false;
                IsWalking = false;
                return false;

            case AiPatrolCommandKind.Crouch:
                IsCrouching = true;
                return true;

            case AiPatrolCommandKind.Delay:
                if (Deadline == NoDeadline)
                {
                    Deadline = command.Operand + tick;
                }

                // The walker advances on strictly greater, so a delay of n occupies n + 1 ticks.
                return tick > Deadline;

            default:
                return false;
        }
    }
}
