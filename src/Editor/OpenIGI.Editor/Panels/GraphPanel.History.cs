namespace OpenIGI.Editor.Panels;

/// <summary>
/// Back and forward through the graphs looked at, as a browser goes: every graph opened is remembered, and Alt+Left, the
/// mouse's back button or the Back button returns to the one before, Alt+Right or Forward to the one after.
/// </summary>
internal sealed partial class GraphPanel
{
    // How many graphs back are remembered.
    private const int HistoryDepth = 50;

    private readonly List<GraphPlace> _back = [];
    private readonly Stack<GraphPlace> _forward = new();
    private GraphPlace? _here;
    private bool _navigating;

    // A graph the panel shows: one of the mission's by its scope's key, or an event of an AI script.
    private readonly record struct GraphPlace(string? ScopeKey, string? AiPath, string? AiScopeKey);

    // Called as a graph is opened: what was shown before is remembered, unless this is going back or forward.
    private void Arrive(GraphPlace next)
    {
        if (!_navigating && _here is { } was && was != next)
        {
            _back.Add(was);
            if (_back.Count > HistoryDepth)
            {
                _back.RemoveAt(0);
            }

            _forward.Clear();
        }

        _here = next;
    }

    private void GoBack()
    {
        if (_back.Count == 0 || _here is not { } here)
        {
            return;
        }

        GraphPlace to = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Push(here);
        Go(to);
    }

    private void GoForward()
    {
        if (_forward.Count == 0 || _here is not { } here)
        {
            return;
        }

        _back.Add(here);
        Go(_forward.Pop());
    }

    private void Go(GraphPlace place)
    {
        _navigating = true;
        try
        {
            if (place.AiPath is { } path)
            {
                LoadAiScripts();
                if (_aiScripts.TryGetValue(path, out AiScript? script) && script.Scopes.FirstOrDefault(s => s.Key == place.AiScopeKey) is { } scope)
                {
                    OpenAi(path, scope);
                }
            }
            else if (place.ScopeKey is { } key)
            {
                CloseAi();
                _stale = true;
                Open(key);
                if (_rows.GetValueOrDefault(key) is { } row)
                {
                    _listSelection.Set(row);
                    _list.Reveal(row);
                }
            }

            _here = place;
        }
        finally
        {
            _navigating = false;
        }
    }
}
