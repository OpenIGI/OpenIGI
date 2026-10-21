namespace OpenIGI.Host.Services;

/// <summary>A part of the host with one job of its own, made by the host and released with it.</summary>
/// <remarks>
/// The host's frame loop calls each service where its work belongs in the frame, in an order the loop spells out; a
/// service does not reach into another's state, and what it needs of the host it is handed when it is made.
/// </remarks>
internal interface IHostService : IDisposable
{
    /// <summary>Gets the service's name, as the console lists it.</summary>
    string Name { get; }
}
