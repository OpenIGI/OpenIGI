namespace OpenIGI.Core.Simulation;

/// <summary>
/// Marks what a <see cref="StateCheckpoint"/> leaves alone. On a type: its objects are never recorded or walked (a definition,
/// a registry, a service). On a field: it is never put back on a restore (a live voice, a cache of something drawn) -- the
/// value it has then stands.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Field, Inherited = true)]
public sealed class NotSimulationStateAttribute : Attribute
{
}
