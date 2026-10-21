using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Frontend.Screens;

/// <summary>Small fluent helper for one-line child wiring.</summary>
internal static class PanelFluent
{
    public static Panel WithChild(this Panel panel, UiElement child)
    {
        panel.AddChild(child);
        return panel;
    }
}
