namespace OpenIGI.Game.Igi1;

/// <summary>
/// The special cases I.G.I. 1's levels need by name or number, in one place a mod can change: the title publishes one per
/// session, and a mod's content module, which registers after the title's, edits it.
/// </summary>
public sealed class Igi1Quirks
{
    /// <summary>
    /// Gets the archive spellings of destroyed models whose level data names them otherwise, by mission: the name the level
    /// gives, and the name the shipped archive stores the geometry under.
    /// </summary>
    /// <remarks>
    /// Mission 6's OBJECTS.QVM names Terminal_94's destroyed model <c>252_02_1</c>; the archive holds that geometry only as
    /// <c>252_01_2</c>. The scene keeps the authored name and draws the archive's.
    /// </remarks>
    public Dictionary<(int Mission, string Model), string> DestroyedModelArchiveNames { get; } = new()
    {
        [(6, "252_02_1")] = "252_01_2",
    };

    /// <summary>Gets the archive spelling of a destroyed model a mission names, if it has another.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="model">The model the level names.</param>
    /// <returns>The archive's name, or <see langword="null"/>.</returns>
    public string? DestroyedModelArchiveName(int mission, string model)
    {
        foreach (((int Mission, string Model) key, string archiveName) in DestroyedModelArchiveNames)
        {
            if (key.Mission == mission && string.Equals(key.Model, model, StringComparison.OrdinalIgnoreCase))
            {
                return archiveName;
            }
        }

        return null;
    }
}
