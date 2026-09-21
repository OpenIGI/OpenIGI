using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>Serialises the test classes that open a real OpenAL device.</summary>
/// <remarks>
/// <para>
/// <c>alcMakeContextCurrent</c> sets the current context for the <em>process</em>, not for the caller,
/// so two live <c>OpenAlAudioDevice</c> instances are one context between them: whichever was created
/// last owns it, and every call the other makes goes to the wrong place. A voice started on the losing
/// device is simply never heard from again, which surfaces as a track that "stopped while being
/// pumped".
/// </para>
/// <para>
/// That is a property of OpenAL rather than a fault in the device, and the game never has two. It only
/// bites here, because xunit runs test classes in parallel — so the classes that own a device share a
/// collection and take their turns.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class OpenAlDeviceFixture
{
    /// <summary>The collection name the classes that own a device carry.</summary>
    public const string Name = "OpenAL device";
}
