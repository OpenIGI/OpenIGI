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

    // What the armoury sells in the title played: its guns, then -- where money is spent -- the ammunition of each gun this
    // player carries, as I.G.I. 2's shop lists it, marked full when the pouch is.
    private IReadOnlyList<ArmouryItem> BuyItems
    {
        get
        {
            IReadOnlyList<ArmouryItem> items = Armoury.For(_titleRuntime.Title.Id);
            if (HostedPlayer?.Weapons is not { } weapons || CurrentMatchHud() is not { Money: not null, FreeLoadout: false })
            {
                return items;
            }

            List<ArmouryItem> rows = [.. items];
            foreach (ArmouryItem gun in items)
            {
                if (gun.AmmoPrice > 0 && Armoury.AmmoRoom(weapons, gun.Weapon, gun.Rounds) is { } room)
                {
                    rows.Add(Armoury.AmmoRow(gun, room <= 0));
                }
            }

            return rows;
        }
    }

    // Once a frame, before the game reads its input: the menu opened or shut, and an item asked for.
    private void UpdateBuyMenu()
    {
        // A round's freeze time holds this player where it stands, as the server holds it, so what it predicts is what is run.
        if (HostedPlayer is { } own)
        {
            own.HeldStill = CurrentMatchHud() is { Frozen: true };
        }

        bool keyDown = TickObserver is ISessionScoreboard && !_menuFlow.MenuMode && BuyKeyDown();
        bool pressed = keyDown && !_buyKeyWasDown;
        _buyKeyWasDown = keyDown;

        bool open = !_menuFlow.MenuMode && CurrentMatchHud() is { BuyClock: not null } or { FreeLoadout: true } && BuyItems.Count > 0;
        if (!open || (_buyMenuUp && (pressed || _window.Input.Keyboard.WasPressed(KeyCode.Escape))))
        {
            CloseBuyMenu();
            return;
        }

        if (!_buyMenuUp)
        {
            if (pressed && !_teamMenuUp)
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

                    // A click on a row is the shop's: never a shot once the menu is shut with the button still down.
                    _window.Input.Mouse.TakePress(MouseButton.Left);
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

    // Whether a match overlay has the pointer out over it -- the buy menu, the map vote, the scoreboard clicked into -- so the
    // look leaves the cursor free rather than locking it back in the middle every frame.
    private bool OverlayHasPointer => _buyMenuUp || _teamMenuUp || _mapVoteUp || _scoreboardPointer;

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
