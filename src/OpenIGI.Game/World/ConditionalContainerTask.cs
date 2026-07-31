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
/// The port does not run cut-scenes, so this evaluates the condition once against the level's initial
/// playable state and exposes the result as <see cref="IsRun"/>; the render, spawn and collision walks
/// skip a container whose <see cref="IsRun"/> is false. See <see cref="ConditionalContainerGate"/>.
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
    /// Memoised on first read. A container whose condition transitively references itself reads its own
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
