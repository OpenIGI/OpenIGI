namespace OpenIGI.Core.Simulation;

/// <summary>
/// Marks a field that only indexes state recorded elsewhere (a map from each task to its id, kept beside the id table): put back
/// on a restore like any other, but left out when two checkpoints are compared (<see cref="StateCheckpoint.Differences"/>), as two
/// loads may fill it in another order with the same contents.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DerivedStateAttribute : Attribute
{
}
