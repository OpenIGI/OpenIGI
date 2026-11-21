using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// A match's buy menu, in the played title's look: a sheet of its glass at the left of the screen, a strip with what it is and the
/// money, then a line for each item -- its number key, its name, its price on the right.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    private readonly List<(int Index, float X, float Y, float Width, float Height)> _buyRows = [];

    /// <inheritdoc/>
    public IReadOnlyList<(int Index, float X, float Y, float Width, float Height)> BuyMenuRows => _buyRows;

    /// <inheritdoc/>
    public void DrawBuyMenu(
        SpriteBatch batch, IReadOnlyList<ArmouryItem> items, int? money, string? buyClock, (float X, float Y) pointer, int screenWidth, int screenHeight)
    {
        _buyRows.Clear();
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float body = o.Px(HudOverlay.Body);
        float row = MathF.Max(o.KeyCapHeight, o.Text.LineHeightAt(body)) + o.Px(8f);
        float header = o.Text.LineHeightAt(body) + o.Px(22f);
        float width = Math.Min(o.Px(480f), o.Safe.Width * 0.6f);
        float height = header + o.Px(8f) + (row * items.Count) + o.Px(10f);
        var sheet = new UiRect(o.Safe.X + o.Px(24f), o.Safe.Y + Math.Max(o.Px(90f), (o.Safe.Height - height) * 0.45f), width, height);
        o.Sheet(sheet);

        // A free loadout (a deathmatch's) has no money and no clock: every gun is for nothing, and comes now or at the next spawn.
        bool free = money is null;
        var strip = new UiRect(sheet.X, sheet.Y, sheet.Width, header);
        o.HeaderStrip(strip);
        float stripY = strip.Y + ((strip.Height - o.Text.LineHeightAt(body)) * 0.5f);
        float pad = o.Px(16f);
        o.Diamond(new UiVec2(sheet.X + pad + o.Px(4f), stripY + (o.Text.LineHeightAt(body) * 0.5f)), o.Px(5f), o.Ink.Accent);
        o.Text.DrawAt(free ? "LOADOUT" : "ARMOURY", sheet.X + pad + o.Px(16f), stripY, body, o.Ink.Highlight);
        string cash = free
            ? "FREE - NOW OR NEXT SPAWN"
            : string.Create(CultureInfo.InvariantCulture, $"${money:N0}{(buyClock is { } clock ? $" - {clock}" : string.Empty)}");
        o.Text.DrawRightAt(cash, sheet.Right - pad, stripY, body, o.Ink.Accent);

        float y = strip.Bottom + o.Px(8f);
        for (int i = 0; i < items.Count; i++)
        {
            ArmouryItem item = items[i];
            var rect = new UiRect(sheet.X + o.Px(8f), y, sheet.Width - o.Px(16f), row - o.Px(4f));
            bool affordable = !item.Full && (free || item.Price <= money);
            bool over = pointer.X >= rect.X && pointer.X <= rect.Right && pointer.Y >= rect.Y && pointer.Y <= rect.Bottom;
            o.Row(rect, own: false, over: over && affordable, ink: o.Ink.Accent);
            if (over && affordable)
            {
                o.Brackets(rect, o.Ink.Accent, 5f);
            }

            float textY = rect.Y + ((rect.Height - o.Text.LineHeightAt(body)) * 0.5f);
            float capX = rect.X + o.Px(8f);
            if (i < 9)
            {
                o.KeyCap((i + 1).ToString(CultureInfo.InvariantCulture), capX, rect.Y + ((rect.Height - o.KeyCapHeight) * 0.5f));
            }

            Bgra32 ink = affordable ? o.Ink.Plain : HudOverlay.Fade(o.Quiet, 0.7f);
            o.Text.DrawAt(o.Text.Capitals(item.Name), capX + o.KeyCapHeight + o.Px(12f), textY, body, ink);
            string price = item.Full ? "FULL" : free ? "FREE" : string.Create(CultureInfo.InvariantCulture, $"${item.Price:N0}");
            o.Text.DrawRightAt(price, rect.Right - o.Px(10f), textY, body, affordable ? o.Ink.Accent : o.Ink.Danger);
            _buyRows.Add((i, rect.X, rect.Y, rect.Width, rect.Height));
            y += row;
        }

        UiCursor.Draw(o.Painter, new UiVec2(pointer.X, pointer.Y));
    }
}
