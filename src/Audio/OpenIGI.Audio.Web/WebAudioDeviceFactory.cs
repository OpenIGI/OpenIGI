using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Audio.Web;

/// <summary>Opens <see cref="WebAudioDevice"/>s over one page.</summary>
/// <remarks>
/// The host re-opens the device whenever the voice count or the output rate changes, and each open
/// closes the page's previous context before making the next, so one page serves every device the host
/// ever asks for.
/// </remarks>
public sealed class WebAudioDeviceFactory : IAudioDeviceFactory
{
    private readonly IWebAudioPage _page;

    /// <summary>Initializes a new instance of the <see cref="WebAudioDeviceFactory"/> class.</summary>
    /// <param name="page">The page's audio graph every device opens over.</param>
    public WebAudioDeviceFactory(IWebAudioPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    /// <inheritdoc/>
    public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new WebAudioDevice(options, _page, logger);
}
