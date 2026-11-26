using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Serialises the test classes that set or read the variables an experience writes.
/// </summary>
/// <remarks>
/// Applying an experience writes every one of them at once -- dropped weapons, bodies, ragdolls -- and they are process-wide
/// singletons, so a test reading one while another applies the original game finds it changed under it.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExperienceSettings
{
    /// <summary>The collection's name.</summary>
    public const string Name = "experience settings";
}
