using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>A match's buy menu: a panel at the left of the screen, a numbered row for each item, its price on the right.</content>
internal sealed partial class RemasteredHudRenderer
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
        if (_theme is not { } type || items.Count == 0)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        float row = Px(40f);
        float width = Math.Min(Px(460f), safe.Width * 0.6f);
        float header = Px(64f);
        var panel = new UiRect(safe.X + Px(24f), safe.Y + Math.Max(Px(90f), (safe.Height - header - (row * items.Count)) * 0.45f), width, header + (row * items.Count) + Px(12f));
        Band(painter, panel, StripFace);

        // A free loadout (a deathmatch's) has no money and no clock: every gun is for nothing, and comes now or at the next spawn.
        bool free = money is null;
        string title = free ? "LOADOUT" : "ARMOURY";
        painter.DrawLabel(type.Heading, title, new UiVec2(panel.X + Px(18f), panel.Y + Px(14f)), Px(20f), UiPalette.TextBright, 0.12f);
        string cash = free
            ? "FREE   ·   NOW OR NEXT SPAWN"
            : string.Create(CultureInfo.InvariantCulture, $"${money:N0}{(buyClock is { } clock ? $"   ·   {clock}" : string.Empty)}");
        float cashSize = Px(15f);
        painter.DrawLabel(type.Mono, cash, new UiVec2(panel.Right - Px(18f) - UiPainter.MeasureLabel(type.Mono, cash, cashSize, 0.06f), panel.Y + Px(18f)), cashSize, Victory, 0.06f);

        float y = panel.Y + header;
        for (int i = 0; i < items.Count; i++)
        {
            ArmouryItem item = items[i];
            var rect = new UiRect(panel.X + Px(8f), y, panel.Width - Px(16f), row - Px(4f));
            bool affordable = !item.Full && (free || item.Price <= money);
            bool over = pointer.X >= rect.X && pointer.X <= rect.Right && pointer.Y >= rect.Y && pointer.Y <= rect.Bottom;
            if (over && affordable)
            {
                painter.FillRect(rect, Victory.WithAlpha(60));
            }

            Bgra32 ink = affordable ? UiPalette.TextBright : UiPalette.TextMuted;
            float size = Px(16f);
            float textY = rect.Y + ((rect.Height - size) * 0.5f);
            string number = i < 9 ? (i + 1).ToString(CultureInfo.InvariantCulture) : " ";
            painter.DrawLabel(type.Mono, number, new UiVec2(rect.X + Px(10f), textY), size, affordable ? Victory : UiPalette.TextMuted, 0f);
            painter.DrawLabel(type.Heading, item.Name.ToUpperInvariant(), new UiVec2(rect.X + Px(40f), textY), size, ink, 0.08f);
            string price = item.Full ? "FULL" : free ? "FREE" : string.Create(CultureInfo.InvariantCulture, $"${item.Price:N0}");
            painter.DrawLabel(type.Mono, price, new UiVec2(rect.Right - Px(10f) - UiPainter.MeasureLabel(type.Mono, price, size, 0f), textY), size, affordable ? Victory : Enemy.WithAlpha(200), 0f);
            _buyRows.Add((i, rect.X, rect.Y, rect.Width, rect.Height));
            y += row;
        }

        UiCursor.Draw(painter, new UiVec2(pointer.X, pointer.Y));
    }
}
