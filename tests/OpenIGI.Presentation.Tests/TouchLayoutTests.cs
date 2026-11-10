using OpenIGI.Input;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// A player's own layout of a touch screen (<see cref="TouchLayout"/>): the shipped layout is what it was before there was one
/// to change, what is changed survives being written down and read back, and the controls stand and are pressed by it.
/// </summary>
public sealed class TouchLayoutTests
{
    private static readonly LayoutContext Phone = new(1200f, 540f);

    private static TouchButtonSpot Spot(TouchControls controls, TouchControlId id)
    {
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            if (spot.Id == id)
            {
                return spot;
            }
        }

        throw new InvalidOperationException($"{id} is not laid out.");
    }

    [Fact]
    public void The_shipped_layout_is_written_as_nothing_and_nothing_reads_as_the_shipped_layout()
    {
        Assert.Equal(string.Empty, TouchLayoutCodec.Encode(new TouchLayout()));
        Assert.True(TouchLayoutCodec.Decode(string.Empty).IsDefault);
        Assert.True(TouchLayoutCodec.Decode(null).IsDefault);
        Assert.True(TouchLayoutCodec.Decode("not a layout").IsDefault);
        Assert.True(TouchLayoutCodec.Decode("v9/c.Fire=r1,b1,s100,o100,v1").IsDefault);
    }

    [Fact]
    public void A_changed_layout_survives_being_written_and_read()
    {
        TouchLayout layout = new() { StickMode = TouchStickMode.Fixed };
        layout.Set(TouchControlId.Fire, new TouchPlacement(HudEdgeX.Right, 420f, HudEdgeY.Middle, -35f, 1.25f, 0.6f));
        layout.Set(TouchControlId.Peek, layout.Resolve(TouchControlId.Peek) with { Visible = false });
        layout.Set(TouchControlId.Flashlight, layout.Resolve(TouchControlId.Flashlight) with { Visible = true });
        layout.Set(HudElement.Vitals, new HudPlacement(40f, -120f, 0.8f, 0.5f));
        layout.Set(HudElement.Timer, HudPlacement.Default with { Visible = false });

        string text = TouchLayoutCodec.Encode(layout);
        TouchLayout read = TouchLayoutCodec.Decode(text);

        Assert.Equal(text, TouchLayoutCodec.Encode(read));
        Assert.Equal(TouchStickMode.Fixed, read.StickMode);
        Assert.Equal(new TouchPlacement(HudEdgeX.Right, 420f, HudEdgeY.Middle, -35f, 1.25f, 0.6f), read.Resolve(TouchControlId.Fire));
        Assert.False(read.Resolve(TouchControlId.Peek).Visible);
        Assert.True(read.Resolve(TouchControlId.Flashlight).Visible);
        Assert.Equal(new HudPlacement(40f, -120f, 0.8f, 0.5f), read.Resolve(HudElement.Vitals));
        Assert.False(read.Resolve(HudElement.Timer).Visible);

        // Only what was changed is written: the reload is still the game's to place.
        Assert.False(read.Controls.ContainsKey(TouchControlId.Reload));
        Assert.DoesNotContain("Reload", text, StringComparison.Ordinal);

        // A settings file and a console carry it whole: nothing in it needs quoting or ends a command.
        Assert.DoesNotContain(text, static c => char.IsWhiteSpace(c) || c is '"' or ';' or '\'');
    }

    [Fact]
    public void A_button_added_twice_survives_being_written_and_read_and_both_are_pressed_in_play()
    {
        TouchLayout layout = new();
        Assert.Equal(0, layout.Add(TouchControlId.Jump, new TouchPlacement(HudEdgeX.Left, 500f, HudEdgeY.Bottom, 300f)));
        Assert.Equal(1, layout.Add(TouchControlId.Jump, new TouchPlacement(HudEdgeX.Centre, 0f, HudEdgeY.Middle, 0f, 1.5f, 0.5f)));
        Assert.Equal(-1, layout.Add(TouchControlId.Stick, default));

        TouchLayout read = TouchLayoutCodec.Decode(TouchLayoutCodec.Encode(layout));
        Assert.Equal(layout.Extras, read.Extras);
        Assert.False(read.IsDefault);

        TouchState touch = new();
        TouchControls controls = new() { Arrangement = read };
        controls.Update(touch, TouchControlMode.Play, Phone);
        TouchButtonSpot[] jumps = [.. controls.Layout.Where(static s => s.Button == TouchButton.Jump)];
        Assert.Equal(3, jumps.Length);
        Assert.Equal([-1, 0, 1], jumps.Select(static s => s.Extra));

        foreach (TouchButtonSpot jump in jumps)
        {
            touch.BeginFrame();
            touch.Down(1, jump.Centre.X, jump.Centre.Y);
            controls.Update(touch, TouchControlMode.Play, Phone);
            Assert.Contains(TouchButton.Jump, controls.Held);
            touch.BeginFrame();
            touch.Up(1, jump.Centre.X, jump.Centre.Y);
            controls.Update(touch, TouchControlMode.Play, Phone);
        }
    }

    [Fact]
    public void A_part_that_is_not_understood_is_left_out_and_the_rest_still_read()
    {
        TouchLayout read = TouchLayoutCodec.Decode("v1/c.Teleport=r1,b1,s100,o100,v1/c.Jump=l100,t200,s150,o50,v1/h.Compass=x1,y1,s100,o100,v1/rubbish/c.Scope=r1");

        Assert.Single(read.Controls);
        Assert.Empty(read.Hud);
        Assert.Equal(new TouchPlacement(HudEdgeX.Left, 100f, HudEdgeY.Top, 200f, 1.5f, 0.5f), read.Resolve(TouchControlId.Jump));
    }

    [Fact]
    public void A_layout_keeps_sizes_and_opacities_in_range_and_the_pause_button_on_the_screen()
    {
        TouchLayout layout = new();
        layout.Set(TouchControlId.Jump, layout.Resolve(TouchControlId.Jump) with { Scale = 9f, Opacity = 0f });
        layout.Set(TouchControlId.Pause, layout.Resolve(TouchControlId.Pause) with { Visible = false });

        Assert.Equal(TouchLayout.MaxScale, layout.Resolve(TouchControlId.Jump).Scale);
        Assert.Equal(TouchLayout.MinOpacity, layout.Resolve(TouchControlId.Jump).Opacity);
        Assert.True(layout.Resolve(TouchControlId.Pause).Visible);
    }

    [Fact]
    public void The_shipped_layout_stands_where_the_controls_always_stood()
    {
        TouchControls controls = new();
        controls.Update(new TouchState(), TouchControlMode.Play, Phone);

        TouchButtonSpot fire = Spot(controls, TouchControlId.Fire);
        Assert.Equal(1200f - Phone.Px(300f), fire.Centre.X, 3);
        Assert.Equal(540f - Phone.Px(230f), fire.Centre.Y, 3);
        Assert.Equal(Phone.Px(110f), fire.Radius, 3);
        Assert.True(fire.Aims);

        TouchButtonSpot weapon = Spot(controls, TouchControlId.Weapon);
        Assert.True(weapon.Card);
        Assert.Equal(600f, weapon.Centre.X, 3);

        Assert.Equal(Phone.Px(250f), controls.StickRest.X, 3);
        Assert.Equal(540f - Phone.Px(230f), controls.StickRest.Y, 3);
        Assert.Equal(Phone.Px(TouchControls.StickReach), controls.StickReachPixels, 3);

        // What the player has not added is not there.
        Assert.DoesNotContain(controls.Layout, static s => s.Id is TouchControlId.Flashlight or TouchControlId.WeaponWheel or TouchControlId.QuickSave);
    }

    [Theory]
    [InlineData(1200f, 540f)]
    [InlineData(2400f, 1080f)]
    [InlineData(1024f, 768f)]
    [InlineData(2532f, 1170f)]
    public void Every_control_of_the_shipped_layout_is_on_the_screen_whatever_its_shape(float width, float height)
    {
        LayoutContext screen = new(width, height);
        TouchControls controls = new() { Editing = true };
        controls.Update(new TouchState(), TouchControlMode.Edit, screen);

        Assert.Equal(TouchLayout.AllControls.Count - 1, controls.Layout.Count);
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            Assert.InRange(spot.Centre.X, 0f, width);
            Assert.InRange(spot.Centre.Y, 0f, height);
        }
    }

    [Fact]
    public void The_top_rows_close_up_over_a_button_the_session_does_not_offer()
    {
        TouchControls controls = new() { ChatShown = true };
        controls.Update(new TouchState(), TouchControlMode.Play, Phone);

        // No voice in this room: the chat stands where the voice would have, the fourth along from the corner.
        TouchButtonSpot chat = Spot(controls, TouchControlId.Chat);
        Assert.Equal(1200f - Phone.Px(80f + (3f * 36f * 2.6f)), chat.Centre.X, 2);
    }

    [Fact]
    public void A_place_on_the_screen_and_the_placement_for_it_agree()
    {
        TouchLayout layout = new();
        foreach ((float x, float y) in new[] { (60f, 60f), (600f, 270f), (1150f, 500f), (300f, 400f), (900f, 100f) })
        {
            TouchPlacement placed = TouchLayout.MovedTo(layout.Resolve(TouchControlId.Jump), new UiVec2(x, y), Phone);
            UiVec2 back = TouchLayout.CentreOf(placed, Phone);
            Assert.Equal(x, back.X, 2);
            Assert.Equal(y, back.Y, 2);
        }
    }

    [Fact]
    public void A_control_stands_and_is_pressed_where_the_layout_puts_it_and_one_taken_off_is_not_pressed()
    {
        TouchLayout layout = new();
        layout.Set(TouchControlId.Jump, TouchLayout.MovedTo(layout.Resolve(TouchControlId.Jump), new UiVec2(700f, 150f), Phone));
        layout.Set(TouchControlId.Reload, layout.Resolve(TouchControlId.Reload) with { Visible = false });
        layout.Set(TouchControlId.Flashlight, layout.Resolve(TouchControlId.Flashlight) with { Visible = true });

        TouchState touch = new();
        TouchControls controls = new() { Arrangement = layout };
        controls.Update(touch, TouchControlMode.Play, Phone);
        UiVec2 reloadWas = TouchLayout.CentreOf(new TouchLayout().Resolve(TouchControlId.Reload), Phone);
        TouchButtonSpot flashlight = Spot(controls, TouchControlId.Flashlight);

        touch.BeginFrame();
        touch.Down(1, 700f, 150f);
        touch.Down(2, reloadWas.X, reloadWas.Y);
        touch.Down(3, flashlight.Centre.X, flashlight.Centre.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);

        Assert.Contains(TouchButton.Jump, controls.Held);
        Assert.Contains(TouchButton.Flashlight, controls.Held);
        Assert.DoesNotContain(TouchButton.Reload, controls.Held);
    }

    [Fact]
    public void Where_one_button_laps_another_the_smaller_is_the_one_pressed()
    {
        TouchLayout layout = new();
        UiVec2 fire = TouchLayout.CentreOf(layout.Resolve(TouchControlId.Fire), Phone);
        layout.Set(TouchControlId.Crouch, TouchLayout.MovedTo(layout.Resolve(TouchControlId.Crouch), fire, Phone));

        TouchState touch = new();
        TouchControls controls = new() { Arrangement = layout };
        controls.Update(touch, TouchControlMode.Play, Phone);
        touch.BeginFrame();
        touch.Down(1, fire.X, fire.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);

        Assert.Contains(TouchButton.Crouch, controls.Held);
        Assert.DoesNotContain(TouchButton.Fire, controls.Held);
    }

    [Fact]
    public void A_fixed_stick_is_taken_only_by_a_thumb_on_it_and_pushes_from_where_it_is_drawn()
    {
        TouchState touch = new();
        TouchControls controls = new() { Arrangement = new TouchLayout { StickMode = TouchStickMode.Fixed } };
        controls.Update(touch, TouchControlMode.Play, Phone);
        UiVec2 rest = controls.StickRest;

        // Far from the stick on its own side: a floating stick would take this thumb; a fixed one leaves it to the look.
        touch.BeginFrame();
        touch.Down(1, 40f, 200f);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Null(controls.StickOrigin);

        touch.BeginFrame();
        touch.Down(2, rest.X + 20f, rest.Y);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Equal(rest, controls.StickOrigin);
        Assert.True(controls.Stick.X > 0f);
    }

    [Fact]
    public void A_stick_moved_to_the_right_takes_thumbs_on_the_right()
    {
        TouchLayout layout = new();
        layout.Set(TouchControlId.Stick, new TouchPlacement(HudEdgeX.Right, 250f, HudEdgeY.Bottom, 230f));
        TouchState touch = new();
        TouchControls controls = new() { Arrangement = layout };
        controls.Update(touch, TouchControlMode.Play, Phone);

        touch.BeginFrame();
        touch.Down(1, 150f, 300f);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.Null(controls.StickOrigin);

        touch.BeginFrame();
        touch.Down(2, 1000f, 200f);
        controls.Update(touch, TouchControlMode.Play, Phone);
        Assert.NotNull(controls.StickOrigin);
    }

    [Fact]
    public void The_display_moves_a_readout_as_the_layout_moves_it_and_leaves_out_one_it_hides()
    {
        TouchLayout layout = new();
        layout.Set(HudElement.Vitals, new HudPlacement(100f, -50f, 1.5f));
        layout.Set(HudElement.Timer, HudPlacement.Default with { Visible = false });
        HudTheme theme = new(
            "test", 640, 480, null!, null!, default,
            new Dictionary<HudElement, HudAnchor>
            {
                [HudElement.Vitals] = new(HudEdgeX.Left, 10f, HudEdgeY.Bottom, 10f),
                [HudElement.Timer] = new(HudEdgeX.Centre, 0f, HudEdgeY.Top, 14f),
            });

        HudLayoutOverrides arranged = new(layout);
        HudDrawContext moved = new(null!, null!, theme, null!, null!, null!, 1920f, 1080f, 2f) { Layout = arranged };
        HudDrawContext plain = new(null!, null!, theme, null!, null!, null!, 1920f, 1080f, 2f);

        Assert.Equal(new UiVec2(20f, 1060f), plain.Place(HudElement.Vitals));
        Assert.Equal(new UiVec2(120f, 1010f), moved.Place(HudElement.Vitals));
        Assert.NotNull(plain.Place(HudElement.Timer));
        Assert.Null(moved.Place(HudElement.Timer));

        // The editor still shows what is hidden, to bring it back.
        arranged.Editing = true;
        Assert.NotNull(moved.Place(HudElement.Timer));
    }
}

/// <summary>
/// The layout's editor (<see cref="TouchLayoutEditor"/>): a finger moves what it lands on and keeps it on the screen, the bar
/// resizes, fades, hides and resets, each of the three layouts keeps its own changes, and nothing is saved until the player says.
/// </summary>
public sealed class TouchLayoutEditorTests
{
    private static readonly LayoutContext Phone = new(1200f, 540f);

    private readonly TouchState _touch = new();
    private readonly TouchControls _controls = new();
    private readonly TouchLayoutEditor _editor = new();
    private HudLayoutOverrides? _readouts;

    public TouchLayoutEditorTests()
    {
        _editor.Open([string.Empty, string.Empty, string.Empty], 0);
        Frame();
    }

    private void Frame(Action<TouchState>? fingers = null)
    {
        _touch.BeginFrame();
        fingers?.Invoke(_touch);
        _controls.Update(_touch, TouchControlMode.Edit, Phone);
        _editor.Update(_touch, Phone, _controls, _readouts);
    }

    private void Tap(UiVec2 at)
    {
        Frame(t => t.Down(1, at.X, at.Y));
        Frame(t => t.Up(1, at.X, at.Y));
        Frame();
    }

    private void Drag(UiVec2 from, UiVec2 to)
    {
        Frame(t => t.Down(1, from.X, from.Y));
        Frame(t => t.Move(1, (from.X + to.X) * 0.5f, (from.Y + to.Y) * 0.5f));
        Frame(t => t.Move(1, to.X, to.Y));
        Frame(t => t.Up(1, to.X, to.Y));
        Frame();
    }

    private UiVec2 Centre(TouchControlId id)
    {
        foreach (TouchEditorItem item in _editor.Items)
        {
            if (!item.Item.IsReadout && item.Item.Control == id)
            {
                return item.Box.Center;
            }
        }

        throw new InvalidOperationException($"{id} is not in the editor.");
    }

    private TouchEditorControl Widget(TouchEditorWidget widget)
    {
        foreach (TouchEditorControl control in _editor.Bar)
        {
            if (control.Widget == widget)
            {
                return control;
            }
        }

        throw new InvalidOperationException($"{widget} is not on the bar.");
    }

    [Fact]
    public void The_editor_shows_every_control_even_those_the_session_does_not_offer_or_the_layout_hides()
    {
        foreach (TouchControlId id in TouchLayout.AllControls)
        {
            Assert.Contains(_editor.Items, item => !item.Item.IsReadout && item.Item.Control == id);
        }

        Assert.Contains(_editor.Items, static item => item.Item.Control == TouchControlId.Flashlight && item.Hidden);
        Assert.Empty(_controls.Held);
    }

    [Fact]
    public void A_finger_drags_what_it_lands_on_and_a_step_back_undoes_it()
    {
        UiVec2 was = Centre(TouchControlId.Jump);
        Drag(was, new UiVec2(was.X - 100f, was.Y - 40f));

        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Jump), _editor.Selected);
        Assert.True(_editor.Working.Controls.ContainsKey(TouchControlId.Jump));
        UiVec2 now = Centre(TouchControlId.Jump);
        Assert.InRange(now.X, was.X - 103f, was.X - 97f);
        Assert.InRange(now.Y, was.Y - 43f, was.Y - 37f);
        Assert.True(_editor.Dirty);

        Tap(Widget(TouchEditorWidget.Undo).Box.Center);
        Assert.False(_editor.Working.Controls.ContainsKey(TouchControlId.Jump));
        Assert.Equal(was, Centre(TouchControlId.Jump));
        Assert.False(_editor.Dirty);
    }

    [Fact]
    public void A_tap_selects_without_moving_anything()
    {
        Tap(Centre(TouchControlId.Scope));

        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Scope), _editor.Selected);
        Assert.True(_editor.Working.IsDefault);
        Assert.False(_editor.CanUndo);
    }

    [Fact]
    public void A_dragged_control_is_kept_on_the_screen()
    {
        Drag(Centre(TouchControlId.Fire), new UiVec2(5000f, 5000f));

        foreach (TouchEditorItem item in _editor.Items)
        {
            if (item.Item == TouchLayoutItem.Of(TouchControlId.Fire))
            {
                Assert.InRange(item.Box.Right, 0f, 1200.5f);
                Assert.InRange(item.Box.Bottom, 0f, 540.5f);
                return;
            }
        }

        Assert.Fail("Fire is not in the editor.");
    }

    [Fact]
    public void A_dragged_control_catches_on_the_line_through_another()
    {
        UiVec2 jump = Centre(TouchControlId.Jump);
        UiVec2 scope = Centre(TouchControlId.Scope);

        // Left of the sight and three pixels off its level: it falls to the sight's own.
        Frame(t => t.Down(1, jump.X, jump.Y));
        Frame(t => t.Move(1, scope.X - 150f, scope.Y + 3f));
        Assert.Equal(scope.Y, _editor.GuideY);
        Assert.Equal(scope.Y, Centre(TouchControlId.Jump).Y, 1);
        Frame(t => t.Up(1, scope.X - 150f, scope.Y + 3f));
        Frame();
        Assert.Null(_editor.GuideY);
    }

    [Fact]
    public void The_bar_takes_a_control_off_the_screen_and_puts_it_back_but_never_the_pause_button()
    {
        Tap(Centre(TouchControlId.Peek));
        Assert.True(Widget(TouchEditorWidget.Show).Active);
        Tap(Widget(TouchEditorWidget.Show).Box.Center);
        Assert.False(_editor.Working.Resolve(TouchControlId.Peek).Visible);
        Assert.False(Widget(TouchEditorWidget.Show).Active);

        Tap(Widget(TouchEditorWidget.Show).Box.Center);
        Assert.True(_editor.Working.Resolve(TouchControlId.Peek).Visible);

        Tap(Centre(TouchControlId.Pause));
        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Pause), _editor.Selected);
        Assert.False(Widget(TouchEditorWidget.Show).Enabled);
        Tap(Widget(TouchEditorWidget.Show).Box.Center);
        Assert.True(_editor.Working.Resolve(TouchControlId.Pause).Visible);
    }

    [Fact]
    public void The_sliders_set_the_selected_controls_size_and_opacity_and_reset_puts_it_back()
    {
        Tap(Centre(TouchControlId.Reload));
        TouchEditorControl size = Widget(TouchEditorWidget.Size);
        (float from, float to) = TouchLayoutEditor.SliderTrack(size.Box);
        Tap(new UiVec2(to, size.Box.Center.Y));
        Assert.Equal(TouchLayout.MaxScale, _editor.Working.Resolve(TouchControlId.Reload).Scale);

        TouchEditorControl opacity = Widget(TouchEditorWidget.Opacity);
        (from, _) = TouchLayoutEditor.SliderTrack(opacity.Box);
        Tap(new UiVec2(from, opacity.Box.Center.Y));
        Assert.Equal(TouchLayout.MinOpacity, _editor.Working.Resolve(TouchControlId.Reload).Opacity, 3);

        Tap(Widget(TouchEditorWidget.ResetItem).Box.Center);
        Assert.False(_editor.Working.Controls.ContainsKey(TouchControlId.Reload));
    }

    [Fact]
    public void Two_fingers_pinch_the_size_of_what_the_first_holds()
    {
        UiVec2 fire = Centre(TouchControlId.Fire);
        Frame(t => t.Down(1, fire.X, fire.Y));
        Frame(t => t.Down(2, fire.X - 100f, fire.Y));
        Frame(t => t.Move(2, fire.X - 150f, fire.Y));

        Assert.Equal(1.5f, _editor.Working.Resolve(TouchControlId.Fire).Scale, 2);

        Frame(t => t.Up(2, fire.X - 150f, fire.Y));
        Frame(t => t.Move(1, fire.X - 200f, fire.Y));
        Frame(t => t.Up(1, fire.X - 200f, fire.Y));

        // The finger left after a pinch does not drag: the control is where it was, only larger.
        Assert.Equal(fire.X, Centre(TouchControlId.Fire).X, 1);
    }

    [Fact]
    public void Each_layout_keeps_its_own_changes_and_saving_tells_all_three_and_which_is_in_use()
    {
        IReadOnlyList<string>? saved = null;
        int savedSlot = -1;
        _editor.Saved += (layouts, slot) => (saved, savedSlot) = ([.. layouts], slot);

        UiVec2 jump = Centre(TouchControlId.Jump);
        Drag(jump, new UiVec2(jump.X - 100f, jump.Y));
        Tap(Widget(TouchEditorWidget.Slot2).Box.Center);
        Assert.Equal(1, _editor.Slot);
        Assert.True(_editor.Working.IsDefault);
        Assert.Equal(jump, Centre(TouchControlId.Jump));

        Assert.True(Widget(TouchEditorWidget.Save).Enabled);
        Tap(Widget(TouchEditorWidget.Save).Box.Center);

        Assert.NotNull(saved);
        Assert.Equal(1, savedSlot);
        Assert.Contains("c.Jump=", saved![0], StringComparison.Ordinal);
        Assert.Equal(string.Empty, saved[1]);
        Assert.False(_editor.Dirty);
        Assert.True(_editor.IsOpen);
    }

    [Fact]
    public void Leaving_with_changes_asks_first_and_discarding_saves_nothing()
    {
        bool saved = false, closed = false;
        _editor.Saved += (_, _) => saved = true;
        _editor.Closed += () => closed = true;

        UiVec2 jump = Centre(TouchControlId.Jump);
        Drag(jump, new UiVec2(jump.X - 100f, jump.Y));
        Tap(Widget(TouchEditorWidget.Exit).Box.Center);
        Assert.True(_editor.Prompting);
        Assert.True(_editor.IsOpen);

        // While the question is up nothing under it is taken hold of.
        Tap(Centre(TouchControlId.Scope));
        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Jump), _editor.Selected);

        Tap(Widget(TouchEditorWidget.PromptDiscard).Box.Center);
        Assert.False(_editor.IsOpen);
        Assert.True(closed);
        Assert.False(saved);
    }

    [Fact]
    public void Leaving_with_nothing_changed_leaves_at_once()
    {
        Tap(Widget(TouchEditorWidget.Exit).Box.Center);
        Assert.False(_editor.IsOpen);
    }

    [Fact]
    public void Reset_all_puts_the_layout_back_as_shipped_and_can_be_undone()
    {
        UiVec2 jump = Centre(TouchControlId.Jump);
        Drag(jump, new UiVec2(jump.X - 100f, jump.Y));
        Tap(Widget(TouchEditorWidget.StickMode).Box.Center);
        Assert.Equal(TouchStickMode.Fixed, _editor.Working.StickMode);

        Tap(Widget(TouchEditorWidget.ResetAll).Box.Center);
        Assert.True(_editor.Working.IsDefault);

        Tap(Widget(TouchEditorWidget.Undo).Box.Center);
        Assert.Equal(TouchStickMode.Fixed, _editor.Working.StickMode);
        Assert.True(_editor.Working.Controls.ContainsKey(TouchControlId.Jump));
    }

    [Fact]
    public void The_list_adds_another_of_a_button_which_is_moved_like_any_other_and_can_be_taken_away()
    {
        Tap(Widget(TouchEditorWidget.Add).Box.Center);
        Assert.True(_editor.Adding);
        TouchEditorControl jump = _editor.Bar.First(static c => c.Widget == TouchEditorWidget.PaletteItem && c.Kind == TouchControlId.Jump);
        Tap(jump.Box.Center);

        Assert.False(_editor.Adding);
        Assert.Single(_editor.Working.Extras);
        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Jump, 0), _editor.Selected);
        Assert.Equal(2, _editor.Items.Count(static i => !i.Item.IsReadout && i.Item.Control == TouchControlId.Jump));
        Assert.False(_editor.Working.Controls.ContainsKey(TouchControlId.Jump));

        // Dragged, the added one moves and the game's own stays.
        UiVec2 own = Centre(TouchControlId.Jump);
        TouchEditorItem added = _editor.Items.First(static i => i.Item.IsExtra);
        Drag(added.Box.Center, new UiVec2(added.Box.Center.X - 200f, added.Box.Center.Y + 100f));
        Assert.Equal(own, Centre(TouchControlId.Jump));
        TouchEditorItem moved = _editor.Items.First(static i => i.Item.IsExtra);
        Assert.InRange(moved.Box.Center.X, added.Box.Center.X - 206f, added.Box.Center.X - 194f);

        Tap(Widget(TouchEditorWidget.ResetItem).Box.Center);
        Assert.Empty(_editor.Working.Extras);
        Assert.Null(_editor.Selected);

        Tap(Widget(TouchEditorWidget.Undo).Box.Center);
        Assert.Single(_editor.Working.Extras);
    }

    [Fact]
    public void Adding_a_button_the_layout_hides_shows_it_and_a_tap_off_the_list_puts_the_list_away()
    {
        Tap(Widget(TouchEditorWidget.Add).Box.Center);
        Tap(_editor.Bar.First(static c => c.Widget == TouchEditorWidget.PaletteItem && c.Kind == TouchControlId.Flashlight).Box.Center);
        Assert.Empty(_editor.Working.Extras);
        Assert.True(_editor.Working.Resolve(TouchControlId.Flashlight).Visible);
        Assert.Equal(TouchLayoutItem.Of(TouchControlId.Flashlight), _editor.Selected);

        Tap(Widget(TouchEditorWidget.Add).Box.Center);
        Assert.True(_editor.Adding);
        Tap(new UiVec2(_editor.PaletteBox.X - 20f, _editor.PaletteBox.Center.Y));
        Assert.False(_editor.Adding);
        Assert.Empty(_editor.Working.Extras);
    }

    [Fact]
    public void The_bar_goes_to_the_screens_other_edge_and_a_finger_on_it_moves_nothing_under_it()
    {
        Assert.True(_editor.BarBox.Y < 270f);
        Tap(Widget(TouchEditorWidget.Flip).Box.Center);
        Assert.True(_editor.BarAtBottom);
        Assert.True(_editor.BarBox.Y > 270f);

        // The weapon's card is under the bar now: a drag begun on the bar leaves it be.
        UiVec2 onBar = new(_editor.BarBox.X + 4f, _editor.BarBox.Center.Y);
        Drag(onBar, new UiVec2(onBar.X, onBar.Y - 200f));
        Assert.True(_editor.Working.IsDefault);
    }

    [Fact]
    public void A_readout_of_the_display_is_dragged_by_the_box_it_was_drawn_in()
    {
        _readouts = new HudLayoutOverrides(new TouchLayout());
        _readouts.Boxes[HudElement.Vitals] = new UiRect(10f, 400f, 60f, 130f);
        Frame();
        Assert.Contains(_editor.Items, static item => item.Item == TouchLayoutItem.Of(HudElement.Vitals));

        // Up into open screen, clear of every control and guide.
        Drag(new UiVec2(40f, 465f), new UiVec2(340f, 215f));

        Assert.Equal(TouchLayoutItem.Of(HudElement.Vitals), _editor.Selected);
        HudPlacement moved = _editor.Working.Resolve(HudElement.Vitals);
        float unit = HudLayoutOverrides.Unit(540f);
        Assert.InRange(moved.Dx * unit, 290f, 310f);
        Assert.InRange(moved.Dy * unit, -260f, -240f);
        Assert.Same(_editor.Working, _readouts.Layout);
        Assert.True(_readouts.Editing);
    }
}
