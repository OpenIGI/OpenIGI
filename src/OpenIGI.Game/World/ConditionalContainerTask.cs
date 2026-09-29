using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's <c>ConditionalContainer</c> — a container whose children are only <em>registered into the
/// world</em> while its <c>Condition</c> holds. The original's <c>ConditionalContainer_RunHandler</c>
/// (<c>0x520EF0</c> in the shared engine).
/// </summary>
/// <remarks>
/// <para>
/// The three parameters are the ones level data declares
/// (<c>Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start",
/// "VarString", "Run at stop", "VarString")</c>), in that order. Each frame the original evaluates
/// <c>Condition</c>; on the transition to true it runs <c>Run at start</c> and calls
/// <c>ConditionalContainer_RegisterChildren</c> (<c>0x520D20</c>), which fires each child's
/// <c>DynCubeObj</c> register event so the subtree becomes visible, collidable and ticked; on the
/// transition to false it runs <c>Run at stop</c> and <c>ConditionalContainer_RemoveChildren</c>
/// (<c>0x520DC0</c>), which unregisters them. The children remain <em>tasks</em> throughout — so their
/// exported script variables keep resolving — they are simply absent from the world while the condition
/// is false.
/// </para>
/// <para>
/// <b>Why the port needs this.</b> Level 1 places its intro and outro cut-scene actors inside
/// <c>ConditionalContainer</c>s gated on cut-scene / mission-progress state that is false during ordinary
/// play (<c>ConditionalContainer_4001</c> "Intro cutscene", <c>4007</c> "Outro Cutscene"). Among them is a
/// second David Jones — <c>HumanSoldier</c> "Jones drive truck" on the player's own model
/// <c>000_01_1</c> — plus the intro heli, trains and a train-top Jones. With no
/// <c>ConditionalContainer</c> type the port built every one of them as a plain container and rendered its
/// children, so those actors stood in the level at mission start where the original never registers them.
/// The player and the real guards sit under <c>ConditionalContainer_1495</c>, whose condition
/// <c>!4001.isRun &amp;&amp; !4007.isRun</c> is true during play, so they are unaffected.
/// </para>
/// <para>
/// <see cref="ConditionalContainerGate"/> prepares the evaluator, and the level walk
/// (<see cref="LevelUpdateWalker"/>) runs <see cref="Update"/> when it reaches the container. Render, spawn, AI and
/// collision paths read the registration, <see cref="IsActive"/>; script conditions read <see cref="IsRun"/>.
/// </para>
/// </remarks>
public partial class ConditionalContainerTask : ContainerTask
{
    /// <summary>The script name the level data instantiates this type under.</summary>
    public const string TypeName = "ConditionalContainer";

    private bool? _isRun;
    private bool _resolving;

    /// <summary>The expression that decides whether the subtree is registered — <c>Condition</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>The expression run once when the container starts — <c>Run at start</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at start")]
    public string RunAtStart { get; set; } = string.Empty;

    /// <summary>The expression run once when the container stops — <c>Run at stop</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at stop")]
    public string RunAtStop { get; set; } = string.Empty;

    /// <summary>
    /// Evaluates <see cref="Condition"/> to <see langword="true"/> or <see langword="false"/>, or
    /// <see langword="null"/> when no gate has wired one — in which case the container is treated as
    /// running, so an un-prepared tree renders exactly as it did before.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="ConditionalContainerGate.Prepare"/>. It is a closure rather than a stored value
    /// because a container's condition can name another container's <c>isRun</c>
    /// (<c>1495</c> reads <c>4001</c> and <c>4007</c>), and evaluating one lazily pulls the others through
    /// their exported variables.
    /// </remarks>
    internal Func<ConditionalContainerTask, bool>? ConditionResolver { get; set; }

    /// <summary>
    /// Gets whether the container is running, and therefore whether its subtree is registered into the
    /// world — the original's <c>isRun</c> byte (<c>+284</c>).
    /// </summary>
    /// <remarks>
    /// Between refreshes this returns the last latched value. A container whose condition transitively references itself reads its own
    /// <see cref="IsRun"/> as <see langword="false"/> mid-resolution, which matches the original starting
    /// every container un-run and only latching true once the condition first evaluates true.
    /// </remarks>
    public bool IsRun
    {
        get
        {
            if (_isRun is { } cached)
            {
                return cached;
            }

            if (ConditionResolver is null)
            {
                // No gate prepared this tree: fall back to "running" so nothing is silently hidden.
                return true;
            }

            if (_resolving)
            {
                // A cycle in the condition graph: the original has not latched this container true yet.
                return false;
            }

            _resolving = true;
            try
            {
                _isRun = ConditionResolver(this);
            }
            finally
            {
                _resolving = false;
            }

            return _isRun.Value;
        }
    }

    /// <summary>
    /// Gets or sets whether the container's children are registered in the world -- the original's <c>active</c>
    /// byte (<c>+274</c>), distinct from <see cref="IsRun"/>.
    /// </summary>
    /// <remarks>
    /// A container activates its subtree in the same update its condition first holds, but on the tick the
    /// condition fails it still runs its children once more and only deactivates them the tick after -- so
    /// <c>active</c> drops a tick after <c>run</c>. The mission loader
    /// starts it equal to <see cref="IsRun"/>.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>The container's update in the level walk -- <c>0x4874E0</c>.</summary>
    /// <param name="walker">The walk.</param>
    /// <remarks>
    /// The condition is read when the walk reaches the container. On a rising edge the children are brought
    /// into the world and run in the same call; while the condition held last tick they run once more; a
    /// container false this tick and last takes its children out of the world and runs none of them. The start
    /// and stop expressions run after the children.
    /// </remarks>
    public void Update(LevelUpdateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        bool old = _isRun ?? false;
        bool run = walker.HoldContainer?.Invoke(this) == true
            ? old
            : ConditionResolver?.Invoke(this) ?? true;
        _isRun = run;
        if (run || old)
        {
            if (!IsActive)
            {
                IsActive = true;
                LevelUpdateWalker.Activate(this);
            }

            walker.WalkChildren(this);
        }
        else if (IsActive)
        {
            IsActive = false;
            walker.Deactivate(this);
        }

        if (run != old)
        {
            walker.ContainerTransition?.Invoke(this, run);
        }
    }

    /// <summary>Re-evaluates the live condition and returns whether the registration state changed.</summary>
    /// <param name="started"><see langword="true"/> when this transition registered the subtree.</param>
    internal bool Refresh(out bool started)
    {
        bool before = _isRun ?? false;
        bool after = ConditionResolver?.Invoke(this) ?? true;
        _isRun = after;
        started = !before && after;
        return before != after;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        // Other tasks read this: LevelTimer_999 gates on ConditionalContainer_4001.isRun, and the player's
        // own container 1495 gates on 4001/4007's isRun. Bound writable because the original binds the byte
        // writable (QVM_BindVar); a write pins the memoised result.
        variables.BindBool(this, "isRun", () => IsRun, value => _isRun = value);
    }
}
