using Plotree.Models;

namespace Plotree.Services;

/// <summary>
/// Maintains a bounded, serialization-based history of project changes.
/// </summary>
/// <remarks>
/// Snapshots are JSON rather than model references so a later mutation of a project cannot
/// alter an entry that has already been recorded. The serializer is deliberately used for every
/// snapshot, keeping undo/redo restoration consistent with .plotree file persistence and its
/// migrations.
/// </remarks>
public sealed class UndoRedoManager
{
    /// <summary>The default number of reversible changes retained in memory.</summary>
    public const int DefaultHistoryCapacity = 100;

    private readonly List<Change> _undoHistory = [];
    private readonly List<Change> _redoHistory = [];
    private string? _checkpointJson;

    /// <summary>Initializes a history manager with the specified change capacity.</summary>
    public UndoRedoManager(int historyCapacity = DefaultHistoryCapacity)
    {
        if (historyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(historyCapacity),
                historyCapacity,
                "History capacity must be greater than zero.");
        }

        HistoryCapacity = historyCapacity;
    }

    /// <summary>Gets the maximum number of reversible changes retained.</summary>
    public int HistoryCapacity { get; }

    /// <summary>Gets whether there is a change that can be undone.</summary>
    public bool CanUndo => _undoHistory.Count > 0;

    /// <summary>Gets whether there is a change that can be redone.</summary>
    public bool CanRedo => _redoHistory.Count > 0;

    /// <summary>
    /// Clears history and establishes <paramref name="project"/> as the saved checkpoint.
    /// </summary>
    public void Reset(PlotProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _undoHistory.Clear();
        _redoHistory.Clear();
        _checkpointJson = CreateSnapshot(project);
    }

    /// <summary>
    /// Establishes <paramref name="project"/> as the saved checkpoint without discarding history.
    /// </summary>
    public void SaveCheckpoint(PlotProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _checkpointJson = CreateSnapshot(project);
    }

    /// <summary>
    /// Determines whether <paramref name="project"/> differs from the saved checkpoint.
    /// </summary>
    /// <remarks>
    /// A manager with no checkpoint treats its project as dirty, so callers should call
    /// <see cref="Reset"/> after creating or loading a document.
    /// </remarks>
    public bool IsDirty(PlotProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return _checkpointJson is null || !StringComparer.Ordinal.Equals(_checkpointJson, CreateSnapshot(project));
    }

    /// <summary>
    /// Records a project transition. Identical serialized states do not consume history or clear redo.
    /// </summary>
    /// <returns><see langword="true"/> when a reversible change was recorded.</returns>
    public bool RecordChange(PlotProject before, PlotProject after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeJson = CreateSnapshot(before);
        var afterJson = CreateSnapshot(after);
        if (StringComparer.Ordinal.Equals(beforeJson, afterJson))
        {
            return false;
        }

        _undoHistory.Add(new Change(beforeJson, afterJson));
        if (_undoHistory.Count > HistoryCapacity)
        {
            _undoHistory.RemoveAt(0);
        }

        _redoHistory.Clear();
        return true;
    }

    /// <summary>
    /// Undoes the latest recorded change and returns an independent restored project.
    /// </summary>
    /// <returns>The restored project, or <see langword="null"/> when there is nothing to undo.</returns>
    public PlotProject? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        var index = _undoHistory.Count - 1;
        var change = _undoHistory[index];
        _undoHistory.RemoveAt(index);
        _redoHistory.Add(change);

        return Restore(change.BeforeJson);
    }

    /// <summary>
    /// Redoes the latest undone change and returns an independent restored project.
    /// </summary>
    /// <returns>The restored project, or <see langword="null"/> when there is nothing to redo.</returns>
    public PlotProject? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var index = _redoHistory.Count - 1;
        var change = _redoHistory[index];
        _redoHistory.RemoveAt(index);
        _undoHistory.Add(change);

        return Restore(change.AfterJson);
    }

    private static string CreateSnapshot(PlotProject project) => ProjectSerializer.Serialize(project);

    private static PlotProject Restore(string snapshot) => ProjectSerializer.Deserialize(snapshot);

    private sealed record Change(string BeforeJson, string AfterJson);
}
