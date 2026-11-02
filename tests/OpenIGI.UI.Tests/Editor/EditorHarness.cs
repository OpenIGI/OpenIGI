using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>Drives an editor root frame by frame with scripted input, as a user would.</summary>
internal sealed class EditorHarness
{
    private static readonly Lazy<UiFont> SharedFont = new(static () =>
        UiFont.ForMeasurement(FontAtlasBuilder.Build(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "JetBrainsMono-Regular.ttf")), 32f)));

    public EditorHarness(float width = 1280f, float height = 720f)
    {
        Width = width;
        Height = height;
        Theme = new EditorTheme(new Fonts());
        Root = new EditorRoot(Theme);
    }

    public EditorTheme Theme { get; }

    public EditorRoot Root { get; }

    public InputState Input { get; } = new();

    public float Width { get; }

    public float Height { get; }

    public void Frame(float seconds = 1f / 60f)
    {
        Root.Frame(Input, Width, Height, seconds);
        Input.BeginFrame();
        Input.Keyboard.BeginFrame();
    }

    public void MoveTo(UiVec2 point)
    {
        Input.Mouse.SetPosition(point.X, point.Y);
        Frame();
    }

    public void Press(UiVec2 point, MouseButton button = MouseButton.Left)
    {
        Input.Mouse.SetPosition(point.X, point.Y);
        Input.Mouse.SetButton(button, down: true);
        Frame();
    }

    public void Release(UiVec2 point, MouseButton button = MouseButton.Left)
    {
        Input.Mouse.SetPosition(point.X, point.Y);
        Input.Mouse.SetButton(button, down: false);
        Frame();
    }

    public void Click(UiVec2 point, MouseButton button = MouseButton.Left)
    {
        Press(point, button);
        Release(point, button);
    }

    public void Drag(UiVec2 from, UiVec2 to, int steps = 4)
    {
        Press(from);
        for (int i = 1; i <= steps; i++)
        {
            MoveTo(new UiVec2(from.X + ((to.X - from.X) * i / steps), from.Y + ((to.Y - from.Y) * i / steps)));
        }

        Release(to);
    }

    public void Type(string text)
    {
        foreach (char c in text)
        {
            Input.Keyboard.EnqueueCharacter(c);
        }

        Frame();
    }

    public void Key(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        SetModifiers(modifiers, down: true);
        Input.Keyboard.SetKey(key, down: true);
        Frame();
        Input.Keyboard.SetKey(key, down: false);
        SetModifiers(modifiers, down: false);
        Frame();
    }

    private void SetModifiers(KeyModifiers modifiers, bool down)
    {
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            Input.Keyboard.SetKey(KeyCode.LeftControl, down);
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            Input.Keyboard.SetKey(KeyCode.LeftShift, down);
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            Input.Keyboard.SetKey(KeyCode.LeftAlt, down);
        }
    }

    private sealed class Fonts : IEditorFonts
    {
        public UiFont Font(EditorFontFace face, float sizePx) => SharedFont.Value;
    }
}
