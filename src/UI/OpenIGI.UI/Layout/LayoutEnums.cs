namespace OpenIGI.UI.Layout;

/// <summary>The axis a stack or flex container lays its children out along.</summary>
public enum Orientation
{
    /// <summary>Children are placed left to right.</summary>
    Horizontal,

    /// <summary>Children are placed top to bottom.</summary>
    Vertical,
}

/// <summary>How an element is placed within the horizontal space its parent gives it.</summary>
public enum HorizontalAlignment
{
    /// <summary>Fill the whole width offered.</summary>
    Stretch,

    /// <summary>Sit against the left edge at the element's desired width.</summary>
    Left,

    /// <summary>Sit centred at the element's desired width.</summary>
    Center,

    /// <summary>Sit against the right edge at the element's desired width.</summary>
    Right,
}

/// <summary>How an element is placed within the vertical space its parent gives it.</summary>
public enum VerticalAlignment
{
    /// <summary>Fill the whole height offered.</summary>
    Stretch,

    /// <summary>Sit against the top edge at the element's desired height.</summary>
    Top,

    /// <summary>Sit centred at the element's desired height.</summary>
    Center,

    /// <summary>Sit against the bottom edge at the element's desired height.</summary>
    Bottom,
}

/// <summary>How a flex container distributes its children along the main axis.</summary>
public enum FlexJustify
{
    /// <summary>Pack children at the start.</summary>
    Start,

    /// <summary>Pack children at the centre.</summary>
    Center,

    /// <summary>Pack children at the end.</summary>
    End,

    /// <summary>Even space between children, none at the ends.</summary>
    SpaceBetween,

    /// <summary>Even space around each child, half-space at the ends.</summary>
    SpaceAround,

    /// <summary>Even space between and at the ends alike.</summary>
    SpaceEvenly,
}
