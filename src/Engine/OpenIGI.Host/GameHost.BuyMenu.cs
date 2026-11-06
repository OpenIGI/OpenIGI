using OpenIGI.Game.Config;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// A match's buy menu: opened on its key while the buy time runs, the pointer free over it; a click on an item, or its number,
/// asks the server for it (<c>/buy</c>), and the menu shuts with the buy time, on its key again, or on Escape.
/// </content>
public sealed partial class GameHost
{
    // Whether the menu is up, and whether its key was down last frame (the menu opens on the press, not the hold).
    private bool _buyMenuUp;
    private bool _buyKeyWasDown;

    // The number keys, in the order the menu numbers its items.
    private static readonly KeyCode[] BuyNumberKeys =
        [KeyCode.D1, KeyCode.D2, KeyCode.D3, KeyCode.D4, KeyCode.D5, KeyCode.D6, KeyCode.D7, KeyCode.D8, KeyCode.D9];

    // What the armoury sells in the title played.
    private IReadOnlyList<ArmouryItem> BuyItems => Armoury.For(_titleRuntime.Title.Id);

    // Once a frame, before the game reads its input: the menu opened or shut, and an item asked for.
    private void UpdateBuyMenu()
    {
        bool keyDown = TickObserver is ISessionScoreboard && !_menuFlow.MenuMode && BuyKeyDown();
        bool pressed = keyDown && !_buyKeyWasDown;
        _buyKeyWasDown = keyDown;

        bool open = !_menuFlow.MenuMode && CurrentMatchHud() is { BuyClock: not null } && BuyItems.Count > 0;
        if (!open || (_buyMenuUp && (pressed || _window.Input.Keyboard.WasPressed(KeyCode.Escape))))
        {
            CloseBuyMenu();
            return;
        }

        if (!_buyMenuUp)
        {
            if (pressed)
            {
                _buyMenuUp = true;
                _window.CursorLocked = false;
                _window.Input.Captured = true;
            }

            return;
        }

        int? asked = null;
        for (int i = 0; i < BuyNumberKeys.Length && i < BuyItems.Count; i++)
        {
            if (_window.Input.Keyboard.WasPressed(BuyNumberKeys[i]))
            {
                asked = i;
            }
        }

        if (asked is null && _window.Input.Mouse.WasPressed(MouseButton.Left) && _remasteredHud is { } hud)
        {
            float x = _window.Input.Mouse.X;
            float y = _window.Input.Mouse.Y;
            foreach ((int index, float rx, float ry, float width, float height) in hud.BuyMenuRows)
            {
                if (x >= rx && x <= rx + width && y >= ry && y <= ry + height)
                {
                    asked = index;
                }
            }
        }

        if (asked is { } item && item < BuyItems.Count && TickObserver is ISessionTalk talk)
        {
            talk.Say($"/buy {BuyItems[item].Id}");
        }

        _window.CursorLocked = false;
        _window.Input.Captured = true;
    }

    private void CloseBuyMenu()
    {
        if (_buyMenuUp)
        {
            _buyMenuUp = false;
            _window.CursorLocked = true;
        }
    }

    // Whether the buy menu's key is held: G unless rebound, read against the shipped defaults for a profile saved before it existed.
    private bool BuyKeyDown()
    {
        PlayerProfile? profile = _configuration.Active;
        if (PlayerProfileInputBindings.BoundKeyName(profile, PlayerProfile.BuyMenuAction) is null)
        {
            profile = null;
        }

        return PlayerProfileInputBindings.IsDown(profile, _window.Input, PlayerProfile.BuyMenuAction);
    }

    // The buy menu's key's name, as the HUD shows it.
    private string BuyKeyName() =>
        PlayerProfileInputBindings.BoundKeyName(_configuration.Active, PlayerProfile.BuyMenuAction) ?? "G";
}
