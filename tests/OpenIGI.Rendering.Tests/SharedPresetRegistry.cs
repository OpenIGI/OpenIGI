using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Groups every test that applies a preset to the one shared <see cref="Core.Configuration.Presets.GraphicsPresetRegistry"/>.
/// </summary>
/// <remarks>
/// <b>The registry the game uses is a singleton, and so are the variables it writes.</b> xUnit runs test
/// classes in parallel, so two classes each applying a preset to it were interleaving: one would apply
/// ultra, the other vanilla, and the first would then assert about a variable the second had just
/// rewritten. That failed about one run in five, on whichever assertion lost the race -- the kind of flake
/// that gets re-run until it passes rather than read. A collection is xUnit's unit of parallelism, so
/// naming one here puts the classes in a queue instead.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SharedPresetRegistry
{
    /// <summary>The collection's name, for the attribute on each class that joins it.</summary>
    public const string Name = "graphics presets";
}
