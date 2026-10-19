using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Host.Configuration;

/// <summary>
/// Reads and writes the game's settings and player profiles, always leaving the original installation
/// untouched.
/// </summary>
/// <remarks>
/// <para>
/// What the player accumulates — their profiles, their settings, their progress — is written to
/// <see cref="SettingsPaths"/>, not into the game folder. An installation may sit somewhere the player
/// cannot write, on a read-only mount, or be shared between several people with their own profiles, and
/// none of those work if the game saves next to its own data.
/// </para>
/// <para>
/// The file is OpenIGI's own <c>openigi-config.qvm</c> and never the retail <c>config.qvm</c>, which is
/// read but never written. The format is identical, so copying one over the other would work.
/// </para>
/// </remarks>
internal static class HostConfiguration
{
    /// <summary>The file OpenIGI keeps its own settings and profiles in.</summary>
    public const string FileName = "openigi-config.qvm";

    /// <summary>Loads the configuration, preferring the player's own file over the installation's.</summary>
    /// <param name="fileSystem">The virtual file system the installation is read through.</param>
    /// <param name="logger">The log read failures are reported to.</param>
    /// <returns>The loaded configuration, or a fresh default when none can be read.</returns>
    /// <remarks>
    /// In order: the player's own file, then one an earlier build left inside the installation, then the
    /// retail file, then defaults. The second of those is only ever read — the next save moves it out to
    /// the per-user location, so profiles made before this moved survive rather than appearing lost.
    /// </remarks>
    public static GameConfig Load(VirtualFileSystem fileSystem, IOpenIgiLogger logger)
    {
        GameConfig config = Read(fileSystem, logger);

        // Whichever file it came from: a new player's profiles are the retail file's, and those carry the
        // original's arrow keys. See GameConfig.MigrateControls.
        if (config.MigrateControls())
        {
            logger.Information(
                "Config",
                "Profiles still on the original controls were moved to WASD with the mouse not inverted.");
        }

        return config;
    }

    private static GameConfig Read(VirtualFileSystem fileSystem, IOpenIgiLogger logger)
    {
        string userPath = SettingsPaths.GetFilePath(FileName);

        if (File.Exists(userPath))
        {
            try
            {
                return ConfigScript.Load(File.ReadAllBytes(userPath), logger, userPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or QvmFormatException)
            {
                logger.Warning("Config", $"Could not read {userPath}: {ex.Message}");
            }
        }

        // ConfigScript.OpenIgiPath resolves into the installation, which is where builds before this one
        // wrote. Kept as a read so an existing set of profiles is picked up exactly once and then
        // written out where it should have been.
        foreach (string path in new[] { ConfigScript.OpenIgiPath, ConfigScript.RetailPath })
        {
            if (!fileSystem.Exists(path))
            {
                continue;
            }

            try
            {
                GameConfig loaded = ConfigScript.Load(fileSystem.ReadAllBytes(path), logger, path);

                if (path == ConfigScript.OpenIgiPath)
                {
                    logger.Information(
                        "Config",
                        $"Read {loaded.Profiles.Count} profile(s) from the installation; they move to "
                            + $"{userPath} on the next save.");
                }

                return loaded;
            }
            catch (Exception ex) when (ex is IOException or QvmFormatException)
            {
                logger.Warning("Config", $"Could not read {path}: {ex.Message}");
            }
        }

        logger.Information("Config", "No configuration found; starting from the defaults.");
        return GameConfig.CreateDefault();
    }

    /// <summary>Writes the configuration to the player's own file.</summary>
    /// <param name="configuration">The configuration to persist.</param>
    /// <param name="logger">The log the save is reported to.</param>
    public static void Save(GameConfig configuration, IOpenIgiLogger logger)
    {
        string path = SettingsPaths.GetFilePath(FileName);

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, ConfigScriptWriter.Write(configuration));
            logger.Information("Config", $"Saved {configuration.Profiles.Count} profile(s) to {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Config", $"Could not save the configuration: {ex.Message}");
        }
    }
}
