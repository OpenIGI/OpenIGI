using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Host.Multiplayer;

/// <summary>A session that knows the mode it plays before its level is up: what that level must be made into.</summary>
public interface ISessionMode
{
    /// <summary>Gets the mode's id, as <c>coop</c> or <c>tdm</c>; empty when the session doesn't say.</summary>
    string Mode { get; }

    /// <summary>Gets or sets the layout of the map being played, which the host found as the level loaded; <see langword="null"/> for none.</summary>
    MpLayout? Layout { get; set; }
}
