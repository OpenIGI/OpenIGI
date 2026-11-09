namespace OpenIGI.Host.Multiplayer;

/// <summary>A session that keeps a recording of itself from its start, which can be saved as a demo at any point.</summary>
public interface IDemoSource
{
    /// <summary>Saves the session so far as a demo.</summary>
    /// <param name="path">Where to.</param>
    /// <returns>Why it could not be, or <see langword="null"/> once saved.</returns>
    string? SaveDemo(string path);
}
