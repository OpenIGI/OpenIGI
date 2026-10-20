using OpenIGI.Host.Services;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // What the time of day reads of the host.
    private sealed class TimeOfDayHost(GameHost host) : ITimeOfDayHost
    {
        public bool TitleAuthorsAmbient => host._deferredLighting is not null;

        public bool WeatherFalls => (host._rain?.IsWeatherEnabled ?? false) || (host._snow?.IsWeatherEnabled ?? false);

        public int Milliseconds => host._loop.TimeSource.Milliseconds;
    }
}
