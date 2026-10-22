using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenIGI.Core.Content;

namespace OpenIGI.Modding;

/// <summary>Reads a mod's settings file: the player's copy beside the mod when there is one, else the copy the mod ships.</summary>
public static class ModConfig
{
    /// <summary>Reads a mod's JSON settings.</summary>
    /// <typeparam name="T">The settings' type.</typeparam>
    /// <param name="directory">The mod's directory, where a player's own copy of the file wins.</param>
    /// <param name="fileName">The file's name; the shipped copy is the embedded resource of that name.</param>
    /// <param name="typeInfo">The settings' generated JSON shape.</param>
    /// <param name="shipped">The assembly that embeds the shipped copy; the settings type's own by default.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ModLoadException">Neither copy is there, or the one read is not valid.</exception>
    public static T Read<T>(string directory, string fileName, JsonTypeInfo<T> typeInfo, Assembly? shipped = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(typeInfo);
        string path = Path.Combine(directory, fileName);
        try
        {
            if (File.Exists(path))
            {
                using FileStream file = File.OpenRead(path);
                return JsonSerializer.Deserialize(file, typeInfo) ?? throw new ModLoadException($"'{fileName}' is empty.");
            }

            Assembly assembly = shipped ?? typeof(T).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(fileName)
                ?? throw new ModLoadException($"'{fileName}' is neither beside the mod nor embedded in {assembly.GetName().Name}.");
            return JsonSerializer.Deserialize(stream, typeInfo) ?? throw new ModLoadException($"'{fileName}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ModLoadException($"'{fileName}' is not valid: {ex.Message}", ex);
        }
    }
}
