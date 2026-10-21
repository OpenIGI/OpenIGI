using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The card atlases opened, as their dimensions.</summary>
    public List<(int Width, int Height)> CardCaptures { get; } = [];

    /// <summary>The pages drawn into, in order.</summary>
    public List<(int X, int Y, int Width, int Height)> CardPages { get; } = [];

    /// <summary>Whether a card capture is open right now.</summary>
    public bool IsCapturingCards { get; private set; }

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height)
    {
        if (IsCapturingCards)
        {
            throw new GraphicsDeviceException("A card capture is already in progress.");
        }

        CardCaptures.Add((width, height));
        IsCapturingCards = true;
        CurrentPass = ScenePass.Card;
        return true;
    }

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        if (!IsCapturingCards)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        CardPages.Add((x, y, width, height));
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        if (!IsCapturingCards)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        IsCapturingCards = false;
        CurrentPass = ScenePass.None;
    }

    /// <inheritdoc/>
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination)
    {
    }

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => new(90_001);

    /// <inheritdoc/>
    public TextureHandle CardNormal => new(90_002);

    /// <inheritdoc/>
    public TextureHandle CardMaterial => new(90_003);

    /// <inheritdoc/>
    public TextureHandle CardDepth => new(90_004);
}
