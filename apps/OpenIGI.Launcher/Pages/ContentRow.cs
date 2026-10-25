using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// One entry: grip, picture, name and summary, size, state with its switch, actions. A load-order row is
/// carried by its grip; an available row has an empty grip column so the two groups line up.
/// </summary>
internal sealed class ContentRow : Grid, IReorderItem
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly bool _ordered;
    private readonly ContentPageKind _kind;
    private bool _confirming;
    private readonly Func<string, BackdropPicture?> _pictures;
    private readonly ReorderHandle? _handle;
    private readonly Thumbnail _thumbnail;
    private readonly PackInfo _info;
    private readonly Labelled _size;
    private readonly StateLabel _state;
    private readonly OnOffSwitch? _switch;
    private readonly BusyBar _busy;
    private readonly CutButton _main;
    private readonly FlexPanel _links;
    private readonly List<(LinkButton Button, Action Act)> _linkSlots = [];
    private Action? _mainAction;
    private bool _working;
    private float _lift;

    public ContentRow(UiTheme theme, LauncherModel model, string id, bool ordered, Func<string, BackdropPicture?> pictures, ContentPageKind kind)
    {
        _theme = theme;
        _model = model;
        _ordered = ordered;
        _kind = kind;
        _pictures = pictures;
        EntryId = id;

        ColumnSpacing = 24f;
        Padding = new Thickness(4f, 12f, 20f, 12f);
        MinHeight = 104f;
        ColumnDefinitions.Add(GridLength.Absolute(44f));
        ColumnDefinitions.Add(GridLength.Absolute(150f));
        ColumnDefinitions.Add(GridLength.Star);
        ColumnDefinitions.Add(GridLength.Absolute(110f));
        ColumnDefinitions.Add(GridLength.Absolute(180f));
        ColumnDefinitions.Add(GridLength.Absolute(200f));
        RowDefinitions.Add(GridLength.Auto);

        if (ordered)
        {
            _handle = new ReorderHandle(theme) { VerticalAlignment = VerticalAlignment.Center };
            Add(_handle, 0, 0);
        }

        _thumbnail = new Thumbnail(theme) { Height = 72f, VerticalAlignment = VerticalAlignment.Center };
        _info = new PackInfo(theme) { VerticalAlignment = VerticalAlignment.Center };
        _size = new Labelled(theme, "SIZE") { VerticalAlignment = VerticalAlignment.Center };
        _state = new StateLabel(theme);

        var state = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f, VerticalAlignment = VerticalAlignment.Center };
        state.AddChild(_state);
        if (ordered)
        {
            _switch = new OnOffSwitch(theme) { HorizontalAlignment = HorizontalAlignment.Left };
            _switch.Activated += _ => Toggle();
            state.AddChild(_switch);
        }

        _busy = new BusyBar(theme) { Visibility = Visibility.Collapsed };
        _main = new CutButton(theme) { Height = 44f };
        _main.Activated += _ => _mainAction?.Invoke();
        _links = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 14f, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            var link = new LinkButton(theme);
            link.Activated += _ => _linkSlots[slot].Act();
            _linkSlots.Add((link, static () => { }));
            _links.Add(link);
        }

        var actions = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
        actions.AddChild(_busy);
        actions.AddChild(_main);
        actions.AddChild(_links);

        Add(_thumbnail, 0, 1);
        Add(_info, 0, 2);
        Add(_size, 0, 3);
        Add(state, 0, 4);
        Add(actions, 0, 5);
    }

    public string EntryId { get; }

    /// <summary>Sets the row's place in the load order, as the player counts.</summary>
    public int Position
    {
        set
        {
            if (_handle is not null)
            {
                _handle.Label = value.ToString("00", CultureInfo.InvariantCulture);
            }
        }
    }

    private LoadOrderItem? Item => _ordered
        ? (_kind == ContentPageKind.Mods ? _model.ModOrder : _model.LoadOrder).FirstOrDefault(i => i.Id == EntryId)
        : null;

    private ContentEntry? Entry => _ordered ? Item?.Entry : _model.Packs.FirstOrDefault(p => p.Pack.Id == EntryId);

    public void SetLift(float amount) => _lift = amount;

    public void Refresh()
    {
        if (Item is { Mod: { } dropped } localMod)
        {
            // A mod its author dropped in the local folder: played as it is, never updated or removed from here.
            _thumbnail.Picture = null;
            _thumbnail.Short = "LOCAL";
            _thumbnail.Local = true;
            _thumbnail.Dimmed = !localMod.Enabled;
            _info.Name = dropped.Manifest.Name;
            _info.Tags = dropped.Manifest.RunsCode ? ["LOCAL", "RUNS CODE"] : ["LOCAL"];
            _info.Description = dropped.Manifest.Summary ?? "A mod in the library's local mods folder.";
            (_info.Detail, _info.DetailColor) = localMod.Enabled
                ? ($"{dropped.Manifest.Version} · {dropped.Folder.ToUpperInvariant()}", ContentInk.Caption)
                : ("OFF · THE GAME STARTS WITHOUT IT", ContentInk.Caption);
            _size.Value = "—";
            Update();
            return;
        }

        if (Item is { IsLocal: true } local)
        {
            _thumbnail.Picture = null;
            _thumbnail.Short = "LOCAL";
            _thumbnail.Local = true;
            _thumbnail.Dimmed = !local.Enabled;
            _info.Name = "Local overrides";
            _info.Tags = ["GAME FOLDER", .. ConflictTag(local)];
            _info.Description = local.Folder is null
                ? "Files you drop in the game's own overrides folder. Choose the game's folder in Settings to use it."
                : local.LocalFiles == 0
                    ? "Files you drop in the game's own overrides folder. It is empty."
                    : $"Files you drop in the game's own overrides folder: {local.LocalFiles.ToString("N0", CultureInfo.InvariantCulture)} so far.";
            (_info.Detail, _info.DetailColor) = Describe(local, local.Folder?.ToUpperInvariant() ?? string.Empty);
            _size.Value = local.LocalBytes == 0 ? "—" : LauncherModel.FormatSize(local.LocalBytes / 1e6);
            Update();
            return;
        }

        if (Entry is not { } entry)
        {
            return;
        }

        bool installed = entry.Installed is not null;
        _thumbnail.Picture = entry.ThumbnailPath is { } path ? _pictures(path) : null;
        _thumbnail.Short = Short(entry.Pack.Id);
        _thumbnail.Local = false;
        _thumbnail.Dimmed = !installed || (Item is { } item && !item.Enabled);
        _info.Name = entry.Pack.Title;
        _info.Tags = Item is { } ordered ? ConflictTag(ordered) : [];
        _info.Description = entry.Pack.Summary;
        (_info.Detail, _info.DetailColor) = Item is { } listed ? Describe(listed, string.Empty) : (string.Empty, ContentInk.Caption);
        if (entry.Pack.Mod is { } mod)
        {
            _info.Tags = mod.RunsCode ? ["RUNS CODE"] : [];
            (_info.Detail, _info.DetailColor) = DescribeMod(entry, mod, Item);
        }

        _size.Value = LauncherModel.FormatSize((entry.Installed?.Size ?? entry.Pack.Size) / 1e6);
        Update();
    }

    public void Update()
    {
        LoadOrderItem? item = Item;
        if (_switch is not null && item is not null)
        {
            _switch.IsOn = item.Enabled;
        }

        if (item is { IsLocal: true } local)
        {
            _working = false;
            _busy.Visibility = Visibility.Collapsed;
            _main.Visibility = Visibility.Visible;
            _state.Set(local.Enabled ? "IN USE" : "TURNED OFF", local.Enabled ? _theme.Success : _theme.TextMuted);
            Main("OPEN FOLDER", CutButtonStyle.Outline, local.Mod is { } dropped ? () => _model.OpenModFolder(dropped.Id) : _model.OpenLocalOverrides);
            Links();
            return;
        }

        if (Entry is not { } entry)
        {
            return;
        }

        DownloadJob? job = _model.Job is { } j && j.ContentId == EntryId ? j : null;
        _working = job is not null || entry.State == ContentState.Working;
        Bgra32 accent = _theme.Accent;

        if (job is not null)
        {
            _state.Set(job.Paused ? "PAUSED" : job.Stage == Services.InstallStage.Installing ? "INSTALLING" : "DOWNLOADING", accent);
            _busy.Visibility = Visibility.Visible;
            _busy.Value = job.Progress;
            _busy.Left = job.Paused ? "Paused" : $"{LauncherModel.FormatSize(job.Done)} / {LauncherModel.FormatSize(job.Size)}";
            _busy.Right = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(job.Progress * 100f)}%");
            _main.Visibility = Visibility.Collapsed;
            Links((job.Paused ? "RESUME" : "PAUSE", _model.TogglePause), ("CANCEL", _model.Cancel));
            return;
        }

        _busy.Visibility = Visibility.Collapsed;
        _main.Visibility = Visibility.Visible;

        // A runtime stands in no load order; installed, it is in use.
        bool enabled = item?.Enabled ?? (entry.Pack.Kind == ContentKind.Runtime && entry.Enabled);
        string update = entry.DownloadBytes < 0 ? string.Empty : $" · {LauncherModel.FormatSize(entry.DownloadBytes / 1e6)}";
        // A mod with code asks before its first install: it runs on the player's machine, with the game's rights.
        if (entry.State == ContentState.Available && _confirming)
        {
            _state.Set("RUNS CODE ON THIS PC", accent);
            Main("INSTALL ANYWAY", CutButtonStyle.Filled, () =>
            {
                _confirming = false;
                _model.TrustAndInstallMod(EntryId);
            });
            Links(("CANCEL", () => _confirming = false), ("SOURCE", () => _model.OpenModSource(EntryId)));
            return;
        }

        switch (entry.State)
        {
            case ContentState.Available:
                _state.Set("NOT INSTALLED", _theme.TextMuted);
                Main("INSTALL", CutButtonStyle.Filled, () =>
                {
                    if (_model.IsTrusted(EntryId))
                    {
                        _model.InstallContent(EntryId);
                    }
                    else
                    {
                        _confirming = true;
                    }
                });
                Links(entry.Pack.Mod is { Source: not null } ? [("SOURCE", () => _model.OpenModSource(EntryId))] : []);
                break;
            case ContentState.UpdateAvailable:
                _state.Set("UPDATE" + update, accent);
                Main("UPDATE", CutButtonStyle.Filled, () => _model.InstallContent(EntryId));
                Links(("VERIFY", () => _model.VerifyContent(EntryId)), ("FOLDER", () => _model.OpenContentFolder(EntryId)), ("REMOVE", () => _model.RemoveContent(EntryId)));
                break;
            case ContentState.Damaged:
                _state.Set("DAMAGED", _theme.Danger);
                Main("REPAIR", CutButtonStyle.Filled, () => _model.InstallContent(EntryId));
                Links(("REMOVE", () => _model.RemoveContent(EntryId)));
                break;
            case ContentState.Incompatible:
                _state.Set("NEEDS NEWER GAME", _theme.TextMuted);
                Main($"NEEDS {entry.Pack.MinGameVersion}", CutButtonStyle.Disabled, null);
                Links(entry.Installed is not null ? [("REMOVE", () => _model.RemoveContent(EntryId))] : []);
                break;
            case ContentState.Working:
                _state.Set("WORKING", accent);
                Main("WORKING", CutButtonStyle.Disabled, null);
                Links();
                break;
            default:
                if (enabled && (entry.NeedsGame ?? entry.NeedsMod) is { } needs)
                {
                    // On, and not going to load: the loader leaves out a mod whose game or mod is missing.
                    _state.Set($"NEEDS {needs.ToUpperInvariant()}", ContentInk.Conflict);
                }
                else
                {
                    _state.Set(
                        !entry.Listed ? "NO LONGER OFFERED" : enabled ? "IN USE" : "TURNED OFF",
                        enabled && entry.Listed ? _theme.Success : _theme.TextMuted);
                }

                Main("REMOVE", CutButtonStyle.Danger, () => _model.RemoveContent(EntryId));
                Links(("VERIFY", () => _model.VerifyContent(EntryId)), ("FOLDER", () => _model.OpenContentFolder(EntryId)));
                break;
        }
    }

    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;

        // Carried: a soft shadow under it and a lighter glass, so it reads as lifted off the list.
        if (_lift > 0f)
        {
            for (int step = 4; step >= 1; step--)
            {
                float spread = context.Px(4f * step) * _lift;
                var shadow = new UiRect(box.X - spread, box.Y - spread + (context.Px(10f) * _lift), box.Width + (spread * 2f), box.Height + (spread * 2f));
                painter.FillRect(shadow, UiPalette.Ink.WithAlpha((byte)(34 * _lift)));
            }
        }

        // The design's row: dark glass, a hairline that warms while the pack is being worked on or carried.
        float warm = MathF.Max(_lift, _working ? 0.5f : 0f);
        Bgra32 border = Interpolate.Lerp(UiPalette.White(0.08f), _theme.Accent.WithAlpha(160), warm);
        Bgra32 tint = Interpolate.Lerp(UiPalette.Ink.WithAlpha(168), Bgra32.FromRgba(26, 28, 30, 220), _lift);
        KitDraw.Glass(painter, _theme, box, tint, null, border);

        // The accent bar the design marks the chosen row with: the one carried, or whose grip has focus.
        float bar = MathF.Max(_lift, _handle?.Visual.Focus ?? 0f);
        if (bar > 0.01f)
        {
            painter.FillRect(new UiRect(box.X, box.Y, context.Px(3f), box.Height), _theme.Accent.WithAlpha((byte)(255 * bar)));
        }
    }

    private void Toggle()
    {
        if (Item is not { } item)
        {
            return;
        }

        if (_kind == ContentPageKind.Mods)
        {
            _model.SetModEnabled(EntryId, !item.Enabled);
        }
        else
        {
            _model.SetContentEnabled(EntryId, !item.Enabled);
        }
    }

    // A mod's third line: what it still needs, else who made it and what its code was built from.
    private static (string Text, Bgra32 Color) DescribeMod(ContentEntry entry, ContentModInfo mod, LoadOrderItem? item)
    {
        if (item is { Enabled: false })
        {
            return ("OFF · THE GAME STARTS WITHOUT IT", ContentInk.Caption);
        }

        if (item is not null && entry.NeedsGame is { } game)
        {
            return ($"READS {game.ToUpperInvariant()}'S DATA · SET ITS FOLDER IN SETTINGS", ContentInk.Conflict);
        }

        if (item is not null && entry.NeedsMod is { } other)
        {
            return ($"NEEDS THE MOD {other.ToUpperInvariant()} INSTALLED AND ON", ContentInk.Conflict);
        }

        string by = mod.Authors.Count > 0 ? "BY " + string.Join(", ", mod.Authors).ToUpperInvariant() : string.Empty;
        string built = mod.Commit is { Length: > 0 } commit ? $"BUILT FROM {commit[..Math.Min(7, commit.Length)].ToUpperInvariant()}" : string.Empty;
        return (string.Join(" · ", new[] { entry.Pack.Version, by, built }.Where(static part => part.Length > 0)), ContentInk.Caption);
    }

    // A CONFLICT tag on a row another enabled layer wins files from.
    private static string[] ConflictTag(LoadOrderItem item) => item.Enabled && item.Conflict.CoveredFiles > 0 ? ["CONFLICT"] : [];

    // The third line: what this layer loses to the ones above it, else what it wins over the ones below.
    private static (string Text, Bgra32 Color) Describe(LoadOrderItem item, string fallback)
    {
        if (!item.Enabled)
        {
            return ("OFF · THE GAME STARTS WITHOUT IT", ContentInk.Caption);
        }

        if (item.Conflict.CoveredBy.Count > 0)
        {
            (string name, int files) = item.Conflict.CoveredBy[0];
            string more = item.Conflict.CoveredBy.Count > 1 ? $" AND {item.Conflict.CoveredBy.Count - 1} MORE" : string.Empty;
            return ($"{Files(item.Conflict.CoveredFiles)} COVERED BY {name.ToUpperInvariant()}{more}", ContentInk.Conflict);
        }

        if (item.Conflict.Covers.Count > 0)
        {
            (string name, int files) = item.Conflict.Covers[0];
            string more = item.Conflict.Covers.Count > 1 ? $" AND {item.Conflict.Covers.Count - 1} MORE" : string.Empty;
            return ($"WINS {Files(files)} OVER {name.ToUpperInvariant()}{more}", ContentInk.Caption);
        }

        return (fallback, ContentInk.Caption);
    }

    private static string Files(int count) =>
        count == 1 ? "1 FILE" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} FILES";

    private void Main(string text, CutButtonStyle style, Action? act)
    {
        _main.Text = text;
        _main.Style = style;
        _main.IsEnabled = act is not null;
        _mainAction = act;
    }

    private void Links(params (string Text, Action Act)[] links)
    {
        for (int i = 0; i < _linkSlots.Count; i++)
        {
            LinkButton button = _linkSlots[i].Button;
            if (i < links.Length)
            {
                button.Text = links[i].Text;
                button.Visibility = Visibility.Visible;
                _linkSlots[i] = (button, links[i].Act);
            }
            else
            {
                button.Visibility = Visibility.Collapsed;
            }
        }

        _links.Visibility = links.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // The label over the picture: the id's first word, as the design's short codes ("HD").
    private static string Short(string id) => id.Split('-')[0].ToUpperInvariant();
}
