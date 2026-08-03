using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Desktop;

/// <summary>
/// Reads and writes the game's settings, always leaving the original installation untouched: OpenIGI's own
/// <c>openigi-config.qvm</c> wins over the retail file when it is present, and is written next to it rather
/// than over it.
/// </summary>
internal static class DesktopConfiguration
{
    /// <summary>Loads the configuration, preferring OpenIGI's own file, then the retail file, then defaults.</summary>
    /// <param name="fileSystem">The virtual file system the configuration is read through.</param>
    /// <param name="logger">The log read failures are reported to.</param>
    /// <returns>The loaded configuration, or a fresh default when none can be read.</returns>
    public static GameConfig Load(VirtualFileSystem fileSystem, IIgiLogger logger)
    {
        // Ours wins when it is there, so a setting changed in OpenIGI survives even though the retail
        // file is still the fallback.
        foreach (string path in new[] { ConfigScript.OpenIgiPath, ConfigScript.RetailPath })
        {
            if (!fileSystem.Exists(path))
            {
                continue;
            }

            try
            {
                return ConfigScript.Load(fileSystem.ReadAllBytes(path), logger);
            }
            catch (Exception ex) when (ex is IOException or QvmFormatException)
            {
                logger.Warning("Config", $"Could not read {path}: {ex.Message}");
            }
        }

        logger.Information("Config", "No configuration found; starting from the defaults.");
        return GameConfig.CreateDefault();
    }

    /// <summary>Writes the configuration to <c>openigi-config.qvm</c> beside the retail file.</summary>
    /// <param name="gamePath">The original installation directory the file is written into.</param>
    /// <param name="configuration">The configuration to persist.</param>
    /// <param name="logger">The log the save is reported to.</param>
    public static void Save(string gamePath, GameConfig configuration, IIgiLogger logger)
    {
        try
        {
            // Written next to the retail file rather than over it, so an original installation is never
            // modified. The format is identical, so copying it across would work.
            string path = Path.Combine(gamePath, "openigi-config.qvm");
            File.WriteAllBytes(path, ConfigScriptWriter.Write(configuration));
            logger.Information("Config", $"Saved {configuration.Profiles.Count} profile(s) to {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Config", $"Could not save the configuration: {ex.Message}");
        }
    }
}
