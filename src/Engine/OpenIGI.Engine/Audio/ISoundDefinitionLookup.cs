namespace OpenIGI.Engine.Audio;

/// <summary>Where the names game code says are resolved to the sounds they define.</summary>
/// <remarks>
/// Both games resolve a name the same way — the first definition of it, searched in the order the scripts declared
/// them — but each fills its table through its own natives, so a title can bring its own table and the mixer still
/// plays from it.
/// </remarks>
public interface ISoundDefinitionLookup
{
    /// <summary>Looks a definition up by the name game code says.</summary>
    /// <param name="name">The definition name, such as <c>ai_death_1</c>.</param>
    /// <param name="definition">Receives the definition when one answers to the name.</param>
    /// <returns><see langword="true"/> when the name is defined.</returns>
    bool TryResolve(string name, out SoundDefinition? definition);
}
