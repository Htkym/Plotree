namespace Plotree.Services;

/// <summary>UI-thread render requests are coalesced and deferred until loaded and outside pointer gestures.</summary>
public sealed class DeferredGraphRenderer(Func<Action, bool> enqueue, Action render)
{
    private bool _loaded;
    private bool _suspended;
    private bool _pending;
    private bool _queued;
    private bool _rendering;
    private int _generation;

    public void SetLoaded(bool loaded)
    {
        if (_loaded == loaded) return;
        _loaded = loaded;
        if (!loaded)
        {
            _generation++;
            _queued = false;
            _pending = true;
        }
        Schedule();
    }

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        Schedule();
    }

    public void Request()
    {
        _pending = true;
        Schedule();
    }

    private void Schedule()
    {
        if (!_loaded || _suspended || !_pending || _queued || _rendering) return;
        var generation = _generation;
        _queued = true;
        if (!enqueue(() => Run(generation))) _queued = false;
    }

    private void Run(int generation)
    {
        if (generation != _generation) return;
        _queued = false;
        if (!_loaded || _suspended || !_pending) return;
        _pending = false;
        _rendering = true;
        try
        {
            render();
        }
        finally
        {
            _rendering = false;
            Schedule();
        }
    }
}
