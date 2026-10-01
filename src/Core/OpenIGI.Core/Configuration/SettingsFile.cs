using System.Text;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// Reads and writes <c>settings.cfg</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is a console script, not a second configuration format: loading it is just executing its
/// lines, so anything that can be typed can be saved and vice versa, and a player can hand-edit it
/// with the same vocabulary the console taught them.
/// </para>
/// <para>
/// Only variables flagged <see cref="CVarOptions.Archive"/> <em>and</em> differing from their default are
/// written. Recording a diff rather than a snapshot means a default that is later re-tuned — a fog
/// density, a shadow resolution — takes effect for everyone who never overrode it.
/// </para>
/// </remarks>
public static class SettingsFile
{
    /// <summary>Loads settings by executing the file's contents.</summary>
    /// <param name="console">The console to execute against.</param>
    /// <param name="path">The file to read.</param>
    /// <returns><see langword="true"/> when the file existed and was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    public static bool Load(ConsoleSystem console, string path)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        foreach (string line in lines)
        {
            console.Execute(line);
        }

        return true;
    }

    /// <summary>Writes the current settings.</summary>
    /// <param name="console">The console whose registry, aliases and bindings are written.</param>
    /// <param name="path">The file to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <remarks>
    /// The write is atomic — a temporary file beside the target is replaced into place — so a crash
    /// or a power cut during the save cannot leave a truncated settings file behind.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    public static bool Save(ConsoleSystem console, string path)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string text = Render(console);
        string temporary = path + ".tmp";

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, text, Encoding.UTF8);

            if (File.Exists(path))
            {
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Renders the settings that would be written.</summary>
    /// <param name="console">The console whose registry, aliases and bindings are rendered.</param>
    /// <returns>The file's contents.</returns>
    /// <remarks>Exposed so tests and the <c>dump_settings</c> command can inspect it without writing a file.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    public static string Render(ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);

        StringBuilder builder = new();
        builder.AppendLine("// OpenIGI settings. Written by the game; safe to edit by hand.");
        builder.AppendLine("// Only values that differ from their default are recorded.");
        builder.AppendLine();

        List<ICVar> modified = [.. console.Variables.Modified(CVarOptions.Archive)];
        if (modified.Count > 0)
        {
            builder.AppendLine("// Variables");
            foreach (ICVar variable in modified)
            {
                builder.Append(variable.Name)
                    .Append(' ')
                    .AppendLine(CommandTokenizer.Quote(variable.ValueText));
            }

            builder.AppendLine();
        }

        if (console.Aliases.Count > 0)
        {
            builder.AppendLine("// Aliases");
            foreach (KeyValuePair<string, string> alias in Ordered(console.Aliases))
            {
                builder.Append("alias ")
                    .Append(CommandTokenizer.Quote(alias.Key))
                    .Append(' ')
                    .AppendLine(CommandTokenizer.Quote(alias.Value));
            }

            builder.AppendLine();
        }

        if (console.Binds.Count > 0)
        {
            builder.AppendLine("// Key bindings");
            foreach (KeyValuePair<string, string> bind in Ordered(console.Binds))
            {
                builder.Append("bind ")
                    .Append(CommandTokenizer.Quote(bind.Key))
                    .Append(' ')
                    .AppendLine(CommandTokenizer.Quote(bind.Value));
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string>> Ordered(IReadOnlyDictionary<string, string> table) =>
        table.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase);
}
