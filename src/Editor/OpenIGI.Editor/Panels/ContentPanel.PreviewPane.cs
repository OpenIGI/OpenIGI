using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The Content browser's preview, docked on its own: the selected file as a picture, a model, a sound or text.</summary>
internal sealed partial class ContentPanel
{
    /// <summary>Gets the preview of the selected file, a panel of its own for the dock.</summary>
    public EditorElement PreviewPanel => _previewPane;

    // The preview's own element: the open buttons, the text, model and sound views, and the facts and picture it draws.
    private sealed class PreviewPane(EditorTheme theme, ContentPanel owner) : EditorElement(theme)
    {
        /// <inheritdoc/>
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        /// <inheritdoc/>
        protected override void ArrangeContent(LayoutContext context, UiRect contentRect) => owner.ArrangePreview(context, contentRect);

        /// <inheritdoc/>
        protected override void OnDraw(UiPainter painter)
        {
            painter.FillRect(Bounds, Theme.Panel);
            owner.DrawPreview(painter);
        }

        public void Hold(UiElement child) => AddChild(child);
    }

    // The preview laid out in its pane: the buttons that open the file along the top, its view below.
    private void ArrangePreview(LayoutContext context, UiRect preview)
    {
        float pad = Theme.Px(6f);
        float control = Theme.ControlHeight;
        UiRect inner = UiRect.FromEdges(preview.X, preview.Y + pad, preview.Right, preview.Bottom);

        // A config script opens in its form first, the script second.
        float openX = inner.X + pad;
        if (_openForm.Visibility == Visibility.Visible)
        {
            _openForm.Measure(context, new UiVec2(float.PositiveInfinity, control));
            _openForm.Arrange(new UiRect(openX, inner.Y, _openForm.DesiredSize.X, control));
            openX += _openForm.DesiredSize.X + Theme.Px(4f);
        }

        _open.Measure(context, new UiVec2(float.PositiveInfinity, control));
        _open.Arrange(new UiRect(openX, inner.Y, _open.DesiredSize.X, control));
        UiRect body = UiRect.FromEdges(inner.X, inner.Y + control + pad + Theme.RowHeight, inner.Right, inner.Bottom);
        _text.Place(context, body);
        _model.Place(context, body);
        _sound.Place(context, body);
        PreviewArea = inner;
    }
}
