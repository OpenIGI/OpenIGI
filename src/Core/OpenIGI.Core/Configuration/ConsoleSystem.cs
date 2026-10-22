namespace OpenIGI.Core.Configuration;

/// <summary>
/// Executes console input against the variable and command registries.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place that decides what a line of text means. A statement's first token is
/// resolved in order — alias, command, console variable — so a variable can be read by naming it and
/// written by naming it with a value, exactly as players expect, while an alias can shadow either.
/// </para>
/// <para>
/// The same entry point serves the on-screen console, the start-up <c>+command</c> arguments and the
/// settings file, which is what makes <c>settings.cfg</c> a plain script rather than a second format
/// with its own parser.
/// </para>
/// </remarks>
public sealed class ConsoleSystem
{
    /// <summary>How deep alias expansion may nest before it is treated as a loop.</summary>
    private const int MaxAliasDepth = 16;

    private readonly List<IConsoleOutput> _outputs = [];
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _binds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _kept = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initialises a new instance of the <see cref="ConsoleSystem"/> class.</summary>
    /// <param name="variables">The variable registry, or <see langword="null"/> for <see cref="CVarRegistry.Default"/>.</param>
    /// <param name="commands">The command registry, or <see langword="null"/> to create an empty one.</param>
    public ConsoleSystem(CVarRegistry? variables = null, CommandRegistry? commands = null)
    {
        Variables = variables ?? CVarRegistry.Default;
        Commands = commands ?? new CommandRegistry();
        Variables.Added += AdoptKeptSetting;
    }

    /// <summary>Occurs for every line written to the console.</summary>
    public event EventHandler<ConsoleLine>? LineWritten;

    /// <summary>Gets the variable registry this console reads and writes.</summary>
    public CVarRegistry Variables { get; }

    /// <summary>Gets the command registry this console dispatches through.</summary>
    public CommandRegistry Commands { get; }

    /// <summary>Gets or sets a value indicating whether commands and variables flagged as cheats may be used.</summary>
    public bool CheatsEnabled { get; set; }

    /// <summary>Gets or sets the source of the seconds stamped onto each line written.</summary>
    /// <remarks>
    /// <para>
    /// A function rather than a clock type, so this project keeps its independence from any particular
    /// timing source and a test can hand it a number it chose. It counts from whenever the host decides
    /// the session began; nothing here interprets the value beyond passing it on.
    /// </para>
    /// <para>
    /// The default returns zero, which is what makes a line's timestamp optional rather than a
    /// requirement — a host that never sets this simply gets unstamped lines.
    /// </para>
    /// </remarks>
    public Func<double> Clock { get; set; } = static () => 0.0;

    /// <summary>Gets the seconds the clock currently reports.</summary>
    public double Elapsed => Clock();

    /// <summary>Gets the registered aliases, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>Gets the key bindings, keyed by key name.</summary>
    /// <remarks>
    /// The console owns the table but knows nothing about input devices; the platform shell reads it
    /// and calls <see cref="Execute"/> when a bound key goes down.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Binds => _binds;

    /// <summary>Gets the values a settings file gave mods' variables that nothing has declared this run, keyed by name.</summary>
    /// <remarks>
    /// A mod switched off declares none of its variables, so its lines would otherwise be dropped as retired settings and lost
    /// at the next save; they are kept as they were read and written back, until the mod is on again and declares them. A
    /// variable declared after the settings were read -- a mod's, as its game starts -- takes its kept value then.
    /// </remarks>
    public IReadOnlyDictionary<string, string> KeptSettings => _kept;

    /// <summary>Keeps the value a settings file gave a mod's variable that nothing has declared this run.</summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="value">The value, as the file wrote it.</param>
    public void KeepSetting(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _kept[name] = value;
    }

    // A mod's variable declared after the settings were read takes the value they kept for it.
    private void AdoptKeptSetting(object? sender, CVarAddedEventArgs e)
    {
        if (_kept.Remove(e.Variable.Name, out string? value))
        {
            e.Variable.TrySetText(value, out _);
        }
    }

    /// <summary>Adds a sink that receives every line the console writes.</summary>
    /// <param name="output">The sink to add.</param>
    public void AddOutput(IConsoleOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Add(output);
    }

    /// <summary>Removes a previously added sink.</summary>
    /// <param name="output">The sink to remove.</param>
    /// <returns><see langword="true"/> when the sink was registered.</returns>
    public bool RemoveOutput(IConsoleOutput output) => _outputs.Remove(output);

    /// <summary>Writes a line to every sink.</summary>
    /// <param name="line">The line to write.</param>
    public void Write(ConsoleLine line)
    {
        foreach (IConsoleOutput output in _outputs)
        {
            output.Write(line);
        }

        LineWritten?.Invoke(this, line);
    }

    /// <summary>Writes text to every sink.</summary>
    /// <param name="text">The text to write.</param>
    /// <param name="severity">How the line should be presented.</param>
    /// <remarks>Stamped with <see cref="Elapsed"/>, so a line written by a command is timed like one mirrored from the log.</remarks>
    public void Write(string text, ConsoleSeverity severity = ConsoleSeverity.Output) =>
        Write(new ConsoleLine(text, severity, string.Empty, Elapsed));

    /// <summary>Runs a line of console input, which may hold several statements.</summary>
    /// <param name="line">The input to run.</param>
    /// <param name="echo">Whether the input is echoed above its output, as it is for typed commands.</param>
    /// <returns><see langword="true"/> when every statement was understood.</returns>
    public bool Execute(string? line, bool echo = false)
    {
        if (echo && !string.IsNullOrWhiteSpace(line))
        {
            Write(new ConsoleLine($"] {line.Trim()}", ConsoleSeverity.Echo, string.Empty, Elapsed));
        }

        bool ok = true;
        foreach (string statement in CommandTokenizer.SplitStatements(line))
        {
            ok &= ExecuteStatement(statement, 0);
        }

        return ok;
    }

    /// <summary>Runs the command bound to a key.</summary>
    /// <param name="key">The key name, as used by <c>bind</c>.</param>
    /// <returns><see langword="true"/> when a binding existed and ran without error.</returns>
    public bool ExecuteBind(string key) =>
        _binds.TryGetValue(key ?? string.Empty, out string? command) && Execute(command);

    /// <summary>Defines or replaces an alias.</summary>
    /// <param name="name">The alias name.</param>
    /// <param name="expansion">The text the alias expands to. An empty value removes the alias.</param>
    public void SetAlias(string name, string expansion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.IsNullOrEmpty(expansion))
        {
            _aliases.Remove(name);
            return;
        }

        _aliases[name] = expansion;
    }

    /// <summary>Binds a key to a command, or clears the binding when <paramref name="command"/> is empty.</summary>
    /// <param name="key">The key name.</param>
    /// <param name="command">The command to run, or an empty string to unbind.</param>
    public void SetBind(string key, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (string.IsNullOrEmpty(command))
        {
            _binds.Remove(key);
            return;
        }

        _binds[key] = command;
    }

    /// <summary>Suggests completions for a partially typed line.</summary>
    /// <param name="input">The text typed so far.</param>
    /// <returns>The candidates and the longest prefix they share.</returns>
    /// <remarks>
    /// Completion applies to the first token only. A partially typed value is left alone, because the
    /// useful thing to show there is the variable's range, which the console prints as the line is
    /// typed rather than on demand.
    /// </remarks>
    public ConsoleCompletion Complete(string? input)
    {
        string prefix = (input ?? string.Empty).TrimStart();
        if (prefix.Contains(' ', StringComparison.Ordinal))
        {
            return ConsoleCompletion.None;
        }

        List<string> names = [];

        foreach (ConsoleCommand command in Commands.All)
        {
            if ((command.Flags & CommandOptions.Hidden) == 0 &&
                command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(command.Name);
            }
        }

        foreach (ICVar variable in Variables.All)
        {
            if ((variable.Flags & CVarOptions.Hidden) == 0 &&
                variable.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(variable.Name);
            }
        }

        foreach (string alias in _aliases.Keys)
        {
            if (alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(alias);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return new ConsoleCompletion(names, LongestCommonPrefix(names, prefix));
    }

    /// <summary>Ranks what the reader might mean by a partially typed line, best first.</summary>
    /// <param name="input">The line as typed so far.</param>
    /// <param name="limit">The most candidates to return.</param>
    /// <returns>The candidates, best first.</returns>
    /// <remarks>
    /// The rich counterpart to <see cref="Complete"/>, which is deliberately left alone: that answers
    /// what Tab may safely insert, this answers what to show while the reader is still deciding. Unlike
    /// <see cref="Complete"/> it matches loosely, ranks its results, and completes a half-typed
    /// <em>value</em> once the line has named a variable.
    /// </remarks>
    public IReadOnlyList<ConsoleSuggestion> Suggest(string? input, int limit = 32) =>
        ConsoleSuggester.Suggest(this, input, limit);

    /// <summary>Describes a variable the way the console prints it when named without a value.</summary>
    /// <param name="variable">The variable to describe.</param>
    /// <returns>A single line naming the current value, the default, the range and the help text.</returns>
    public static string Describe(ICVar variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        string line = $"{variable.Name} = {CommandTokenizer.Quote(variable.ValueText)}";

        if (!variable.IsDefault)
        {
            line += $"  (default {CommandTokenizer.Quote(variable.DefaultText)})";
        }

        if (variable.RangeText.Length > 0)
        {
            line += $"  [{variable.RangeText}]";
        }

        if (variable.Help.Length > 0)
        {
            line += $"  -- {variable.Help}";
        }

        return line;
    }

    private bool ExecuteStatement(string statement, int depth)
    {
        IReadOnlyList<string> tokens = CommandTokenizer.Tokenize(statement);
        if (tokens.Count == 0)
        {
            return true;
        }

        string name = tokens[0];

        if (_aliases.TryGetValue(name, out string? expansion))
        {
            if (depth >= MaxAliasDepth)
            {
                Write($"Alias '{name}' expands too deeply; stopped to avoid a loop.", ConsoleSeverity.Error);
                return false;
            }

            // Arguments typed after the alias are appended, so `alias fogmax "r_fog_quality"; fogmax 4` works.
            string tail = tokens.Count > 1 ? " " + string.Join(' ', tokens.Skip(1).Select(CommandTokenizer.Quote)) : string.Empty;

            bool ok = true;
            foreach (string inner in CommandTokenizer.SplitStatements(expansion + tail))
            {
                ok &= ExecuteStatement(inner, depth + 1);
            }

            return ok;
        }

        if (Commands.TryGet(name, out ConsoleCommand? command))
        {
            return Invoke(command, tokens);
        }

        if (Variables.TryGet(name, out ICVar? variable))
        {
            return tokens.Count == 1 ? Report(variable) : Assign(variable, tokens);
        }

        Write($"Unknown command or variable '{name}'. Try 'find {name}'.", ConsoleSeverity.Error);
        return false;
    }

    private bool Invoke(ConsoleCommand command, IReadOnlyList<string> tokens)
    {
        if ((command.Flags & CommandOptions.Cheat) != 0 && !CheatsEnabled)
        {
            Write($"'{command.Name}' is a cheat command and cheats are disabled.", ConsoleSeverity.Error);
            return false;
        }

        command.Execute(new ConsoleCommandContext(this, command.Name, [.. tokens.Skip(1)]));
        return true;
    }

    private bool Report(ICVar variable)
    {
        Write(Describe(variable));
        return true;
    }

    private bool Assign(ICVar variable, IReadOnlyList<string> tokens)
    {
        if ((variable.Flags & CVarOptions.ReadOnly) != 0)
        {
            Write($"'{variable.Name}' is read-only.", ConsoleSeverity.Error);
            return false;
        }

        if ((variable.Flags & CVarOptions.Cheat) != 0 && !CheatsEnabled)
        {
            Write($"'{variable.Name}' is a cheat variable and cheats are disabled.", ConsoleSeverity.Error);
            return false;
        }

        // Everything after the name is the value, so unquoted text with spaces still assigns sensibly.
        string value = tokens.Count == 2 ? tokens[1] : string.Join(' ', tokens.Skip(1));

        if (!variable.TrySetText(value, out string? reason))
        {
            Write(reason ?? $"'{value}' is not valid for {variable.Name}.", ConsoleSeverity.Error);
            return false;
        }

        if ((variable.Flags & CVarOptions.RequiresRestart) != 0)
        {
            Write($"{variable.Name} takes effect after a restart.", ConsoleSeverity.Notice);
        }

        return true;
    }

    private static string LongestCommonPrefix(List<string> names, string fallback)
    {
        if (names.Count == 0)
        {
            return fallback;
        }

        string prefix = names[0];
        foreach (string name in names)
        {
            int length = 0;
            while (length < prefix.Length && length < name.Length &&
                   char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(name[length]))
            {
                length++;
            }

            prefix = prefix[..length];
        }

        return prefix;
    }
}
