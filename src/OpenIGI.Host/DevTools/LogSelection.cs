namespace OpenIGI.Desktop;

/// <summary>
/// A range of scrollback text picked out with the pointer, from one row and column to another.
/// </summary>
/// <remarks>
/// <para>
/// Held as an <b>anchor</b> and a <b>focus</b> rather than a start and an end, for the same reason a
/// text field is: the anchor is where the drag began and stays put, the focus follows the pointer, and
/// dragging upwards works without the two ever swapping roles.
/// </para>
/// <para>
/// Rows are indexed among the lines <em>currently passing the filter</em>, not into the scrollback.
/// That is what makes a selection behave as it looks — dragging across three visible lines selects
/// those three, not whatever happens to sit between them in the unfiltered buffer. It also means a
/// selection does not survive a filter change, which is why one clears it.
/// </para>
/// </remarks>
public sealed class LogSelection
{
    private int _anchorRow = -1;
    private int _anchorColumn;
    private int _focusRow = -1;
    private int _focusColumn;

    /// <summary>Gets a value indicating whether anything is selected.</summary>
    public bool IsActive => _anchorRow >= 0 && _focusRow >= 0 && !(IsSameRow && _anchorColumn == _focusColumn);

    /// <summary>Gets a value indicating whether a drag is in progress.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets the first selected row, or -1 when nothing is selected.</summary>
    public int FirstRow => !IsActive ? -1 : Math.Min(_anchorRow, _focusRow);

    /// <summary>Gets the last selected row, or -1 when nothing is selected.</summary>
    public int LastRow => !IsActive ? -1 : Math.Max(_anchorRow, _focusRow);

    private bool IsSameRow => _anchorRow == _focusRow;

    /// <summary>Starts a selection at a row and column.</summary>
    /// <param name="row">The row the pointer went down on.</param>
    /// <param name="column">The character column within that row.</param>
    public void Begin(int row, int column)
    {
        _anchorRow = row;
        _anchorColumn = Math.Max(0, column);
        _focusRow = row;
        _focusColumn = _anchorColumn;
        IsDragging = true;
    }

    /// <summary>Moves the free end of the selection.</summary>
    /// <param name="row">The row the pointer is over.</param>
    /// <param name="column">The character column within that row.</param>
    public void Extend(int row, int column)
    {
        if (_anchorRow < 0)
        {
            return;
        }

        _focusRow = row;
        _focusColumn = Math.Max(0, column);
    }

    /// <summary>Ends the drag, keeping whatever was selected.</summary>
    public void End() => IsDragging = false;

    /// <summary>Selects every row.</summary>
    /// <param name="rowCount">How many rows there are.</param>
    /// <param name="lastRowLength">How many characters the final row holds.</param>
    public void SelectAll(int rowCount, int lastRowLength)
    {
        if (rowCount <= 0)
        {
            Clear();
            return;
        }

        _anchorRow = 0;
        _anchorColumn = 0;
        _focusRow = rowCount - 1;
        _focusColumn = Math.Max(0, lastRowLength);
        IsDragging = false;
    }

    /// <summary>Drops the selection.</summary>
    public void Clear()
    {
        _anchorRow = -1;
        _focusRow = -1;
        _anchorColumn = 0;
        _focusColumn = 0;
        IsDragging = false;
    }

    /// <summary>Works out which characters of a row are selected.</summary>
    /// <param name="row">The row being drawn.</param>
    /// <param name="length">How many characters that row holds.</param>
    /// <param name="from">Receives the first selected column.</param>
    /// <param name="to">Receives one past the last selected column.</param>
    /// <returns><see langword="true"/> when any of the row is selected.</returns>
    /// <remarks>
    /// A row in the middle of a multi-row selection is selected end to end; only the first and last
    /// rows are cut at a column. The end column is clamped to the row's length, so a drag that ran off
    /// the right of a short line does not paint a selection over empty space.
    /// </remarks>
    public bool TryRangeFor(int row, int length, out int from, out int to)
    {
        from = 0;
        to = 0;

        if (!IsActive || row < FirstRow || row > LastRow)
        {
            return false;
        }

        (int startRow, int startColumn, int endRow, int endColumn) = Normalised();

        int first = row == startRow ? startColumn : 0;
        int last = row == endRow ? endColumn : length;

        from = Math.Clamp(first, 0, length);
        to = Math.Clamp(last, 0, length);
        return to > from;
    }

    /// <summary>Returns the selection in reading order, first position first.</summary>
    /// <returns>The start row and column, then the end row and column.</returns>
    public (int StartRow, int StartColumn, int EndRow, int EndColumn) Normalised()
    {
        if (_anchorRow < _focusRow || (IsSameRow && _anchorColumn <= _focusColumn))
        {
            return (_anchorRow, _anchorColumn, _focusRow, _focusColumn);
        }

        return (_focusRow, _focusColumn, _anchorRow, _anchorColumn);
    }
}
