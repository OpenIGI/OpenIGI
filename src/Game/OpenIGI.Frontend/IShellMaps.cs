namespace OpenIGI.Frontend;

/// <summary>A community map as the shell shows it: published, installed, or both.</summary>
/// <param name="Id">The map's id.</param>
/// <param name="Title">Its name.</param>
/// <param name="Summary">What it is.</param>
/// <param name="Author">Who made it.</param>
/// <param name="Version">Its newest version known: the published one, else the installed one.</param>
/// <param name="Rating">Its average rating, one to five stars, or nought before anyone has rated it.</param>
/// <param name="Ratings">How many have rated it.</param>
/// <param name="Downloads">How many times it has been downloaded.</param>
/// <param name="Size">What downloading it costs, in bytes.</param>
/// <param name="InstalledVersion">The version installed here, or <see langword="null"/> when it is not.</param>
/// <param name="Online">Whether it is published now (it was in the last listing).</param>
public sealed record ShellMap(
    string Id,
    string Title,
    string Summary,
    string Author,
    int Version,
    double Rating,
    int Ratings,
    int Downloads,
    long Size,
    int? InstalledVersion,
    bool Online)
{
    /// <summary>Gets whether it is installed here.</summary>
    public bool Installed => InstalledVersion is not null;

    /// <summary>Gets whether a newer version is published than the one installed.</summary>
    public bool HasUpdate => InstalledVersion is { } installed && Online && Version > installed;
}

/// <summary>
/// Community maps, for the shell's Maps page: what is installed, what is published for this game, and downloading, playing,
/// removing, rating and reporting one. The host does the work; the page shows it.
/// </summary>
public interface IShellMaps
{
    /// <summary>Lists the maps installed here and, once fetched, those published for this game, together.</summary>
    /// <returns>The maps: installed first, then the rest by downloads.</returns>
    IReadOnlyList<ShellMap> Maps();

    /// <summary>Fetches what is published; <see cref="Maps"/> includes it once done.</summary>
    /// <returns>Nothing when it went well, or what went wrong.</returns>
    Task<string?> RefreshAsync();

    /// <summary>Downloads and installs a map, or its newer version.</summary>
    /// <param name="id">The map.</param>
    /// <returns>Nothing when it went well, or what went wrong.</returns>
    Task<string?> InstallAsync(string id);

    /// <summary>Plays an installed map: leaves the shell and starts its mission.</summary>
    /// <param name="id">The map.</param>
    /// <param name="difficulty">The difficulty index to play at.</param>
    void Play(string id, int difficulty);

    /// <summary>Removes an installed map from this machine.</summary>
    /// <param name="id">The map.</param>
    void Remove(string id);

    /// <summary>Rates a map, one to five stars.</summary>
    /// <param name="id">The map.</param>
    /// <param name="stars">The stars.</param>
    /// <returns>Nothing when it went well, or what went wrong.</returns>
    Task<string?> RateAsync(string id, int stars);

    /// <summary>Reports a map as broken or not right to have.</summary>
    /// <param name="id">The map.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Nothing when it went well, or what went wrong.</returns>
    Task<string?> ReportAsync(string id, string reason);
}
