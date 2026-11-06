using OpenIGI.Core.Collections;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>Rebindable shortcuts, the command palette, notifications and dragging between widgets.</summary>
public sealed class EditorShellTests
{
    /// <summary>An action that throws is reported to the table's handler, and the caller carries on.</summary>
    [Fact]
    public void A_failing_action_is_reported_not_thrown()
    {
        ActionTable table = new();
        (EditorAction? Action, Exception? Error) heard = default;
        table.Failed = (action, error) => heard = (action, error);
        EditorAction broken = table.Add(new EditorAction("test.broken", "Broken", static () => throw new InvalidOperationException("boom")));

        Assert.True(broken.TryExecute());
        Assert.Same(broken, heard.Action);
        Assert.Equal("boom", heard.Error?.Message);

        table.Failed = null;
        Assert.Throws<InvalidOperationException>(() => broken.TryExecute());
    }

    /// <summary>Only changed shortcuts are saved; loading them back rebinds, and reset restores the defaults.</summary>
    [Fact]
    public void Shortcuts_save_only_what_changed_and_load_back()
    {
        ActionTable table = Table(out _, out _);
        Assert.Equal(string.Empty, table.SaveBindings());

        table.Rebind("file.save", KeyChord.Parse("Ctrl+Shift+S"));
        table.Rebind("edit.undo", null);
        string saved = table.SaveBindings();
        Assert.Equal("file.save=Ctrl+Shift+S;edit.undo=", saved);

        ActionTable fresh = Table(out EditorAction freshSave, out EditorAction freshUndo);
        fresh.LoadBindings(saved + ";gone.action=Ctrl+Q;file.save=NotAKey");
        Assert.Equal(KeyChord.Parse("Ctrl+Shift+S"), freshSave.Shortcut);
        Assert.Null(freshUndo.Shortcut);

        fresh.ResetBindings();
        Assert.Equal(KeyChord.Parse("Ctrl+Z"), freshUndo.Shortcut);
        Assert.Equal(KeyChord.Parse("Ctrl+S"), freshSave.Shortcut);
    }

    /// <summary>Binding a shortcut another action has reports the clash.</summary>
    [Fact]
    public void Rebinding_reports_clashes()
    {
        ActionTable table = Table(out _, out EditorAction undo);
        IReadOnlyList<EditorAction> clashes = table.Rebind("file.save", KeyChord.Parse("Ctrl+Z"));
        Assert.Same(undo, Assert.Single(clashes));
    }

    /// <summary>The palette ranks as the console does, runs the chosen entry on Enter, and closes.</summary>
    [Fact]
    public void Palette_finds_and_runs()
    {
        EditorHarness harness = new();
        harness.Frame();
        string? ran = null;
        CommandPalette palette = CommandPalette.Open(harness.Root,
        [
            new PaletteEntry("Save Mission", () => ran = "save"),
            new PaletteEntry("Show Grid", () => ran = "grid"),
            new PaletteEntry("Grid Snap", () => ran = "snap"),
        ]);
        harness.Type("grid");
        Assert.Equal(["Grid Snap", "Show Grid"], palette.Matches.Select(static m => m.Title));

        harness.Key(KeyCode.Down);
        harness.Key(KeyCode.Return);
        Assert.Equal("grid", ran);
        Assert.Empty(harness.Root.Popups);
    }

    /// <summary>A notification shows, the same one again does not stack, and it goes once its time is up.</summary>
    [Fact]
    public void Toasts_expire()
    {
        Toasts toasts = new();
        toasts.Show("Saved.", ToastKind.Success, seconds: 1f);
        toasts.Show("Saved.", ToastKind.Success, seconds: 1f);
        Assert.Single(toasts.Messages);

        toasts.Update(1.5f);
        Assert.Empty(toasts.Messages);
    }

    /// <summary>A tile dragged out is dropped on a target that takes it, and only there.</summary>
    [Fact]
    public void Tiles_drag_to_a_drop_target()
    {
        EditorHarness harness = new();
        Selection<string> selection = new();
        TileView<string> tiles = new(harness.Theme, new TileSource<string>
        {
            Items = static () => ["crate", "tower"],
            Label = static item => item,
            Drag = static item => new DragDropData(item, item),
        }, selection);
        Target target = new(harness.Theme);
        SplitHost host = new(harness.Theme, tiles, target);
        harness.Root.Content = host;
        harness.Frame();

        UiVec2 first = new(tiles.Bounds.X + harness.Theme.Px(30f), tiles.Bounds.Y + harness.Theme.Px(30f));
        harness.Drag(first, target.Bounds.Center, steps: 6);
        Assert.Equal("crate", target.Dropped);
        Assert.Equal(["crate"], selection.Items);
    }

    private static ActionTable Table(out EditorAction save, out EditorAction undo)
    {
        ActionTable table = new();
        save = table.Add(new EditorAction("file.save", "Save", static () => { }) { Shortcut = KeyChord.Parse("Ctrl+S") });
        undo = table.Add(new EditorAction("edit.undo", "Undo", static () => { }) { Shortcut = KeyChord.Parse("Ctrl+Z") });
        return table;
    }

    private sealed class Target(UI.Editor.Theme.EditorTheme theme) : EditorElement(theme), IDropTarget
    {
        public string? Dropped { get; private set; }

        public bool CanDrop(DragDropData drag) => drag.Payload is string;

        public void DragOver(DragDropData drag, UiVec2 point)
        {
        }

        public void Drop(DragDropData drag, UiVec2 point) => Dropped = (string)drag.Payload;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;
    }

    // Two widgets side by side, left and right halves.
    private sealed class SplitHost : EditorElement
    {
        private readonly EditorElement _left;
        private readonly EditorElement _right;

        public SplitHost(UI.Editor.Theme.EditorTheme theme, EditorElement left, EditorElement right)
            : base(theme)
        {
            (_left, _right) = (left, right);
            AddChild(left);
            AddChild(right);
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
        {
            float half = contentRect.Width * 0.5f;
            _left.Place(context, new UiRect(contentRect.X, contentRect.Y, half, contentRect.Height));
            _right.Place(context, new UiRect(contentRect.X + half, contentRect.Y, half, contentRect.Height));
        }
    }
}
