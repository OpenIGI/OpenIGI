using OpenIGI.Host.Services;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The probe field and the path-traced lightmaps; made with their caches.
    private BakedLighting _baked = null!;
}
