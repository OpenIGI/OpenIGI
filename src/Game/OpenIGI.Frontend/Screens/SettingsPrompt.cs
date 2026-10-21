using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>What a question on the settings page is about.</summary>
internal enum SettingsPromptKind
{
    /// <summary>Leaving with changes not applied.</summary>
    Leave,

    /// <summary>Keeping a display mode just applied.</summary>
    Keep,
}

/// <summary>One answer to a question: its button, and what choosing it does.</summary>
/// <param name="Caption">The button's caption.</param>
/// <param name="Accent">How the button is drawn: solid for the answer wanted, ghost for the rest.</param>
/// <param name="Chosen">What choosing it does.</param>
internal sealed record SettingsPromptChoice(string Caption, ButtonAccent Accent, Action Chosen);

/// <summary>A question over the page, and how long it has been asked.</summary>
/// <param name="Kind">What it is about.</param>
/// <param name="Eyebrow">The line above its title.</param>
/// <param name="Title">The question.</param>
/// <param name="Message">What it says under the title, given the seconds it has been on screen.</param>
/// <param name="BackCaption">What back does, for the footer: the last answer, the cautious one.</param>
/// <param name="Choices">The answers, the one wanted first.</param>
/// <param name="Expires">After how many seconds back answers it on the player's behalf, or never.</param>
internal sealed record SettingsPrompt(
    SettingsPromptKind Kind,
    string Eyebrow,
    string Title,
    Func<float, string> Message,
    string BackCaption,
    IReadOnlyList<SettingsPromptChoice> Choices,
    float? Expires = null)
{
    /// <summary>Gets or sets how many seconds the question has been on screen.</summary>
    public float Elapsed { get; set; }

    /// <summary>Answers the question the cautious way: its last answer.</summary>
    public void Back() => Choices[^1].Chosen();
}
