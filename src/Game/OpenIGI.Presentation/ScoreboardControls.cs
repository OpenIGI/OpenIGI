namespace OpenIGI.Presentation;

/// <summary>The host's hand on a co-op scoreboard: which players it may put out of the room, and where its pointer is.</summary>
/// <param name="Removable">The entities a remove button is drawn for.</param>
/// <param name="PointerX">The pointer, in pixels.</param>
/// <param name="PointerY">The pointer, in pixels.</param>
/// <param name="Confirming">The entity whose removal waits for a second click, or <see langword="null"/>.</param>
public sealed record ScoreboardControls(IReadOnlySet<int> Removable, float PointerX, float PointerY, int? Confirming);
