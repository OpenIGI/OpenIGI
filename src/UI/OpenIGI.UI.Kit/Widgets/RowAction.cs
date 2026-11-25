namespace OpenIGI.UI.Kit.Widgets;

/// <summary>One of an <see cref="ActionRow"/>'s other buttons: what it says, what pressing it does, and whether it destroys.</summary>
/// <param name="Caption">What it says, shown in capitals.</param>
/// <param name="Pressed">What pressing it does, with the row it is on.</param>
/// <param name="Danger">Whether it destroys something -- a Delete -- and is drawn in the danger's colour.</param>
public sealed record RowAction(string Caption, Action<ActionRow> Pressed, bool Danger = false);
