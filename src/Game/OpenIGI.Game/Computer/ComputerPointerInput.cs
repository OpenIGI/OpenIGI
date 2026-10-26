namespace OpenIGI.Game.Computer;

/// <summary>The mouse as a computer reads it on a tick: the counts it moved since the last and the buttons, as the original's mouse poll leaves them.</summary>
/// <param name="CountsX">The counts it moved across since the last tick.</param>
/// <param name="CountsY">The counts it moved down.</param>
/// <param name="Buttons">Left <c>0x1</c>, right <c>0x2</c>, and one notch of the wheel: away <c>0x100</c>, back <c>0x200</c>.</param>
/// <param name="MapKey">
/// Whether the map key went down since the last tick. Read by the host as it gathers the mouse, because the open computer
/// holds the keyboard from everything else.
/// </param>
public readonly record struct ComputerPointerInput(float CountsX, float CountsY, int Buttons, bool MapKey = false);
