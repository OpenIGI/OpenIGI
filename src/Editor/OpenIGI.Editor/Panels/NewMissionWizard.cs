using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>A mission to make: the level it starts as a copy of, its name and number, where it goes, and what it runs in.</summary>
/// <param name="From">The mission whose level it copies.</param>
/// <param name="Name">Its name, as the mission menu shows it.</param>
/// <param name="Id">Its number.</param>
/// <param name="JoinCampaign">Whether the campaign goes on to it after its last mission.</param>
/// <param name="Target">What it is made to run in.</param>
internal sealed record NewMissionRequest(MissionDescriptor From, string Name, int Id, bool JoinCampaign, MissionTarget Target)
{
    /// <summary>Gets a value indicating whether the mission starts blank: the level's ground and the player, with an exit.</summary>
    public bool Blank { get; init; }

    /// <summary>Gets what a blank mission's ground is.</summary>
    public BlankGround Ground { get; init; }

    /// <summary>Gets the seed seeded ground grows from.</summary>
    public int Seed { get; init; }
}

/// <summary>
/// Making a mission, one decision at a time: which game and what it must run in, the level it starts from, its name and
/// number, then a review of exactly what will be made, before anything is written.
/// </summary>
/// <remarks>
/// The game and target come first because they decide the rest, as a map editor's game configuration does: a retail
/// mission is kept to what the original game can load, an OpenIGI one may use what the remaster adds. Choosing the other
/// game switches the editor to it and opens this wizard again there.
/// </remarks>
internal static class NewMissionWizard
{
    /// <summary>Opens the wizard.</summary>
    /// <param name="root">The editor's window.</param>
    /// <param name="host">The running game.</param>
    /// <param name="create">Makes the mission; whether it was made.</param>
    /// <param name="target">The target to start on, as a reopened wizard carries it.</param>
    /// <param name="name">The name to start with.</param>
    /// <returns>The dialog the wizard is in.</returns>
    public static Dialog Open(EditorRoot root, IEditorHost host, Func<NewMissionRequest, bool> create, MissionTarget target = MissionTarget.Retail, string name = "")
    {
        EditorTheme theme = root.Theme;
        IReadOnlyList<GameTitle> titles = host.InstalledTitles;
        IReadOnlyList<MissionDescriptor> missions = host.Missions;

        // 1. Game and target.
        OptionCards games = new(theme)
        {
            Horizontal = true,
            Cards = [.. titles.Select(t => new OptionCard(t.DisplayName, t == host.Title ? "The game the editor has open." : "The editor switches to it first.", t == host.Title ? "open now" : string.Empty))],
            SelectedIndex = Math.Max(0, titles.ToList().IndexOf(host.Title)),
        };
        OptionCards targets = new(theme)
        {
            Cards =
            [
                new OptionCard("Retail compatible", "Plays in the original game too. Kept to what it can load, and can be exported for it.", "recommended"),
                new OptionCard("OpenIGI only", host.Title.Id == "igi1"
                    ? "Free to use what OpenIGI adds, such as mountain areas. The original game cannot play it."
                    : "Free to use what OpenIGI adds. The original game cannot play it."),
            ],
            SelectedIndex = target == MissionTarget.Retail ? 0 : 1,
        };
        Stack gameStep = new(theme, [("Game", games, 0), ("Runs in", targets, 0)]);

        // 2. The level to start from.
        OptionCards levels = new(theme)
        {
            Cards = [.. missions.Select(m => new OptionCard(
                string.Create(CultureInfo.InvariantCulture, $"{m.Id}.  {host.MissionName(m)}"),
                m.MissionDirectory.TrimEnd('/'),
                host.Mission is { } open && open.Id == m.Id ? "open now" : string.Empty))],
            SelectedIndex = Math.Max(0, missions.ToList().FindIndex(m => host.Mission is { } open && m.Id == open.Id)),
        };


        // How it starts: a full copy, or blank (the level as a place, the player, and an exit to end it).
        bool blankAllowed = host.Title.Id == "igi1";
        OptionCards starts = new(theme)
        {
            Cards =
            [
                new OptionCard("Copy of the level", "Everything in it comes too: buildings, soldiers, cutscenes, triggers and objectives."),
                new OptionCard(
                    "Blank",
                    blankAllowed
                        ? "Its ground, sky and light, and the player with their kit. An exit ahead of them ends the mission."
                        : "Blank missions are for Project I.G.I. for now.",
                    blankAllowed ? "bare minimum" : string.Empty,
                    blankAllowed),
            ],
            SelectedIndex = 0,
        };
        Stack levelStep = new(theme, [("Start with", starts, 0), ("Level", levels, 1)]);

        // The ground of a blank mission: the level's own, flat, or generated from a seed.
        OptionCards grounds = new(theme);
        NumberField seedField = new(theme) { Decimals = 0, Minimum = 0, Maximum = int.MaxValue, Step = 1 };
        seedField.Value = Random.Shared.Next(1, 1_000_000);
        Form seedForm = new(theme, [("Seed", seedField)]);
        Stack groundStep = new(theme, [("Ground", grounds, 0), ("Seed", seedForm, 1)]);
        bool Blank() => starts.SelectedIndex == 1;
        BlankGround Ground() => !Blank() ? BlankGround.Keep : grounds.SelectedIndex switch
        {
            1 => BlankGround.Flat,
            2 => BlankGround.Seeded,
            _ => BlankGround.Keep,
        };
        // The seed only for seeded ground.
        void ShowSeed() => seedForm.Visibility = grounds.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        grounds.SelectionChanged += _ => ShowSeed();
        void FillGrounds()
        {
            string area = host.Title.Id == "igi1" ? $"{BlankGroundAcross()} km across round the player" : "over the whole map";
            int chosen = grounds.SelectedIndex;
            grounds.Cards =
            [
                new OptionCard("Keep the level's ground", "The terrain as it shipped, with nothing else of the level on it."),
                new OptionCard("Flat", $"Level ground at the player's height, {area}, blending into the level's ground."),
                new OptionCard("Hills from a seed", $"Rolling hills, {area}. The same seed always gives the same hills."),
            ];
            grounds.SelectedIndex = Math.Max(0, chosen);
            ShowSeed();
        }

        // 3. Details.
        int nextId = missions.Count == 0 ? 1 : missions.Max(static m => m.Id) + 1;
        TextField nameField = new(theme) { Text = name, Placeholder = "Mission name", Validate = static text => text.Length <= NewMission.MaximumNameLength };
        NumberField number = new(theme) { Decimals = 0, Minimum = 1, Maximum = 999, Step = 1 };
        number.Value = nextId;
        CheckBox campaign = new(theme, "Play it after the campaign's last mission (it joins the mission menu)") { IsChecked = true };
        Form details = new(theme, [("Name", nameField), ("Number", number), (string.Empty, campaign)]);

        // 4. Review.
        Review review = new(theme);

        GameTitle Game() => titles[Math.Clamp(games.SelectedIndex, 0, titles.Count - 1)];
        MissionTarget Target() => targets.SelectedIndex == 1 ? MissionTarget.OpenIgi : MissionTarget.Retail;
        bool Switching() => Game() != host.Title;
        MissionDescriptor? From() => levels.SelectedIndex >= 0 && levels.SelectedIndex < missions.Count ? missions[levels.SelectedIndex] : null;
        int Id() => (int)Math.Round(number.Value);
        string Name() => nameField.Text.Trim();
        MissionDescriptor? Taken() => missions.FirstOrDefault(m => m.Id == Id());

        Dialog? dialog = null;
        Wizard wizard = new(
            theme,
            [
                new WizardPage("Game", "What is it for?", "The game decides which levels and objects there are; the target decides what the mission may use.", gameStep)
                {
                    Problem = () => titles.Count == 0 ? "No game is installed." : null,
                    NextText = () => Switching() ? $"Switch to {Game().DisplayName}" : null,
                    Leave = () =>
                    {
                        if (!Switching())
                        {
                            return false;
                        }

                        // The wizard comes back in the other game with what was chosen so far.
                        string[] reopen = ["wizard", "newmission", Target() == MissionTarget.Retail ? "retail" : "openigi", .. Name().Split(' ', StringSplitOptions.RemoveEmptyEntries)];
                        host.SwitchTitle(Game().Id, [reopen]);
                        return true;
                    },
                },
                new WizardPage("Level", "Start from which level?", "A copy takes everything in it; a blank takes the place and the player. The level itself is left as it was.", levelStep)
                {
                    Problem = () => From() is null ? "Pick the level to start from." : null,
                },
                new WizardPage("Ground", "What ground?", "The ground the blank mission is played on.", groundStep)
                {
                    Entered = FillGrounds,

                    // A copy keeps its level's ground: nothing to decide.
                    Skipped = () => !Blank(),
                },
                new WizardPage("Details", "Name and number", $"The name shows in the mission menu (at most {NewMission.MaximumNameLength} characters); the number is its place among the missions.", details)
                {
                    Problem = () => Name().Length == 0 ? "Give the mission a name."
                        : Name().Length > NewMission.MaximumNameLength ? $"The original keeps at most {NewMission.MaximumNameLength} characters of a name."
                        : Taken() is { } taken ? string.Create(CultureInfo.InvariantCulture, $"Mission {Id()} is {host.MissionName(taken)} already.")
                        : null,
                },
                new WizardPage("Review", "Make this mission?", "Nothing is written until you create it. The game's own files are never changed.", review)
                {
                    Entered = () => review.Lines = Lines(),
                },
            ],
            "Create Mission",
            () => From() is { } from && create(new NewMissionRequest(from, Name(), Id(), campaign.IsChecked, Target())
            {
                Blank = Blank(),
                Ground = Ground(),
                Seed = (int)Math.Round(seedField.Value),
            }));

        IReadOnlyList<(string Label, string Value, bool Warning)> Lines()
        {
            MissionDescriptor from = From()!;
            List<(string, string, bool)> lines =
            [
                ("Game", host.Title.DisplayName, false),
                ("Runs in", Target() == MissionTarget.Retail ? "The original game and OpenIGI" : "OpenIGI only", false),
                ("Mission", string.Create(CultureInfo.InvariantCulture, $"{Id()}. {Name()}"), false),
                (Blank() ? "Blank on" : "Copy of", string.Create(CultureInfo.InvariantCulture, $"{from.Id}. {host.MissionName(from)}"), false),
                ("Ground", Ground() switch
                {
                    BlankGround.Flat => "Flat",
                    BlankGround.Seeded => string.Create(CultureInfo.InvariantCulture, $"Generated from seed {(int)Math.Round(seedField.Value)}"),
                    _ => "The level's own",
                }, false),
                ("Folder", NewMission.DirectoryFor(from.MissionDirectory, Id()), false),
                ("Menu", campaign.IsChecked ? "After the campaign's last mission" : "Not in the campaign; opened from the editor", false),
            ];
            if (Id() != nextId)
            {
                lines.Add(("Note", string.Create(CultureInfo.InvariantCulture, $"Numbers between {nextId - 1} and {Id()} stay free."), true));
            }

            if (Target() == MissionTarget.OpenIgi)
            {
                lines.Add(("Note", "Export for the retail game will refuse this mission.", true));
            }

            return lines;
        }

        wizard.Finished += () => dialog?.Close();
        dialog = Dialog.Open(root, "New Mission", wizard, 720f, 520f);
        return dialog;
    }

    // How far across a play area of mountain areas is, in kilometres.
    private static string BlankGroundAcross() =>
        (OpenIGI.Editor.Terrain.BlankGroundCommand.AreasAcross * 0.512).ToString("0.#", CultureInfo.InvariantCulture);

    // Labelled sections one above another, each taking a share of the room.
    private sealed class Stack : EditorElement
    {
        private readonly (string Label, EditorElement Element, float Share)[] _sections;

        public Stack(EditorTheme theme, (string Label, EditorElement Element, float Share)[] sections)
            : base(theme)
        {
            _sections = sections;
            foreach ((_, EditorElement element, _) in sections)
            {
                AddChild(element);
            }
        }

        private float LabelHeight => Theme.RowHeight + Theme.Px(4f);

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        // A section of share 0 is as tall as its cards; the room left is shared out among the others by their shares.
        protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
        {
            float shares = _sections.Sum(static s => s.Share);
            float room = contentRect.Height - (_sections.Length * (LabelHeight + Theme.Px(10f)));
            float natural = _sections.Where(static s => s.Share == 0f && s.Element.Visibility != Visibility.Collapsed).Sum(s => Natural(s.Element));
            float flexible = MathF.Max(0f, room - natural);
            float y = contentRect.Y;
            foreach ((_, EditorElement element, float share) in _sections)
            {
                if (element.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                float height = share == 0f ? MathF.Min(Natural(element), room) : flexible * share / shares;
                element.Place(context, new UiRect(contentRect.X, y + LabelHeight, contentRect.Width, height));
                y += LabelHeight + height + Theme.Px(10f);
            }
        }

        private float Natural(EditorElement element) => element is OptionCards cards ? cards.NaturalHeight : Theme.ControlHeight;

        protected override void OnDraw(UiPainter painter)
        {
            foreach ((string label, EditorElement element, _) in _sections.Where(static s => s.Element.Visibility != Visibility.Collapsed))
            {
                EditorDraw.Label(painter, Theme, label.ToUpperInvariant(), new UiRect(element.Bounds.X, element.Bounds.Y - LabelHeight, element.Bounds.Width, Theme.RowHeight), Theme.TextDisabled, strong: true);
            }
        }
    }

    // Fields with their labels.
    private sealed class Form : EditorElement
    {
        private readonly (string Label, EditorElement Field)[] _rows;

        public Form(EditorTheme theme, (string Label, EditorElement Field)[] rows)
            : base(theme)
        {
            _rows = rows;
            foreach ((_, EditorElement field) in rows)
            {
                AddChild(field);
            }
        }

        private float LabelWidth => Theme.Px(110f);

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
        {
            float y = contentRect.Y + Theme.Px(6f);
            foreach ((_, EditorElement field) in _rows)
            {
                field.Place(context, new UiRect(contentRect.X + LabelWidth, y, MathF.Min(Theme.Px(420f), contentRect.Width - LabelWidth), Theme.ControlHeight));
                y += Theme.ControlHeight + Theme.Px(10f);
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            foreach ((string label, EditorElement field) in _rows)
            {
                EditorDraw.Label(painter, Theme, label, new UiRect(Bounds.X, field.Bounds.Y, LabelWidth, Theme.ControlHeight), Theme.TextMuted);
            }
        }
    }

    // The summary of what will be made, a line per fact, warnings marked.
    private sealed class Review(EditorTheme theme) : EditorElement(theme)
    {
        public IReadOnlyList<(string Label, string Value, bool Warning)> Lines { get; set; } = [];

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void OnDraw(UiPainter painter)
        {
            EditorDraw.Frame(painter, Theme, Bounds, Theme.Input, Theme.Border);
            float pad = Theme.Px(12f);
            float y = Bounds.Y + pad;
            foreach ((string label, string value, bool warning) in Lines)
            {
                EditorDraw.Label(painter, Theme, label, new UiRect(Bounds.X + pad, y, Theme.Px(100f), Theme.RowHeight), Theme.TextMuted);
                EditorDraw.Label(painter, Theme, value, new UiRect(Bounds.X + pad + Theme.Px(100f), y, Bounds.Width - (pad * 2f) - Theme.Px(100f), Theme.RowHeight), warning ? Theme.Warning : Theme.Text);
                y += Theme.RowHeight + Theme.Px(6f);
            }
        }
    }
}
