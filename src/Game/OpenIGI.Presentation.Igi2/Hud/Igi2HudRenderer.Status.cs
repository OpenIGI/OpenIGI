using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>Retail status art, ammunition, interaction prompts, progress and view flashes.</content>
public sealed partial class Igi2HudRenderer
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private Igi2SpriteResources? _assets;
    private BitmapTextRenderer? _font;
    private int _loadedRounds = -1;
    private int _availableRounds = -1;
    private string _ammunition = string.Empty;
    private int _timerSeconds = -1;
    private string _timer = string.Empty;
    private float _scale = 1f;
    private string _weaponName = string.Empty;
    private string _fireMode = string.Empty;
    private string _weaponLabel = string.Empty;
    private static readonly Bgra32 Blue = Bgra32.FromRgba(102, 153, 255, 255);

    /// <summary>Creates the title's HUD presenter.</summary>
    /// <param name="device">The sprite upload device.</param>
    /// <param name="logger">The load diagnostics.</param>
    public Igi2HudRenderer(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        _device = device;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Load(VirtualFileSystem files)
    {
        Dispose();
        _assets = new Igi2SpriteResources(_device, files, _logger);
        // statusscreen.c StatusScreen_CreateHandler: persistent weapon/ammo text uses ComputerMenu's font3, not fontmp -- and the
        // computer's font3 is the file font1.fnt (ComputerMenu_LoadComputerMenuResource loads its fonts one and three the other way round).
        if (_assets.Get("font1.fnt") is { IsFont: true } font) { _font = new BitmapTextRenderer(font); }
        // Upload persistent status art before gameplay draws it.
        string[] names = ["stance_standing.spr", "stance_crouching.spr", "stance_lying.spr", "stance_flying.spr", "stance_running.spr",
            "powerbarbackground.spr", "powerbar.spr", "visibilitymeter.spr", "visibilitybar.spr", "visibilityalert.spr", "progressbar.spr",
            "action_picklock.spr", "action_searchcab.spr", "action_comput.spr", "action_pressbutton.spr", BinocularSprite, InfraredSprite, ScopeCornerSprite, DragunovOpticsSprite, G11AimSprite, Psg1MilDotSprite, "a_door.spr", "a_fence.spr", "a_ladder.spr", "a_wire.spr", "a_gun.spr", "a_codepad.spr"];
        foreach (string name in names) { _assets.Get(name); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _remastered?.Dispose();
        _remastered = null;
        _font = null;
        _textFont = null;
        if (_bandStripes != TextureHandle.None) { _device.DestroyTexture(_bandStripes); _bandStripes = TextureHandle.None; }
        if (_bandPlain != TextureHandle.None) { _device.DestroyTexture(_bandPlain); _bandPlain = TextureHandle.None; }
        _logFont = null;
        _assets?.Dispose();
        _assets = null;
    }

    private static void DrawEffects(SpriteBatch batch, Igi2HumanPlayer human, int width, int height, bool damage = true)
    {
        if (human.CameraMode != 1) { return; }
        // humanplayer.c HumanPlayer_DrawDamageFlash: four independently fading corner opacities.
        IReadOnlyList<float> flash = human.DamageFlash;
        if (damage && (flash[0] != 0f || flash[1] != 0f || flash[2] != 0f || flash[3] != 0f))
        {
            Bgra32 Corner(int i) => Bgra32.FromRgba(255, 0, 0, Channel(flash[i]));
            batch.DrawQuad(TextureHandle.None, new Vertex2D(0f, 0f, 0f, 0f, Corner(0)),
                new Vertex2D(width, 0f, 1f, 0f, Corner(3)), new Vertex2D(width, height, 1f, 1f, Corner(2)),
                new Vertex2D(0f, height, 0f, 1f, Corner(1)));
        }
        // humanplayer.c HumanPlayer_DrawFlashbangEffect.
        if (human.FlashBangFlash > 0f)
        {
            batch.FillRect(new RectF(0f, 0f, width, height), Bgra32.FromRgba(255, 255, 255, Channel(MathF.Min(human.FlashBangFlash, 0.99f))));
        }
    }

    private void DrawStatus(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        Igi2HumanPlayer human = title.Human;
        // humanplayer.c HumanPlayer_StatusDisplayHandler: posture and health at the lower left, visibility beside them.
        string stance = human.Pose switch
        {
            Igi2HumanPose.Crouching => "stance_crouching.spr",
            Igi2HumanPose.LyingFlat => "stance_lying.spr",
            _ => human.IsRunning ? "stance_running.spr" : "stance_standing.spr",
        };
        float health = human.Damage.MaxDamage > 0f ? Math.Clamp(1f - (human.Damage.Damage / human.Damage.MaxDamage), 0f, 1f) : 0f;
        if (_assets?.Get("powerbarbackground.spr") is { } background && _assets.Get("powerbar.spr") is { } power)
        {
            SpriteFrame frame = background.Asset.Frames[0];
            float x = 40f - (frame.Width * 0.5f), y = height - 50f - (frame.Height * 0.5f);
            Sprite(batch, "powerbarbackground.spr", x, y, Blue);
            SpriteFrame fill = power.Asset.Frames[0];
            Bgra32 healthColour = Bgra32.FromRgba(Channel(0.8f - (health * 0.52f)), Channel((health * 0.22f) + 0.2f), Channel((health * 0.7f) + 0.2f), 255);
            Bar(batch, "powerbar.spr", 38f - (fill.Width * 0.5f), height - 52f - (fill.Height * 0.5f), health, healthColour, vertical: true);
            float visibilityX = x + frame.Width + 5f, visibilityY = y + 10f;
            Sprite(batch, "visibilitymeter.spr", visibilityX, visibilityY, Blue);
            Sprite(batch, "visibilityalert.spr", visibilityX + 10f, y, Blue);
            // The bar is the visibility factor (HumanPlayer_UpdateVisibilityFactor): the light, the stance and the movement, not the light alone.
            float light = Math.Clamp(human.VisibilityFactor, 0f, 1f), brightness = Math.Clamp(light - 0.5f, 0f, 0.5f) * 2f;
            Bgra32 visibilityColour = Bgra32.FromRgba(Channel((brightness + 1f) * 0.4f), Channel(0.6f - (brightness * 0.4f)), Channel(1f - (brightness * 0.8f)), 255);
            Bar(batch, "visibilitybar.spr", visibilityX + 7f, visibilityY, light, visibilityColour, vertical: true);
            if (_assets.Get(stance) is { } posture)
            {
                Sprite(batch, stance, visibilityX + 11f, height - 21f - posture.Asset.Frames[0].Height, Blue);
            }
        }
        if (title.Inventory?.Current is { } held)
        {
            // weapontype.c WeaponType_DrawIconHandler; WeaponType_GetFireTypeScreenPosition; gun.c Gun_StatusDisplayHandler.
            string mode = held.ActiveFireMode switch { 1 => held.WeaponType.FiremodeName1, 2 => held.WeaponType.FiremodeName2, _ => held.WeaponType.FiremodeName };
            if (_weaponName != held.WeaponType.WeaponName || _fireMode != mode)
            {
                _weaponName = held.WeaponType.WeaponName;
                _fireMode = mode;
                _weaponLabel = mode.Length > 0 ? string.Concat(_weaponName, " : ", mode) : _weaponName;
            }
            float right = ((Fpu)width * 0.984375).F, baseline = ((Fpu)height * 0.9583333134651184).F;
            float nameWidth = _font?.Measure(_weaponLabel).Width ?? 0f, lineHeight = _font?.LineHeight ?? 0;
            Text(batch, _weaponLabel, right - nameWidth, baseline - 2f - lineHeight, Blue);
            float iconWidth = 0f;
            if (_assets?.Get(held.WeaponType.WeaponSprite) is { } icon)
            {
                SpriteFrame picture = icon.Asset.Frames[0];
                iconWidth = picture.Width;
                Sprite(batch, held.WeaponType.WeaponSprite, right - iconWidth, baseline - 4f - lineHeight - picture.Height, Bgra32.White);
            }
            float lineWidth = MathF.Max(iconWidth, nameWidth + 3f);
            Fill(batch, new RectF(right - lineWidth, baseline, lineWidth, 1f), Bgra32.FromRgba(102, 153, 255, 128));
            OpenIGI.Game.Igi2.Weapons.Igi2FireTypeRounds rounds = title.Inventory.FireTypeRounds(held, 0);
            if (_loadedRounds != rounds.Loaded || _availableRounds != rounds.Available)
            {
                _loadedRounds = rounds.Loaded;
                _availableRounds = rounds.Available;
                _ammunition = string.Create(CultureInfo.InvariantCulture, $"{rounds.Loaded,4}/{rounds.Available,4}");
            }
            float ammoWidth = _font?.Measure(_ammunition).Width ?? 0f;
            Text(batch, _ammunition, right - 10f - ammoWidth, baseline + 4f, Blue);
        }
        if (title.Level?.LevelFlow is { InterfaceTimerEnabled: true, MaxPlayTime: > 0f } flow)
        {
            int seconds = Math.Max(0, (int)flow.MaxPlayTime - (flow.Tick / 30));
            if (_timerSeconds != seconds)
            {
                _timerSeconds = seconds;
                _timer = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}");
            }
            Text(batch, _timer, (width * 0.5f) - 30f, 24f, Blue);
        }
    }

    private void DrawInteraction(SpriteBatch batch, Igi2HumanPlayer human, float width, float height)
    {
        if (human.IsProgressBar)
        {
            // humanplayer.c HumanPlayer_StatusDisplayHandler: the timed action keeps its bar visible in movement-camera mode.
            float progress = human.TimedActionTime > 0 ? Math.Clamp((float)human.TimedActionTimer / human.TimedActionTime, 0f, 1f) : 1f;
            RectF fill = new(width * 0.315625f, height * 0.8375f, width * 0.36875f * progress, height * 0.010416667f);
            Fill(batch, fill, Bgra32.White);
            Fill(batch, new RectF(width * 0.3125f, height * 0.8333333f, width * 0.375f, height * 0.01875f),
                Bgra32.FromRgba(51, 128, 255, 128));
        }
        DrawActionMenu(batch, human, height);
    }

    /// <summary>The action menu's row: <c>ActionMenuScreen_Draw</c>.</summary>
    /// <remarks>
    /// What may be done is offered left to right from 175 across and 90 up from the foot, 64 apart, each centred on its place; the first,
    /// which the activate key takes, behind a box that pulses as the screen's counter runs from -8 to 8 a tick. Nothing is offered
    /// while the menu is hidden. A thing to work shows its badge (<c>ActionMenuScreen_DrawActionItemHandler</c> and each type's own);
    /// a swap, the weapon lying there and, ten below, the one given for it under the throw-away badge
    /// (<c>HumanPlayer_DrawActionItemHandler</c>), both as their pickup sprites.
    /// </remarks>
    private void DrawActionMenu(SpriteBatch batch, Igi2HumanPlayer human, float height)
    {
        if (human.ActionMenu.NoDrawTimeout != 0)
        {
            return;
        }

        float x = 175f, y = height - 90f;
        bool first = true;
        foreach ((object item, object? context) in human.ActionMenu.Items)
        {
            float tall;
            if (item is Igi2GunPickup pickup)
            {
                float top = ActionItem(batch, pickup.WeaponType.PickupSprite, null, x, y);
                tall = top;
                if (context is Igi2WeaponTypeTask give)
                {
                    float below = top > 0f ? top + 10f : 0f;
                    tall = below + ActionItem(batch, give.PickupSprite, ThrowAwaySprite, x, y + below);
                }
            }
            else if (item is IIgi2Activatable target && InteractionIcon(target) is { } icon)
            {
                tall = ActionItem(batch, icon, null, x, y);
            }
            else
            {
                continue;
            }

            if (first)
            {
                float pulse = Math.Abs(((human.Ticks % 17) + 17) % 17 - 8) * 0.125f;
                Fill(batch, new RectF(x - 2f - 32f, y - 2f, 68f, tall + 4f),
                    Bgra32.FromRgba(Channel(0.12f * pulse), Channel(0.28f * pulse), Channel(0.77f * pulse), Channel(pulse)));
                first = false;
            }

            x += 64f;
        }
    }

    /// <summary>The badge a swap's given weapon is drawn under: the status screen's <c>action_throwaway.spr</c>.</summary>
    public const string ThrowAwaySprite = "action_throwaway.spr";

    // ActionMenuScreen_DrawActionItem: a sprite, and the overlay under it, centred across on the place; its height, or nought with
    // nothing to draw.
    private float ActionItem(SpriteBatch batch, string name, string? overlay, float x, float y)
    {
        if (name.Length == 0 || _assets?.Get(name) is not { } sprite)
        {
            return 0f;
        }

        SpriteFrame frame = sprite.Asset.Frames[0];
        float left = x - (frame.Width / 2);
        if (overlay is not null)
        {
            Sprite(batch, overlay, left, y, Bgra32.White);
        }

        Sprite(batch, name, left, y, Bgra32.White);
        return frame.Height;
    }

    /// <summary>Resolves the retail action badge for the target the human has actually selected.</summary>
    /// <param name="target">The selected target.</param>
    /// <returns>The status archive sprite name, or null when the target has no badge.</returns>
    public static string? InteractionIcon(IIgi2Activatable target) => target switch
    {
        IIgi2Door { IsLocked: true, IsPickable: true } => "action_picklock.spr",
        Igi2GenericTbaTask { Sprite.Length: > 0 } action => action.Sprite,
        { Kind: Igi2ActivateKind.Door } => "a_door.spr",
        { Kind: Igi2ActivateKind.Cabinet } => "action_searchcab.spr",
        { Kind: Igi2ActivateKind.Terminal } => "action_comput.spr",
        { Kind: Igi2ActivateKind.Switch } => "action_pressbutton.spr",
        { Kind: Igi2ActivateKind.GenericTba } => "a_codepad.spr",
        { Kind: Igi2ActivateKind.Ladder } => "a_ladder.spr",
        { Kind: Igi2ActivateKind.Wire } => "a_wire.spr",
        { Kind: Igi2ActivateKind.Fence } => "a_fence.spr",
        { Kind: Igi2ActivateKind.StationaryGun } => "a_gun.spr",
        { Kind: Igi2ActivateKind.SilentKill } => "a_silent.spr",
        _ => null,
    };

    private void DrawMessages(SpriteBatch batch, Igi2TitlePlayer title, float width, float height)
    {
        float y = title.IsCutscenePlaying ? height - 100f : 36f;
        foreach (OpenIGI.Game.Igi2.Flow.Igi2StatusMessageSlot slot in title.StatusMessages.Slots)
        {
            if (!slot.IsUsed || slot.Printed <= 0 || slot.Message?.IsCutsceneMessage != title.IsCutscenePlaying) { continue; }
            _font?.DrawCentered(batch, slot.Text.AsSpan(0, Math.Min(slot.Printed, slot.Text.Length)), width * 0.5f * _scale, y * _scale,
                Bgra32.FromRgba(255, 255, 255, Channel(slot.Fade)), _scale, _scale);
            y += 28f;
        }
    }

    private void Sprite(SpriteBatch batch, string name, float x, float y, Bgra32 colour)
    {
        if (_assets?.Get(name) is not { } sprite) { return; }
        sprite.DrawFrame(batch, sprite.Asset.Frames[0], x * _scale, y * _scale, _scale, _scale, colour);
    }

    private void Text(SpriteBatch batch, string text, float x, float y, Bgra32 colour) => _font?.Draw(batch, text, x * _scale, y * _scale, colour, _scale, _scale);

    private void Fill(SpriteBatch batch, RectF rectangle, Bgra32 colour) => batch.FillRect(
        new RectF(rectangle.X * _scale, rectangle.Y * _scale, rectangle.Width * _scale, rectangle.Height * _scale), colour);

    private void Bar(SpriteBatch batch, string name, float x, float y, float fraction, Bgra32 colour, bool vertical)
    {
        if (fraction <= 0f || _assets?.Get(name) is not { } sprite) { return; }
        SpriteFrame frame = sprite.Asset.Frames[0];
        float h = vertical ? frame.Height * fraction : frame.Height;
        float w = vertical ? frame.Width : frame.Width * fraction;
        float top = vertical ? 1f - fraction : 0f;
        batch.Draw(sprite.Textures[frame.TextureIndex], new RectF(x * _scale, (y + (frame.Height - h)) * _scale, w * _scale, h * _scale),
            new RectF(frame.U0, frame.V0 + ((frame.V1 - frame.V0) * top), (frame.U1 - frame.U0) * (vertical ? 1f : fraction), (frame.V1 - frame.V0) * (vertical ? fraction : 1f)), colour);
    }
}
