namespace OpenIGI.Game.World;

/// <summary>One sound a lamp made, for whoever plays them.</summary>
/// <param name="Index">The fixture's index.</param>
/// <param name="Sound">What it did.</param>
public readonly record struct LampEvent(int Index, LampSound Sound);
