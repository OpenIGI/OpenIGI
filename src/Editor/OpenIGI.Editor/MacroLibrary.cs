using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Projects;

namespace OpenIGI.Editor;

/// <summary>A project's macros: chains of action nodes saved by name in its <c>macros</c> folder, one file each.</summary>
/// <param name="project">The project.</param>
internal sealed class MacroLibrary(EditorProject project)
{
    /// <summary>The extension a macro's file has.</summary>
    public const string Extension = ".macro.json";

    private string Folder => Path.Combine(project.Root, "macros");

    /// <summary>Reads every macro of the project; a file that does not read is left out.</summary>
    /// <returns>The macros, by name.</returns>
    public IReadOnlyDictionary<string, ExecMacro> Load()
    {
        Dictionary<string, ExecMacro> macros = new(StringComparer.Ordinal);
        if (!Directory.Exists(Folder))
        {
            return macros;
        }

        foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension))
        {
            try
            {
                ExecMacro macro = ExecMacroJson.Read(File.ReadAllBytes(file));
                macros[macro.Name] = macro;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                // A macro that does not read is not offered; the rest still are.
            }
        }

        return macros;
    }

    /// <summary>Saves a macro, replacing one of the same name.</summary>
    /// <param name="macro">The macro.</param>
    /// <exception cref="IOException">The project cannot be written.</exception>
    public void Save(ExecMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        Directory.CreateDirectory(Folder);
        string name = string.Concat(macro.Name.Select(static c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        File.WriteAllBytes(Path.Combine(Folder, name + Extension), ExecMacroJson.Write(macro));
    }
}
