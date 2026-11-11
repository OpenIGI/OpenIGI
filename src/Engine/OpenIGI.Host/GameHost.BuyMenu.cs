using OpenIGI.Game.Config;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// A match's buy menu: opened on its key while the buy time runs, the pointer free over it; a click on an item, or its number,
/// asks the server for it (<c>/buy</c>), and the menu shuts once the buy goes through, with the buy time, on its key again, or on
/// Escape.
/// </content>
public sealed partial class GameHost
{
    // Whether the menu is up, and whether its key was down last frame (the menu opens on the press, not the hold).
    private bool _buyMenuUp;
    private bool _buyKeyWasDown;

    // A buy asked for and not yet answered: what the player had as it asked, and when. The menu shuts as what it has changes --
    // money spent, a gun or rounds taken -- and stays up for another pick when the server refused it (too dear, nothing to fill).
    private (string Had, long At)? _buyAsked;

    // The longest a buy is waited on to go through before the menu stops waiting: a refusal never changes anything.
    private const long BuyAnswerMilliseconds = 2000;

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
            if ((TitlePlayer as Game.Player.ITitleMatchPlayer)?.Weapons is not { } weapons || CurrentMatchHud() is not { Money: not null, FreeLoadout: false })
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
        if (TitlePlayer is Game.Player.ITitleMatchPlayer own)
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

        // The buy asked for last went through: the shop is put away, the gun in hand.
        if (_buyAsked is { } waiting)
        {
            if (BuyHoldings() != waiting.Had)
            {
                CloseBuyMenu();
                return;
            }

            if (_loop.TimeSource.Milliseconds - waiting.At > BuyAnswerMilliseconds)
            {
                _buyAsked = null;
            }
        }

        if (asked is { } item && item < BuyItems.Count && TickObserver is ISessionTalk talk)
        {
            talk.Say($"/buy {BuyItems[item].Id}");
            _buyAsked = (BuyHoldings(), _loop.TimeSource.Milliseconds);
        }

        _window.CursorLocked = false;
        _window.Input.Captured = true;
    }

    // Whether a match overlay has the pointer out over it -- the buy menu, the map vote, the scoreboard clicked into -- so the
    // look leaves the cursor free rather than locking it back in the middle every frame.
    private bool OverlayHasPointer => _buyMenuUp || _teamMenuUp || _mapVoteUp || _scoreboardPointer;

    // What a buy changes, as one string: the player's money, each weapon carried, and the rounds in each pouch.
    private string BuyHoldings()
    {
        System.Text.StringBuilder had = new();
        had.Append(CurrentMatchHud()?.Money ?? -1);
        if ((TitlePlayer as Game.Player.ITitleMatchPlayer)?.Weapons is { } weapons)
        {
            foreach (Game.Player.CarriedWeapon carried in weapons.Carried)
            {
                had.Append(System.Globalization.CultureInfo.InvariantCulture, $";w{carried.Weapon.WeaponId}");
            }

            foreach ((int ammo, int rounds) in weapons.Ammunition.Contents.OrderBy(static c => c.Key))
            {
                had.Append(System.Globalization.CultureInfo.InvariantCulture, $";a{ammo}={rounds}");
            }
        }

        return had.ToString();
    }

    private void CloseBuyMenu()
    {
        _buyAsked = null;
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
