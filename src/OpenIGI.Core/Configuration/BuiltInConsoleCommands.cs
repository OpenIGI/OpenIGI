using System.Globalization;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// The commands every console has, independent of what game is running.
/// </summary>
/// <remarks>
/// Anything that needs to know about the game — loading a mission, quitting, taking a screenshot —
/// is registered by the host instead, so this stays testable with nothing but a registry.
/// </remarks>
public static class BuiltInConsoleCommands
{
    /// <summary>Registers the built-in commands on a console.</summary>
    /// <param name="console">The console to register on.</param>
    /// <param name="files">Where <c>exec</c> reads scripts from, or <see langword="null"/> to disable it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    public static void Register(ConsoleSystem console, IConsoleFileSystem? files = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        CommandRegistry commands = console.Commands;

        commands.Add("help", "Lists commands, or explains one.", Help, "[name]");
        commands.Add("find", "Lists variables and commands whose name contains a fragment.", Find, "<fragment>");
        commands.Add("cvarlist", "Lists console variables.", CVarList, "[fragment]");
        commands.Add("cmdlist", "Lists console commands.", CommandList, "[fragment]");
        commands.Add("reset", "Restores a variable to its default.", Reset, "<variable>");
        commands.Add("toggle", "Flips a switch, or steps a variable through the values given.", Toggle, "<variable> [values...]");
        commands.Add("inc", "Adds an amount to a numeric variable.", Increment, "<variable> [amount]");
        commands.Add("echo", "Prints its arguments.", Echo, "<text...>");
        commands.Add("alias", "Defines a named script, or lists the aliases.", Alias, "[name] [command...]");
        commands.Add("unalias", "Removes an alias.", Unalias, "<name>");
        commands.Add("bind", "Binds a key to a command, or lists the bindings.", Bind, "[key] [command...]");
        commands.Add("unbind", "Clears a key binding.", Unbind, "<key>");
        commands.Add("dump_settings", "Prints every variable that differs from its default.", DumpSettings, "[fragment]");

        if (files is not null)
        {
            commands.Add("exec", "Runs a console script.", context => Exec(context, files), "<file>");
        }
    }

    private static void Help(ConsoleCommandContext context)
    {
        if (context.Count == 0)
        {
            context.Print("Commands:");
            foreach (ConsoleCommand command in context.Console.Commands.Search(string.Empty))
            {
                context.Print($"  {command.Signature,-38} {command.Help}");
            }

            context.Print("Type 'cvarlist' for variables, or 'find <text>' to search both.");
            return;
        }

        string name = context[0];

        if (context.Console.Commands.TryGet(name, out ConsoleCommand? found))
        {
            context.Print($"{found.Signature}");
            context.Print($"  {found.Help}");
            return;
        }

        if (context.Console.Variables.TryGet(name, out ICVar? variable))
        {
            context.Print(ConsoleSystem.Describe(variable));
            return;
        }

        context.Error($"Nothing called '{name}'.");
    }

    private static void Find(ConsoleCommandContext context)
    {
        string fragment = context[0];
        int hits = 0;

        foreach (ConsoleCommand command in context.Console.Commands.Search(fragment))
        {
            context.Print($"  [cmd] {command.Signature,-32} {command.Help}");
            hits++;
        }

        foreach (ICVar variable in context.Console.Variables.Search(fragment))
        {
            context.Print($"  [var] {ConsoleSystem.Describe(variable)}");
            hits++;
        }

        context.Print(hits == 0 ? $"Nothing matches '{fragment}'." : $"{hits} match(es).");
    }

    private static void CVarList(ConsoleCommandContext context)
    {
        foreach (ICVar variable in context.Console.Variables.Search(context[0]))
        {
            context.Print($"  {ConsoleSystem.Describe(variable)}");
        }
    }

    private static void CommandList(ConsoleCommandContext context)
    {
        foreach (ConsoleCommand command in context.Console.Commands.Search(context[0]))
        {
            context.Print($"  {command.Signature,-38} {command.Help}");
        }
    }

    private static void Reset(ConsoleCommandContext context)
    {
        if (!TryResolve(context, context[0], out ICVar? variable))
        {
            return;
        }

        variable.Reset();
        context.Print(ConsoleSystem.Describe(variable));
    }

    private static void Toggle(ConsoleCommandContext context)
    {
        if (!TryResolve(context, context[0], out ICVar? variable))
        {
            return;
        }

        // With no values given a switch flips; with values given the variable steps through them,
        // which is how one key cycles a quality preset.
        if (context.Count <= 1)
        {
            string next = variable.ValueText is "0" or "false" or "off" ? "1" : "0";
            Apply(context, variable, next);
            return;
        }

        int current = -1;
        for (int i = 1; i < context.Count; i++)
        {
            if (string.Equals(context[i], variable.ValueText, StringComparison.OrdinalIgnoreCase))
            {
                current = i;
                break;
            }
        }

        int index = current < 0 ? 1 : (current == context.Count - 1 ? 1 : current + 1);
        Apply(context, variable, context[index]);
    }

    private static void Increment(ConsoleCommandContext context)
    {
        if (!TryResolve(context, context[0], out ICVar? variable))
        {
            return;
        }

        double amount = 1.0;
        if (context.Count > 1 &&
            !double.TryParse(context[1], NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
        {
            context.Error($"'{context[1]}' is not a number.");
            return;
        }

        if (!double.TryParse(variable.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double current))
        {
            context.Error($"'{variable.Name}' does not hold a number.");
            return;
        }

        Apply(context, variable, (current + amount).ToString(CultureInfo.InvariantCulture));
    }

    private static void Echo(ConsoleCommandContext context) => context.Print(context.Rest(0));

    private static void Alias(ConsoleCommandContext context)
    {
        if (context.Count == 0)
        {
            foreach (KeyValuePair<string, string> alias in context.Console.Aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
            {
                context.Print($"  {alias.Key,-20} {CommandTokenizer.Quote(alias.Value)}");
            }

            return;
        }

        if (context.Count == 1)
        {
            context.Print(context.Console.Aliases.TryGetValue(context[0], out string? expansion)
                ? $"{context[0]} = {CommandTokenizer.Quote(expansion)}"
                : $"No alias called '{context[0]}'.");
            return;
        }

        context.Console.SetAlias(context[0], context.Rest(1));
    }

    private static void Unalias(ConsoleCommandContext context)
    {
        if (context.Count == 0)
        {
            context.Error("usage: unalias <name>");
            return;
        }

        context.Console.SetAlias(context[0], string.Empty);
    }

    private static void Bind(ConsoleCommandContext context)
    {
        if (context.Count == 0)
        {
            foreach (KeyValuePair<string, string> bind in context.Console.Binds.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
            {
                context.Print($"  {bind.Key,-16} {CommandTokenizer.Quote(bind.Value)}");
            }

            return;
        }

        if (context.Count == 1)
        {
            context.Print(context.Console.Binds.TryGetValue(context[0], out string? command)
                ? $"{context[0]} = {CommandTokenizer.Quote(command)}"
                : $"'{context[0]}' is not bound.");
            return;
        }

        context.Console.SetBind(context[0], context.Rest(1));
    }

    private static void Unbind(ConsoleCommandContext context)
    {
        if (context.Count == 0)
        {
            context.Error("usage: unbind <key>");
            return;
        }

        context.Console.SetBind(context[0], string.Empty);
    }

    private static void DumpSettings(ConsoleCommandContext context)
    {
        string fragment = context[0];
        int count = 0;

        foreach (ICVar variable in context.Console.Variables.Modified())
        {
            if (fragment.Length > 0 && !variable.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            context.Print($"{variable.Name} {CommandTokenizer.Quote(variable.ValueText)}");
            count++;
        }

        context.Print(count == 0 ? "Everything is at its default." : $"{count} setting(s) differ from default.");
    }

    private static void Exec(ConsoleCommandContext context, IConsoleFileSystem files)
    {
        if (context.Count == 0)
        {
            context.Error("usage: exec <file>");
            return;
        }

        if (!files.TryReadText(context[0], out string text))
        {
            context.Error($"Cannot read '{context[0]}'.");
            return;
        }

        foreach (string line in text.Split('\n'))
        {
            context.Console.Execute(line.TrimEnd('\r'));
        }
    }

    private static void Apply(ConsoleCommandContext context, ICVar variable, string value)
    {
        if (!variable.TrySetText(value, out string? reason))
        {
            context.Error(reason ?? $"'{value}' is not valid for {variable.Name}.");
            return;
        }

        context.Print(ConsoleSystem.Describe(variable));
    }

    private static bool TryResolve(ConsoleCommandContext context, string name, out ICVar variable)
    {
        if (context.Console.Variables.TryGet(name, out ICVar? found))
        {
            variable = found;
            return true;
        }

        context.Error(name.Length == 0 ? $"usage: {context.Name} <variable>" : $"No variable called '{name}'.");
        variable = null!;
        return false;
    }
}
