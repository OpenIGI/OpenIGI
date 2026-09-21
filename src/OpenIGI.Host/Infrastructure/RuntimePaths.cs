namespace OpenIGI.Host.Infrastructure;

/// <summary>Chooses writable locations for files produced while the game is running.</summary>
internal static class RuntimePaths
{
    internal const string RuntimeDirectoryEnvironmentVariable = "IGI_RUNTIME_DIR";

    public static string GetLogPath()
    {
        string? explicitDirectory = Environment.GetEnvironmentVariable(RuntimeDirectoryEnvironmentVariable);
        string? applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string? localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return GetLogPath(
            explicitDirectory,
            [
                CombineProductDirectory(applicationData),
                CombineProductDirectory(localApplicationData),
                CombineProductDirectory(Path.GetTempPath()),
            ]);
    }

    internal static string GetLogPath(string? explicitDirectory, IEnumerable<string?> candidates)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            try
            {
                return Path.Combine(EnsureWritableDirectory(explicitDirectory), PersistentLogger.LogFileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new IOException(
                    $"The explicit {RuntimeDirectoryEnvironmentVariable} directory '{explicitDirectory}' is not writable.", ex);
            }
        }

        List<Exception> failures = [];
        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return Path.Combine(EnsureWritableDirectory(candidate), PersistentLogger.LogFileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failures.Add(ex);
            }
        }

        throw new IOException("No writable OpenIGI runtime directory could be found.", failures.Count > 0 ? failures[0] : null);
    }

    private static string? CombineProductDirectory(string? parent) =>
        string.IsNullOrWhiteSpace(parent) ? null : Path.Combine(parent, "OpenIGI");

    private static string EnsureWritableDirectory(string path)
    {
        string directory = Path.GetFullPath(path);
        Directory.CreateDirectory(directory);
        string probe = Path.Combine(directory, $".openigi-write-test-{Guid.NewGuid():N}");
        try
        {
            using (FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush(true);
            }

            return directory;
        }
        finally
        {
            try { File.Delete(probe); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
