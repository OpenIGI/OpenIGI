using System.Globalization;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One node of the task tree: the single object model the whole game is built from.
/// </summary>
/// <remarks>
/// <para>
/// Player, soldiers, weapons, doors, cars, menus, HUD and mission logic are all task types. A task is a
/// tree node with a type, an ordered child list, an optional script id and a set of lifecycle hooks that
/// a tree walk invokes.
/// </para>
/// <para>
/// The original's 32-byte header (<c>0x4012F0</c>) keeps its children in an intrusive doubly-linked list
/// whose header doubles as the list's own sentinel. That list is reproduced here with plain
/// <see cref="NextSibling"/>/<see cref="PreviousSibling"/> links rather than an index into a collection,
/// because the tree walk hands out live cursors into it: a task may delete itself, a sibling or a whole
/// subtree from inside its own update, and the surviving cursors have to stay valid.
/// </para>
/// </remarks>
public abstract class GameTask
{
    private TaskType? _type;

    /// <summary>Gets the id a task that was never given a script id reports.</summary>
    public const int NoId = -1;

    private TaskTree? _tree;

    /// <summary>Gets the task's type.</summary>
    /// <exception cref="InvalidOperationException">The task has not been created through a <see cref="TaskTree"/>.</exception>
    public TaskType Type =>
        _type ?? throw new InvalidOperationException(
            $"The task of CLR type '{GetType().Name}' was not created through a {nameof(TaskTree)} and has no task type.");

    /// <summary>Gets the tree this task belongs to.</summary>
    /// <exception cref="InvalidOperationException">The task has not been created through a <see cref="TaskTree"/>.</exception>
    /// <remarks>
    /// The original reaches the tree through globals — the id table, the level root, the walker's cursor
    /// stack — which every task can touch from anywhere. A back-reference gives handlers the same reach
    /// without the process-wide state, so two trees can exist side by side.
    /// </remarks>
    public TaskTree Tree =>
        _tree ?? throw new InvalidOperationException(
            $"The task of CLR type '{GetType().Name}' was not created through a {nameof(TaskTree)}.");

    /// <summary>Gets the parent task, or <see langword="null"/> for a tree root.</summary>
    public GameTask? Parent { get; private set; }

    /// <summary>Gets the first child, or <see langword="null"/> when there are none.</summary>
    public GameTask? FirstChild { get; private set; }

    /// <summary>Gets the last child, or <see langword="null"/> when there are none.</summary>
    public GameTask? LastChild { get; private set; }

    /// <summary>Gets the next sibling, or <see langword="null"/> when this is the last child.</summary>
    public GameTask? NextSibling { get; private set; }

    /// <summary>Gets the previous sibling, or <see langword="null"/> when this is the first child.</summary>
    public GameTask? PreviousSibling { get; private set; }

    /// <summary>Gets the script id bound to this task, or <see cref="NoId"/> when it has none.</summary>
    /// <remarks>
    /// Ids come from the first argument of the <c>Task_New</c> script command and index a 4096-slot table.
    /// A task created with <c>-1</c>, or whose id was already taken, keeps <see cref="NoId"/>.
    /// </remarks>
    public int Id { get; internal set; } = NoId;

    /// <summary>Gets a value indicating whether this task has been deleted.</summary>
    public bool IsDeleted { get; internal set; }

    /// <summary>Gets a value indicating whether this task has exported its named variables.</summary>
    /// <remarks>
    /// The original keeps this as a guard byte on each exporting type (for example <c>+168</c> at
    /// <c>0x41E5A0</c>) so a second <c>ExportVars</c> cannot bind the same symbols twice.
    /// </remarks>
    public bool IsExported { get; internal set; }

    /// <summary>
    /// Gets the name mission scripts refer to this task by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Task_GetName</c> (<c>0x4F1270</c>) formats <c>"%s_%d"</c> from the type name and the script id
    /// unless the type handles the GetName message. The friendly label passed as the third argument of
    /// <c>Task_New</c> is <em>discarded</em> by the retail build, so it is not the name.
    /// </para>
    /// <para>
    /// Confirmed against retail level data: <c>location0/level1</c> expressions read
    /// <c>AlarmControl_98.isAlarm</c> and <c>Switch_94.isLastPressed</c>, never the labels the
    /// corresponding <c>Task_New</c> calls carry.
    /// </para>
    /// </remarks>
    public virtual string Name =>
        string.Create(CultureInfo.InvariantCulture, $"{Type.Name}_{Id}");

    /// <summary>Gets the editor label the script passed, which the engine itself never uses.</summary>
    /// <remarks>Kept because it is the only human-readable identifier in level data, and tools want it.</remarks>
    public string Label { get; internal set; } = string.Empty;

    /// <summary>Gets the children in order.</summary>
    /// <remarks>
    /// Enumeration reads the live links, so adding or removing children while enumerating behaves like
    /// the original's walk rather than throwing. Use <see cref="TaskTreeWalker"/> for anything that has to
    /// survive a task deleting itself.
    /// </remarks>
    public IEnumerable<GameTask> Children
    {
        get
        {
            for (GameTask? child = FirstChild; child is not null; child = child.NextSibling)
            {
                yield return child;
            }
        }
    }

    /// <summary>Gets the number of children.</summary>
    public int ChildCount
    {
        get
        {
            int count = 0;
            for (GameTask? child = FirstChild; child is not null; child = child.NextSibling)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>Delivers a message to this task alone, without recursing into children.</summary>
    /// <param name="message">The message to deliver.</param>
    /// <remarks>
    /// This is the C# stand-in for indexing the original's vtable. Method resolution is ordinary virtual
    /// dispatch, which is equivalent to the original's copy-on-register plus push-down-on-set scheme for
    /// the start-up-time registration the original actually performs. See the re-note, §2.2 and §11.
    /// </remarks>
    public void Receive(TaskMessage message)
    {
        switch (message)
        {
            case TaskMessage.Create: OnCreate(); break;
            case TaskMessage.Update: OnUpdate(); break;
            case TaskMessage.Destroy: OnDestroy(); break;
            case TaskMessage.PreDestroy: OnPreDestroy(); break;
            case TaskMessage.Render: OnRender(); break;
            case TaskMessage.Parsed: OnParsed(); break;
            case TaskMessage.PostLoad: OnPostLoad(); break;
            default: OnMessage(message); break;
        }
    }

    /// <summary>Called once, immediately after the task is linked into the tree.</summary>
    protected virtual void OnCreate()
    {
    }

    /// <summary>Called once per 30 Hz logic tick.</summary>
    protected virtual void OnUpdate()
    {
    }

    /// <summary>Called once per rendered frame. Must not mutate logic state.</summary>
    protected virtual void OnRender()
    {
    }

    /// <summary>Called at the start of deletion, before any child is destroyed.</summary>
    /// <remarks>Message 3 in the original, sent first by <c>Task_Delete</c> (<c>0x4015F0</c>).</remarks>
    protected virtual void OnPreDestroy()
    {
    }

    /// <summary>Called at the end of deletion, after every child has been destroyed.</summary>
    /// <remarks>The task is still linked into its parent at this point, as it is in the original.</remarks>
    protected virtual void OnDestroy()
    {
    }

    /// <summary>Called once the task's script parameters are bound and its nested children exist.</summary>
    protected virtual void OnParsed()
    {
    }

    /// <summary>Called when the level script that created this task has finished running.</summary>
    protected virtual void OnPostLoad()
    {
    }

    /// <summary>Called for any message without a dedicated hook.</summary>
    /// <param name="message">The message being delivered.</param>
    protected virtual void OnMessage(TaskMessage message)
    {
    }

    /// <summary>Passes a message on to this task's immediate children.</summary>
    /// <param name="message">The message to forward.</param>
    /// <exception cref="InvalidOperationException">The task has not been created through a <see cref="TaskTree"/>.</exception>
    /// <remarks>
    /// <para>
    /// The update message does not broadcast itself down the tree: the frame arbiter delivers it to the
    /// root's children, and each of those decides whether its own subtree ticks. Forwarding is therefore
    /// something a task type opts into, by calling this from <see cref="OnUpdate"/> — which is what the
    /// original's shared <c>Task_UpdateChildren</c> (<c>0x488700</c>) does for the types that install it.
    /// </para>
    /// <para>
    /// A type that does not forward freezes its whole subtree. That is a feature, not an oversight:
    /// <c>Static</c> and <c>Idle</c> install a do-nothing update precisely so that level geometry costs
    /// nothing per tick, and the in-game task stops forwarding to pause the world.
    /// </para>
    /// </remarks>
    protected void ForwardToChildren(TaskMessage message) => Tree.Walker.SendToChildren(this, message);

    /// <summary>Binds this task's exported variables into a registry.</summary>
    /// <param name="variables">The registry to bind into.</param>
    /// <remarks>
    /// Only sent to tasks that were given a script id. Bindings must be live aliases onto fields — the
    /// original binds the field's <em>address</em> (<c>QVM_BindVar</c>, <c>0x4B7E80</c>) and scripts both
    /// read and write through them.
    /// </remarks>
    protected virtual void OnExportVars(ScriptVarRegistry variables)
    {
    }

    /// <summary>Unbinds this task's exported variables.</summary>
    /// <param name="variables">The registry to unbind from.</param>
    protected virtual void OnUnexportVars(ScriptVarRegistry variables)
    {
    }

    /// <summary>Reads this task's script parameters from a level script's argument list.</summary>
    /// <param name="reader">The argument cursor, positioned at the first parameter value.</param>
    /// <remarks>
    /// Overridden by the code the <c>[ScriptParam]</c> source generator emits, so binding costs no
    /// runtime reflection. The declaration order of the attributed members is the wire order.
    /// </remarks>
    protected virtual void BindScriptParameters(ScriptArgumentReader reader)
    {
    }

    /// <summary>Gets the script parameters this task type declares, in wire order.</summary>
    /// <remarks>Overridden by the generated code; empty for a type that declares none.</remarks>
    protected virtual IReadOnlyList<TaskParameter> ScriptParameters => [];

    /// <inheritdoc/>
    public override string ToString() => _type is null ? GetType().Name : Name;

    // The lifecycle hooks are `protected` so a task class in any assembly overrides them with the same
    // modifier. Everything outside GameTask reaches them through these invokers.
    internal IReadOnlyList<TaskParameter> GetDeclaredScriptParameters() => ScriptParameters;

    internal void InvokeBindScriptParameters(ScriptArgumentReader reader) => BindScriptParameters(reader);

    internal void InvokeExportVars(ScriptVarRegistry variables) => OnExportVars(variables);

    internal void InvokeUnexportVars(ScriptVarRegistry variables) => OnUnexportVars(variables);

    internal void SetType(TaskType type) => _type = type;

    internal void SetTree(TaskTree tree) => _tree = tree;

    /// <summary>Appends a child at the end of the child list, as <c>Task_New</c> does.</summary>
    internal void AppendChild(GameTask child)
    {
        child.Parent = this;
        child.PreviousSibling = LastChild;
        child.NextSibling = null;

        if (LastChild is null)
        {
            FirstChild = child;
        }
        else
        {
            LastChild.NextSibling = child;
        }

        LastChild = child;
    }

    /// <summary>Inserts a child at the head of the child list, as <c>Task_NewSibling</c> does.</summary>
    internal void PrependChild(GameTask child)
    {
        child.Parent = this;
        child.NextSibling = FirstChild;
        child.PreviousSibling = null;

        if (FirstChild is null)
        {
            LastChild = child;
        }
        else
        {
            FirstChild.PreviousSibling = child;
        }

        FirstChild = child;
    }

    /// <summary>Detaches this task from its parent's child list.</summary>
    internal void Unlink()
    {
        if (Parent is null)
        {
            return;
        }

        if (PreviousSibling is null)
        {
            Parent.FirstChild = NextSibling;
        }
        else
        {
            PreviousSibling.NextSibling = NextSibling;
        }

        if (NextSibling is null)
        {
            Parent.LastChild = PreviousSibling;
        }
        else
        {
            NextSibling.PreviousSibling = PreviousSibling;
        }

        NextSibling = null;
        PreviousSibling = null;
        Parent = null;
    }
}
