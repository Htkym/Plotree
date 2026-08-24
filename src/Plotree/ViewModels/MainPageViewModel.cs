using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Plotree.Helpers;
using Plotree.Models;
using Plotree.Services;
using Windows.Storage;

namespace Plotree.ViewModels;

/// <summary>
/// Root view model for the document editor. It owns the persisted project, selection-only
/// state, and the history checkpoint used to derive the dirty indicator.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    private readonly ProjectFileService _fileService = new();
    private readonly RecentFilesService _recentFilesService = new();
    private readonly UndoRedoManager _undoRedo = new();

    private PendingTextEdit? _pendingTextEdit;
    private PlotProject? _geometryBefore;
    private bool _geometryCommitQueued;
    private int _mutationScopeDepth;
    private PlotProject? _mutationScopeBefore;
    private bool _isSyncingNodeOptions;
    private bool _isSyncingGroupOptions;
    private ClipboardSnapshot? _clipboard;
    private int _pasteCount;

    private const double PasteOffsetStep = 24;

    public MainPageViewModel()
    {
        Project = CreateNewProject();
        _undoRedo.Reset(Project);

        foreach (var path in _recentFilesService.RecentFiles)
        {
            RecentFiles.Add(path);
        }
    }

    [ObservableProperty]
    public partial PlotProject Project { get; set; }

    /// <summary>Full path of the current file, or null for an unsaved project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFileName), nameof(WindowTitle))]
    public partial string? CurrentFilePath { get; set; }

    /// <summary>Derived from the current project and the history save checkpoint.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>The primary node; it remains the single-node details source.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNodeSelected), nameof(IsNothingSelected))]
    public partial NodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdgeSelected), nameof(IsNothingSelected))]
    public partial EdgeViewModel? SelectedEdge { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCharacterGroupSummary))]
    public partial CharacterOptionViewModel? SelectedCharacter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTag))]
    public partial TagOptionViewModel? SelectedTag { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    public partial CharacterGroupOptionViewModel? SelectedGroup { get; set; }

    private readonly List<NodeViewModel> _selectedNodes = [];

    /// <summary>Every selected node in selection order. This is view-only state.</summary>
    public IReadOnlyList<NodeViewModel> SelectedNodes => _selectedNodes;

    /// <summary>Raised exactly once after a selection transition.</summary>
    public event EventHandler? SelectionChanged;

    public bool IsNodeSelected => SelectedNode is not null;
    public bool IsSingleNodeSelected => _selectedNodes.Count == 1;
    public bool IsMultiNodeSelected => _selectedNodes.Count > 1;
    public int SelectedNodeCount => _selectedNodes.Count;
    public string SelectionSummary => Loc.Format("Selection_NodeCount", _selectedNodes.Count);
    public bool IsEdgeSelected => SelectedEdge is not null;
    public bool IsNothingSelected => _selectedNodes.Count == 0 && SelectedEdge is null;
    public bool HasNodeSelection => _selectedNodes.Count > 0;
    public bool HasSelectedCharacter => SelectedCharacter is not null;
    public bool HasSelectedTag => SelectedTag is not null;
    public bool HasSelectedGroup => SelectedGroup is not null;

    public string SelectedNodeTitle
    {
        get => SelectedNode?.Title ?? string.Empty;
        set
        {
            if (SelectedNode is { } node)
            {
                node.Title = value;
            }
        }
    }

    public int SelectedNodeTypeIndex
    {
        get => SelectedNode?.TypeIndex ?? -1;
        set
        {
            if (SelectedNode is { } node)
            {
                node.TypeIndex = value;
            }
        }
    }

    public bool IsSelectedNodeTypeEditable => SelectedNode?.IsTypeEditable ?? false;

    public string SelectedNodeBody
    {
        get => SelectedNode?.Body ?? string.Empty;
        set
        {
            if (SelectedNode is { } node)
            {
                node.Body = value;
            }
        }
    }

    public string SelectedNodeMemo
    {
        get => SelectedNode?.Memo ?? string.Empty;
        set
        {
            if (SelectedNode is { } node)
            {
                node.Memo = value;
            }
        }
    }

    public string SelectedEdgeLabelText
    {
        get => SelectedEdge?.LabelText ?? string.Empty;
        set
        {
            if (SelectedEdge is { } edge)
            {
                edge.LabelText = value;
            }
        }
    }

    public ObservableCollection<string> RecentFiles { get; } = [];
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<EdgeViewModel> Edges { get; } = [];
    public ObservableCollection<string> TagNames { get; } = [];
    public ObservableCollection<TagOptionViewModel> Tags { get; } = [];
    public ObservableCollection<CharacterOptionViewModel> Characters { get; } = [];
    public ObservableCollection<CharacterGroupOptionViewModel> Groups { get; } = [];
    public ObservableCollection<CharacterAssignmentGroupViewModel> CharacterAssignmentGroups { get; } = [];

    /// <summary>Raised after the canvas collections have structurally changed.</summary>
    public event EventHandler? GraphChanged;

    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; } = true;

    public string CurrentFileName =>
        CurrentFilePath is null
            ? $"{Loc.Get("Default_ProjectTitle")}.plotree"
            : Path.GetFileName(CurrentFilePath);

    public string WindowTitle => $"{CurrentFileName}{(IsDirty ? " *" : string.Empty)} — Plotree";
    public int NodeCount => Project.Nodes.Count;
    public bool CanUndo => _undoRedo.CanUndo;
    public bool CanRedo => _undoRedo.CanRedo;
    public bool CanCopy => _selectedNodes.Count > 0;
    public bool CanPaste => _clipboard is not null;

    /// <summary>Localized tag list for the current node selection, including multi-selection.</summary>
    public string SelectedTagsSummary => SummarizeSelection(
        node => node.Model.TagNames,
        "Summary_Tags");

    /// <summary>Localized character and group list for the current node selection.</summary>
    public string SelectedCharactersSummary
    {
        get
        {
            var summaries = _selectedNodes
                .SelectMany(node => node.Model.CharacterIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => Project.Characters.FirstOrDefault(character => character.Id == id))
                .OfType<Character>()
                .Where(character => !string.IsNullOrWhiteSpace(character.Name))
                .Select(character => $"{character.Name} ({GetCharacterGroupSummary(character)})")
                .ToArray();

            return Loc.Format(
                "Summary_Characters",
                summaries.Length == 0 ? Loc.Get("Summary_None") : string.Join(", ", summaries));
        }
    }

    public string SelectedCharacterGroupSummary =>
        SelectedCharacter is null
            ? Loc.Format("Summary_Groups", Loc.Get("Summary_None"))
            : Loc.Format("Summary_Groups", GetCharacterGroupSummary(SelectedCharacter.Model));

    /// <summary>Proxy for the persisted project title.</summary>
    public string ProjectTitle
    {
        get => Project.Title;
        set
        {
            if (Project.Title == value)
            {
                return;
            }

            var key = ProjectTitleTextEditKey;
            var before = CaptureProjectBeforeChange(key);
            Project.Title = value;
            OnPropertyChanged();
            CompleteProjectMutation(before, key);
        }
    }

    // ----- History and mutation boundaries -----

    private const string ProjectTitleTextEditKey = "project:title";

    /// <summary>Starts coalescing keystrokes in the document-title field.</summary>
    public void BeginProjectTitleTextEdit() => BeginTextEdit(ProjectTitleTextEditKey);

    /// <summary>Starts coalescing keystrokes in a selected node text field.</summary>
    public void BeginSelectedNodeTextEdit(string propertyName)
    {
        if (SelectedNode is { } node)
        {
            BeginTextEdit(GetNodeTextEditKey(node, propertyName));
        }
    }

    /// <summary>Starts coalescing keystrokes in the selected edge label field.</summary>
    public void BeginSelectedEdgeTextEdit()
    {
        if (SelectedEdge is { } edge)
        {
            BeginTextEdit(GetEdgeTextEditKey(edge));
        }
    }

    /// <summary>Completes the current text coalescing session when its text box loses focus.</summary>
    public void CompleteTextEdit() => CommitPendingTextEdit();

    private void BeginTextEdit(string key)
    {
        if (_pendingTextEdit?.Key == key)
        {
            return;
        }

        CommitPendingTextEdit();
        CommitGeometryEdit();
        _pendingTextEdit = new PendingTextEdit(key, CloneProject(Project));
    }

    private void CommitPendingTextEdit()
    {
        if (_pendingTextEdit is not { } pending)
        {
            return;
        }

        _pendingTextEdit = null;
        _undoRedo.RecordChange(pending.Before, Project);
        RefreshHistoryState();
    }

    /// <summary>
    /// Captures the model before a persisted change. Calls made by text setters with the active
    /// text key stay in the same history entry; every other mutation first closes that edit.
    /// </summary>
    internal PlotProject CaptureProjectBeforeChange(string? textEditKey = null)
    {
        if (_mutationScopeDepth > 0)
        {
            return _mutationScopeBefore
                ?? throw new InvalidOperationException("A mutation scope must have a baseline project.");
        }

        if (_pendingTextEdit?.Key != textEditKey)
        {
            CommitPendingTextEdit();
        }

        CommitGeometryEdit();
        return CloneProject(Project);
    }

    /// <summary>Completes an immediate mutation or updates dirty state for a coalesced text edit.</summary>
    internal void CompleteProjectMutation(PlotProject before, string? textEditKey = null)
    {
        if (_mutationScopeDepth > 0)
        {
            RefreshHistoryState();
            return;
        }

        if (_pendingTextEdit is { } pending && pending.Key == textEditKey)
        {
            RefreshHistoryState();
            return;
        }

        _undoRedo.RecordChange(before, Project);
        RefreshHistoryState();
    }

    /// <summary>Runs one or more persisted changes as a single undoable project transition.</summary>
    private void MutateProject(Action mutation)
    {
        if (_mutationScopeDepth > 0)
        {
            _mutationScopeDepth++;
            try
            {
                mutation();
            }
            finally
            {
                _mutationScopeDepth--;
            }

            return;
        }

        var before = CaptureProjectBeforeChange();
        _mutationScopeBefore = before;
        _mutationScopeDepth = 1;
        try
        {
            mutation();
        }
        finally
        {
            _mutationScopeDepth = 0;
            _mutationScopeBefore = null;
        }

        CompleteProjectMutation(before);
    }

    /// <summary>Begins a drag/resize transaction; all pointer updates become one undo entry.</summary>
    internal void BeginGeometryEdit()
    {
        if (_geometryBefore is not null)
        {
            return;
        }

        CommitPendingTextEdit();
        _geometryBefore = CloneProject(Project);
    }

    /// <summary>
    /// Defers the drag/resize commit by one dispatcher turn. PlotCanvas completes every member
    /// of a group drag synchronously, so this combines their position and pin updates.
    /// </summary>
    internal void CompleteGeometryEdit()
    {
        if (_geometryBefore is null || _geometryCommitQueued)
        {
            return;
        }

        _geometryCommitQueued = true;
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is not null && queue.TryEnqueue(CommitGeometryEdit))
        {
            return;
        }

        CommitGeometryEdit();
    }

    private void CommitGeometryEdit()
    {
        _geometryCommitQueued = false;
        if (_geometryBefore is not { } before)
        {
            return;
        }

        _geometryBefore = null;
        _undoRedo.RecordChange(before, Project);
        RefreshHistoryState();
    }

    private void RefreshHistoryState()
    {
        IsDirty = _undoRedo.IsDirty(Project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private static PlotProject CloneProject(PlotProject project) =>
        ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        CommitPendingTextEdit();
        CommitGeometryEdit();
        if (_undoRedo.Undo() is { } restored)
        {
            Project = restored;
            RefreshHistoryState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        CommitPendingTextEdit();
        CommitGeometryEdit();
        if (_undoRedo.Redo() is { } restored)
        {
            Project = restored;
            RefreshHistoryState();
        }
    }

    // ----- Clipboard -----

    /// <summary>
    /// Copies the selected nodes and connections between them into an in-process clipboard.
    /// The clipboard deliberately lives on the view model so it survives a project switch
    /// without involving the system clipboard or changing project history.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        var selectedIds = _selectedNodes
            .Select(node => node.Model.Id)
            .ToHashSet(StringComparer.Ordinal);

        var nodes = Project.Nodes
            .Where(node => selectedIds.Contains(node.Id))
            .Select(CloneNode)
            .ToList();
        var edges = Project.Edges
            .Where(edge => selectedIds.Contains(edge.FromId) && selectedIds.Contains(edge.ToId))
            .Select(CloneEdge)
            .ToList();

        var tagNames = nodes
            .SelectMany(node => node.TagNames.Append(node.LegacyColorTag))
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tags = Project.Tags
            .Where(tag => tagNames.Contains(tag.Name))
            .Select(CloneColorTag)
            .ToList();

        var characterIds = nodes
            .SelectMany(node => node.CharacterIds)
            .ToHashSet(StringComparer.Ordinal);
        var characters = Project.Characters
            .Where(character => characterIds.Contains(character.Id))
            .Select(CloneCharacter)
            .ToList();
        var groupIds = characters
            .SelectMany(character => character.GroupIds)
            .ToHashSet(StringComparer.Ordinal);
        var groups = Project.Groups
            .Where(group => groupIds.Contains(group.Id))
            .Select(CloneGroup)
            .ToList();

        _clipboard = new ClipboardSnapshot(nodes, edges, tags, characters, groups);
        _pasteCount = 0;
        OnPropertyChanged(nameof(CanPaste));
        PasteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Pastes a fresh, offset copy of the clipboard as one project mutation.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboard is not { } clipboard)
        {
            return;
        }

        _pasteCount++;
        var offset = PasteOffsetStep * _pasteCount;
        var existingNodeIds = Project.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var existingEdgeIds = Project.Edges.Select(edge => edge.Id).ToHashSet(StringComparer.Ordinal);
        var nodeIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var pastedNodeIds = new List<string>(clipboard.Nodes.Count);
        var tagNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groupIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var characterIdMap = new Dictionary<string, string>(StringComparer.Ordinal);

        MutateProject(() =>
        {
            ReconcileClipboardTags(clipboard.Tags, tagNameMap);
            ReconcileClipboardGroups(clipboard.Groups, groupIdMap);
            ReconcileClipboardCharacters(clipboard.Characters, groupIdMap, characterIdMap);

            foreach (var source in clipboard.Nodes)
            {
                var pasted = CloneNode(source);
                pasted.Id = CreateUniqueId(existingNodeIds);
                pasted.X += offset;
                pasted.Y += offset;
                pasted.TagNames = pasted.TagNames
                    .Select(tagName => tagNameMap.TryGetValue(tagName, out var mappedName) ? mappedName : tagName)
                    .ToList();
                pasted.LegacyColorTag = pasted.LegacyColorTag is { } legacyTag
                    && tagNameMap.TryGetValue(legacyTag, out var mappedLegacyTag)
                    ? mappedLegacyTag
                    : pasted.LegacyColorTag;
                pasted.CharacterIds = pasted.CharacterIds
                    .Select(characterId => characterIdMap.TryGetValue(characterId, out var mappedId)
                        ? mappedId
                        : characterId)
                    .ToList();
                Project.Nodes.Add(pasted);
                nodeIdMap[source.Id] = pasted.Id;
                pastedNodeIds.Add(pasted.Id);
            }

            foreach (var source in clipboard.Edges)
            {
                if (!nodeIdMap.TryGetValue(source.FromId, out var fromId)
                    || !nodeIdMap.TryGetValue(source.ToId, out var toId))
                {
                    continue;
                }

                var pasted = CloneEdge(source);
                pasted.Id = CreateUniqueId(existingEdgeIds);
                pasted.FromId = fromId;
                pasted.ToId = toId;
                Project.Edges.Add(pasted);
            }
        });

        RebuildGraph();
        SelectNodes(
            Nodes.Where(node => pastedNodeIds.Contains(node.Model.Id)),
            isAdditive: false);
    }

    private void ReconcileClipboardTags(
        IReadOnlyList<ColorTag> sourceTags,
        Dictionary<string, string> tagNameMap)
    {
        foreach (var source in sourceTags)
        {
            var existing = Project.Tags.FirstOrDefault(tag =>
                string.Equals(tag.Name, source.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = CloneColorTag(source);
                Project.Tags.Add(existing);
            }

            tagNameMap[source.Name] = existing.Name;
        }
    }

    private void ReconcileClipboardGroups(
        IReadOnlyList<CharacterGroup> sourceGroups,
        Dictionary<string, string> groupIdMap)
    {
        var existingIds = Project.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var source in sourceGroups)
        {
            var existing = Project.Groups.FirstOrDefault(group =>
                string.Equals(group.Name, source.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = CloneGroup(source);
                existing.Id = CreateUniqueId(existingIds);
                Project.Groups.Add(existing);
            }

            groupIdMap[source.Id] = existing.Id;
        }
    }

    private void ReconcileClipboardCharacters(
        IReadOnlyList<Character> sourceCharacters,
        IReadOnlyDictionary<string, string> groupIdMap,
        Dictionary<string, string> characterIdMap)
    {
        var existingIds = Project.Characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var source in sourceCharacters)
        {
            var existing = !string.IsNullOrWhiteSpace(source.Name)
                ? Project.Characters.FirstOrDefault(character =>
                    string.Equals(character.Name, source.Name, StringComparison.OrdinalIgnoreCase))
                : null;
            if (existing is null)
            {
                existing = CloneCharacter(source);
                existing.Id = CreateUniqueId(existingIds);
                existing.GroupIds = source.GroupIds
                    .Where(groupId => groupIdMap.ContainsKey(groupId))
                    .Select(groupId => groupIdMap[groupId])
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                Project.Characters.Add(existing);
            }

            characterIdMap[source.Id] = existing.Id;
        }
    }

    private static string CreateUniqueId(HashSet<string> existingIds)
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N");
        }
        while (!existingIds.Add(id));

        return id;
    }

    private static PlotNode CloneNode(PlotNode source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Title = source.Title,
        Body = source.Body,
        Memo = source.Memo,
        TagNames = [.. source.TagNames],
        LegacyColorTag = source.LegacyColorTag,
        CharacterIds = [.. source.CharacterIds],
        X = source.X,
        Y = source.Y,
        IsPinned = source.IsPinned,
        Appearance = source.Appearance?.Clone(),
    };

    private static PlotEdge CloneEdge(PlotEdge source) => new()
    {
        Id = source.Id,
        FromId = source.FromId,
        ToId = source.ToId,
        Label = source.Label,
        FromSide = source.FromSide,
        ToSide = source.ToSide,
    };

    private static ColorTag CloneColorTag(ColorTag source) => new()
    {
        Name = source.Name,
        Color = source.Color,
    };

    private static Character CloneCharacter(Character source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Color = source.Color,
        Note = source.Note,
        GroupIds = [.. source.GroupIds],
    };

    private static CharacterGroup CloneGroup(CharacterGroup source) => new()
    {
        Id = source.Id,
        Name = source.Name,
    };

    private sealed record ClipboardSnapshot(
        IReadOnlyList<PlotNode> Nodes,
        IReadOnlyList<PlotEdge> Edges,
        IReadOnlyList<ColorTag> Tags,
        IReadOnlyList<Character> Characters,
        IReadOnlyList<CharacterGroup> Groups);

    // ----- Layout -----

    /// <summary>0 = left-to-right; 1 = top-to-bottom.</summary>
    public int LayoutDirectionIndex
    {
        get => Project.LayoutDirection == LayoutDirection.TopToBottom ? 1 : 0;
        set
        {
            var direction = value == 1 ? LayoutDirection.TopToBottom : LayoutDirection.LeftToRight;
            if (Project.LayoutDirection == direction)
            {
                return;
            }

            MutateProject(() =>
            {
                Project.LayoutDirection = direction;
                AutoLayoutService.Apply(Project);
            });
            OnPropertyChanged();
            RebuildGraph();
        }
    }

    [RelayCommand]
    private void AutoLayout()
    {
        MutateProject(() => AutoLayoutService.Apply(Project));
        RebuildGraph();
    }

    [RelayCommand]
    private void RelayoutAll()
    {
        MutateProject(() =>
        {
            foreach (var node in Project.Nodes)
            {
                node.IsPinned = false;
            }

            AutoLayoutService.Apply(Project);
        });
        RebuildGraph();
    }

    // ----- Selection (intentionally not persisted or recorded) -----

    public void SelectNode(NodeViewModel? node)
    {
        ClearEdgeSelectionCore();
        SetNodeSelectionCore(node is null ? [] : [node], node);
        NotifySelectionChanged();
    }

    public void ToggleNodeSelection(NodeViewModel node)
    {
        ClearEdgeSelectionCore();
        var next = new List<NodeViewModel>(_selectedNodes);
        NodeViewModel? primary;
        if (next.Remove(node))
        {
            primary = next.Count > 0 ? next[^1] : null;
        }
        else
        {
            next.Add(node);
            primary = node;
        }

        SetNodeSelectionCore(next, primary);
        NotifySelectionChanged();
    }

    public void AddNodeToSelection(NodeViewModel node)
    {
        ClearEdgeSelectionCore();
        var next = new List<NodeViewModel>(_selectedNodes);
        if (!next.Contains(node))
        {
            next.Add(node);
        }

        SetNodeSelectionCore(next, node);
        NotifySelectionChanged();
    }

    public void SelectNodes(IEnumerable<NodeViewModel> nodes, bool isAdditive)
    {
        ClearEdgeSelectionCore();
        var next = isAdditive ? new List<NodeViewModel>(_selectedNodes) : [];
        foreach (var node in nodes)
        {
            if (!next.Contains(node))
            {
                next.Add(node);
            }
        }

        SetNodeSelectionCore(next, next.Count > 0 ? next[^1] : null);
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void SelectAllNodes() => SelectNodes(Nodes, isAdditive: false);

    public void SelectEdge(EdgeViewModel? edge)
    {
        SetNodeSelectionCore([], null);
        if (SelectedEdge is { } previous && previous != edge)
        {
            previous.IsSelected = false;
        }

        SelectedEdge = edge;
        if (edge is not null)
        {
            edge.IsSelected = true;
        }

        NotifySelectionChanged();
    }

    public void ClearSelection()
    {
        ClearEdgeSelectionCore();
        SetNodeSelectionCore([], null);
        NotifySelectionChanged();
    }

    public bool CanUnpinSelection => _selectedNodes.Any(node => node.IsPinnedNode);

    [RelayCommand(CanExecute = nameof(CanUnpinSelection))]
    private void UnpinSelection()
    {
        MutateProject(() =>
        {
            foreach (var node in _selectedNodes)
            {
                node.UnpinWithoutRecording();
            }
        });
        OnPropertyChanged(nameof(CanUnpinSelection));
        UnpinSelectionCommand.NotifyCanExecuteChanged();
    }

    private void SetNodeSelectionCore(IReadOnlyList<NodeViewModel> nodes, NodeViewModel? primary)
    {
        foreach (var node in _selectedNodes)
        {
            if (!nodes.Contains(node))
            {
                node.IsSelected = false;
            }
        }

        _selectedNodes.Clear();
        foreach (var node in nodes)
        {
            _selectedNodes.Add(node);
            node.IsSelected = true;
        }

        SelectedNode = primary is not null && _selectedNodes.Contains(primary)
            ? primary
            : _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
    }

    private void ClearEdgeSelectionCore()
    {
        if (SelectedEdge is { } edge)
        {
            edge.IsSelected = false;
            SelectedEdge = null;
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNodes));
        OnPropertyChanged(nameof(SelectedNodeCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(IsSingleNodeSelected));
        OnPropertyChanged(nameof(IsMultiNodeSelected));
        OnPropertyChanged(nameof(IsNothingSelected));
        OnPropertyChanged(nameof(HasNodeSelection));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanUnpinSelection));
        OnPropertyChanged(nameof(SelectedTagsSummary));
        OnPropertyChanged(nameof(SelectedCharactersSummary));

        SyncNodeOptionChecks();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        UnpinSelectionCommand.NotifyCanExecuteChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifyNodeGeometryChanged(NodeViewModel node)
    {
        if (ReferenceEquals(SelectedNode, node))
        {
            NotifyNodeAppearanceChanged();
        }
    }

    // ----- Graph mutations -----

    public NodeViewModel AddNode(NodeType type, double x, double y)
    {
        PlotNode node = null!;
        MutateProject(() =>
        {
            while (Project.Nodes.Any(existing => Math.Abs(existing.X - x) < 8 && Math.Abs(existing.Y - y) < 8))
            {
                x += 24;
                y += 24;
            }

            node = new PlotNode
            {
                Type = type,
                Title = type switch
                {
                    NodeType.Choice => Loc.Get("Default_NewChoice"),
                    NodeType.Ending => Loc.Get("Default_NewEnding"),
                    _ => Loc.Get("Default_NewScene"),
                },
                X = x,
                Y = y,
            };
            Project.Nodes.Add(node);
        });

        RebuildGraph();
        var vm = Nodes.First(current => current.Model.Id == node.Id);
        SelectNode(vm);
        return vm;
    }

    public EdgeViewModel? AddEdge(NodeViewModel from, NodeViewModel to, EdgeSide fromSide, EdgeSide toSide)
    {
        if (from == to || Edges.Any(edge => edge.From == from && edge.To == to))
        {
            return null;
        }

        PlotEdge edge = null!;
        MutateProject(() =>
        {
            edge = new PlotEdge
            {
                FromId = from.Model.Id,
                ToId = to.Model.Id,
                FromSide = fromSide,
                ToSide = toSide,
            };
            Project.Edges.Add(edge);
        });

        RebuildGraph();
        var vm = Edges.First(current => current.Model.Id == edge.Id);
        SelectEdge(vm);
        return vm;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (_selectedNodes.Count > 0)
        {
            var nodes = _selectedNodes.Where(node => node.CanDelete).ToList();
            if (nodes.Count == 0 || !await ConfirmNodeDeletionAsync(nodes))
            {
                return;
            }

            var ids = nodes.Select(node => node.Model.Id).ToHashSet(StringComparer.Ordinal);
            ClearSelection();
            MutateProject(() =>
            {
                Project.Nodes.RemoveAll(node => ids.Contains(node.Id));
                Project.Edges.RemoveAll(edge => ids.Contains(edge.FromId) || ids.Contains(edge.ToId));
            });
            RebuildGraph();
        }
        else if (SelectedEdge is { } edge)
        {
            var id = edge.Model.Id;
            ClearSelection();
            MutateProject(() => Project.Edges.RemoveAll(item => item.Id == id));
            RebuildGraph();
        }
    }

    private static async Task<bool> ConfirmNodeDeletionAsync(IReadOnlyList<NodeViewModel> nodes)
    {
        if (nodes.Count == 1 && string.IsNullOrWhiteSpace(nodes[0].Model.Body))
        {
            return true;
        }

        var isSingle = nodes.Count == 1;
        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = Loc.Get(isSingle ? "Dialog_DeleteNodeTitle" : "Dialog_DeleteNodesTitle"),
            Content = isSingle
                ? Loc.Format("Dialog_DeleteNodeContent", nodes[0].DisplayTitle)
                : Loc.Format("Dialog_DeleteNodesContent", nodes.Count),
            PrimaryButtonText = Loc.Get("Dialog_Delete"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private bool CanDeleteSelected() =>
        _selectedNodes.Any(node => node.CanDelete) || SelectedEdge is not null;

    // ----- Project and node appearance -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(TypeHeaderColor),
        nameof(TypeWidth),
        nameof(TypeHeight),
        nameof(TypeDisplayModeIndex),
        nameof(HasTypeAppearance))]
    public partial int AppearanceTypeIndex { get; set; }

    private NodeType AppearanceType => AppearanceTypeIndex switch
    {
        1 => NodeType.Choice,
        2 => NodeType.Ending,
        _ => NodeType.Scene,
    };

    private NodeAppearance TypeDefaults => Project.Appearance.For(AppearanceType);

    public string? TypeHeaderColor
    {
        get => TypeDefaults.HeaderColor;
        set => ApplyTypeHeaderColor(value);
    }

    private void ApplyTypeHeaderColor(string? value)
    {
        var affectedType = AppearanceType;
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
        MutateProject(() =>
        {
            TypeDefaults.HeaderColor = normalized;
            foreach (var node in Project.Nodes.Where(node => node.Type == affectedType && node.Appearance is not null))
            {
                node.Appearance!.HeaderColor = null;
                if (node.Appearance.IsEmpty)
                {
                    node.Appearance = null;
                }
            }
        });

        NotifyTypeAppearanceChanged(affectedType);
    }

    public double TypeWidth
    {
        get => AppearanceResolver.ResolveDefaults(Project, AppearanceType).Width;
        set
        {
            if (double.IsFinite(value) && Math.Abs(value - TypeWidth) >= 0.5)
            {
                ApplyTypeAppearance(appearance => appearance.Width = value);
            }
        }
    }

    public double TypeHeight
    {
        get => AppearanceResolver.ResolveDefaults(Project, AppearanceType).Height;
        set
        {
            if (double.IsFinite(value) && Math.Abs(value - TypeHeight) >= 0.5)
            {
                ApplyTypeAppearance(appearance => appearance.Height = value);
            }
        }
    }

    public int TypeDisplayModeIndex
    {
        get => (int)AppearanceResolver.ResolveDefaults(Project, AppearanceType).DisplayMode;
        set
        {
            if (value >= 0 && value != TypeDisplayModeIndex)
            {
                ApplyTypeAppearance(appearance => appearance.DisplayMode = ToDisplayMode(value));
            }
        }
    }

    public bool HasTypeAppearance => !TypeDefaults.IsEmpty;

    [RelayCommand]
    private void ResetTypeAppearance() => ApplyTypeAppearance(appearance =>
    {
        appearance.HeaderColor = null;
        appearance.Width = null;
        appearance.Height = null;
        appearance.DisplayMode = null;
    });

    private void ApplyTypeAppearance(Action<NodeAppearance> apply)
    {
        var affectedType = AppearanceType;
        MutateProject(() => apply(TypeDefaults));
        NotifyTypeAppearanceChanged(affectedType);
    }

    private void NotifyTypeAppearanceChanged(NodeType affectedType)
    {
        OnPropertyChanged(nameof(TypeHeaderColor));
        OnPropertyChanged(nameof(TypeWidth));
        OnPropertyChanged(nameof(TypeHeight));
        OnPropertyChanged(nameof(TypeDisplayModeIndex));
        OnPropertyChanged(nameof(HasTypeAppearance));

        // Do not recreate wrappers for a type-default edit: every existing matching card receives
        // all effective-property notifications, and PlotCanvas updates its geometry from them.
        foreach (var node in Nodes.Where(node => node.Type == affectedType))
        {
            node.RefreshEffectiveAppearance();
        }

        if (SelectedNode?.Type == affectedType)
        {
            NotifyNodeAppearanceChanged();
        }
    }

    private NodeAppearance? SelectedNodeAppearance => SelectedNode?.Model.Appearance;
    public bool HasNodeColorOverride => !string.IsNullOrWhiteSpace(SelectedNodeAppearance?.HeaderColor);
    public bool HasNodeSizeOverride =>
        SelectedNodeAppearance is { } appearance && (appearance.Width is not null || appearance.Height is not null);

    public int NodeDisplayModeIndex
    {
        get => SelectedNodeAppearance?.DisplayMode is { } mode ? (int)mode + 1 : 0;
        set
        {
            if (value >= 0 && value != NodeDisplayModeIndex)
            {
                ApplyNodeAppearance(appearance => appearance.DisplayMode = value == 0 ? null : ToDisplayMode(value - 1));
            }
        }
    }

    public void SetNodeHeaderColor(string? hex) =>
        ApplyNodeAppearance(appearance => appearance.HeaderColor = string.IsNullOrWhiteSpace(hex) ? null : hex);

    [RelayCommand]
    private void ResetNodeColor() => SetNodeHeaderColor(null);

    [RelayCommand]
    private void ResetNodeSize() => ApplyNodeAppearance(appearance =>
    {
        appearance.Width = null;
        appearance.Height = null;
    });

    private void ApplyNodeAppearance(Action<NodeAppearance> apply)
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        MutateProject(() =>
        {
            var appearance = node.Model.Appearance ??= new NodeAppearance();
            apply(appearance);
            if (appearance.IsEmpty)
            {
                node.Model.Appearance = null;
            }
        });

        node.RefreshEffectiveAppearance();
        NotifyNodeAppearanceChanged();
    }

    private void NotifyNodeAppearanceChanged()
    {
        OnPropertyChanged(nameof(HasNodeColorOverride));
        OnPropertyChanged(nameof(HasNodeSizeOverride));
        OnPropertyChanged(nameof(NodeDisplayModeIndex));
    }

    private static NodeDisplayMode ToDisplayMode(int index) => index switch
    {
        1 => NodeDisplayMode.Compact,
        2 => NodeDisplayMode.TitleOnly,
        _ => NodeDisplayMode.Full,
    };

    // ----- Tags, characters, and character groups -----

    internal string? ResolveTagColor(string? tagName) =>
        tagName is null
            ? null
            : Project.Tags.FirstOrDefault(tag => string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase))?.Color;

    public void AddTag(string name, string color)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        MutateProject(() =>
        {
            var existing = Project.Tags.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Project.Tags.Add(new ColorTag { Name = name, Color = color });
            }
            else
            {
                name = existing.Name;
            }

            foreach (var node in _selectedNodes)
            {
                node.SetTagWithoutRecording(name, isPresent: true);
            }
        });

        RebuildTagOptions();
        RefreshSelectedNodeTagVisuals();
        NotifySelectionChanged();
    }

    public bool EditTag(TagOptionViewModel? option, string name, string color)
    {
        if (option is null)
        {
            return false;
        }

        name = name.Trim();
        color = color.Trim();
        if (name.Length == 0
            || ColorHex.Parse(color) is null
            || Project.Tags.Any(tag =>
                !ReferenceEquals(tag, option.Model)
                && string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var oldName = option.Model.Name;
        MutateProject(() =>
        {
            option.Model.Name = name;
            option.Model.Color = color;
            foreach (var node in Project.Nodes)
            {
                for (var index = 0; index < node.TagNames.Count; index++)
                {
                    if (string.Equals(node.TagNames[index], oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        node.TagNames[index] = name;
                    }
                }
            }
        });

        RebuildGraph();
        SelectedTag = Tags.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public void DeleteTag(TagOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        var name = option.Model.Name;
        MutateProject(() =>
        {
            Project.Tags.Remove(option.Model);
            foreach (var node in Project.Nodes)
            {
                node.TagNames.RemoveAll(tagName =>
                    string.Equals(tagName, name, StringComparison.OrdinalIgnoreCase));
            }
        });

        SelectedTag = null;
        RebuildGraph();
    }

    [RelayCommand]
    private void ClearSelectedTags()
    {
        if (_selectedNodes.Count == 0)
        {
            return;
        }

        MutateProject(() =>
        {
            foreach (var node in _selectedNodes)
            {
                node.ClearTagsWithoutRecording();
            }
        });
        RefreshSelectedNodeTagVisuals();
        NotifySelectionChanged();
    }

    internal void OnTagToggled(TagOptionViewModel option, bool? isChecked)
    {
        if (_isSyncingNodeOptions || isChecked is not { } isPresent || _selectedNodes.Count == 0)
        {
            return;
        }

        MutateProject(() =>
        {
            foreach (var node in _selectedNodes)
            {
                node.SetTagWithoutRecording(option.Name, isPresent);
            }
        });
        RefreshSelectedNodeTagVisuals();
        NotifySelectionChanged();
    }

    public void AddCharacter(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Project.Characters.Any(character => string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Character character = null!;
        MutateProject(() =>
        {
            character = new Character { Name = name };
            Project.Characters.Add(character);
        });
        RebuildCharacterOptions();
        SelectedCharacter = Characters.FirstOrDefault(option => option.Id == character.Id);
        NotifySelectionChanged();
    }

    public bool EditCharacter(CharacterOptionViewModel? option, string name)
    {
        if (option is null)
        {
            return false;
        }

        name = name.Trim();
        if (name.Length == 0
            || Project.Characters.Any(character =>
                character.Id != option.Id
                && string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        MutateProject(() => option.Model.Name = name);
        RebuildGraph();
        SelectedCharacter = Characters.FirstOrDefault(character => character.Id == option.Id);
        return true;
    }

    public void DeleteCharacter(CharacterOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        var id = option.Id;
        MutateProject(() =>
        {
            Project.Characters.Remove(option.Model);
            foreach (var node in Project.Nodes)
            {
                node.CharacterIds.RemoveAll(characterId =>
                    string.Equals(characterId, id, StringComparison.Ordinal));
            }
        });

        SelectedCharacter = null;
        RebuildGraph();
    }

    internal void OnCharacterToggled(CharacterOptionViewModel option, bool? isChecked)
    {
        if (_isSyncingNodeOptions || isChecked is not { } isPresent || _selectedNodes.Count == 0)
        {
            return;
        }

        MutateProject(() =>
        {
            foreach (var node in _selectedNodes)
            {
                node.SetCharacterWithoutRecording(option.Id, isPresent);
            }
        });
        NotifySelectionChanged();
    }

    public void AddGroup(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Project.Groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        MutateProject(() => Project.Groups.Add(new CharacterGroup { Name = name }));
        RebuildGroupOptions();
        RefreshCharacterGroupSummaries();
    }

    public bool EditGroup(CharacterGroupOptionViewModel? option, string name)
    {
        if (option is null)
        {
            return false;
        }

        name = name.Trim();
        if (name.Length == 0
            || Project.Groups.Any(group =>
                group.Id != option.Id
                && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        MutateProject(() => option.Model.Name = name);
        RebuildGraph();
        SelectedGroup = Groups.FirstOrDefault(group => group.Id == option.Id);
        return true;
    }

    public void DeleteGroup(CharacterGroupOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        var id = option.Id;
        MutateProject(() =>
        {
            Project.Groups.Remove(option.Model);
            foreach (var character in Project.Characters)
            {
                character.GroupIds.RemoveAll(groupId =>
                    string.Equals(groupId, id, StringComparison.Ordinal));
            }
        });

        SelectedGroup = null;
        RebuildGraph();
    }

    internal void OnCharacterGroupToggled(CharacterGroupOptionViewModel option, bool isChecked)
    {
        if (_isSyncingGroupOptions || SelectedCharacter is not { } character)
        {
            return;
        }

        MutateProject(() =>
        {
            if (isChecked && !character.Model.GroupIds.Contains(option.Id))
            {
                character.Model.GroupIds.Add(option.Id);
            }
            else if (!isChecked)
            {
                character.Model.GroupIds.Remove(option.Id);
            }
        });

        SyncGroupChecks();
        RefreshCharacterGroupSummaries();
    }

    internal string GetCharacterGroupSummary(Character character)
    {
        var names = Project.Groups
            .Where(group => character.GroupIds.Contains(group.Id))
            .Select(group => group.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return names.Length == 0 ? Loc.Get("Summary_None") : string.Join(", ", names);
    }

    private string GetCharacterName(string id) =>
        Project.Characters.FirstOrDefault(character => character.Id == id)?.Name ?? string.Empty;

    private string SummarizeSelection(
        Func<NodeViewModel, IEnumerable<string>> values,
        string resourceKey)
    {
        var names = _selectedNodes
            .SelectMany(values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Loc.Format(resourceKey, names.Length == 0 ? Loc.Get("Summary_None") : string.Join(", ", names));
    }

    private void SyncNodeOptionChecks()
    {
        _isSyncingNodeOptions = true;
        foreach (var tag in Tags)
        {
            tag.RefreshSelectionMode();
            tag.IsChecked = GetSelectionState(node => node.HasTag(tag.Name));
        }

        foreach (var character in Characters)
        {
            character.RefreshSelectionMode();
            character.IsChecked = GetSelectionState(node => node.HasCharacter(character.Id));
        }
        _isSyncingNodeOptions = false;
    }

    private bool? GetSelectionState(Func<NodeViewModel, bool> predicate)
    {
        if (_selectedNodes.Count == 0)
        {
            return false;
        }

        var selectedCount = _selectedNodes.Count(predicate);
        return selectedCount == 0 ? false : selectedCount == _selectedNodes.Count ? true : null;
    }

    private void SyncGroupChecks()
    {
        _isSyncingGroupOptions = true;
        foreach (var group in Groups)
        {
            group.IsChecked = SelectedCharacter?.Model.GroupIds.Contains(group.Id) == true;
        }
        _isSyncingGroupOptions = false;
    }

    private void RebuildTagOptions()
    {
        var selectedName = SelectedTag?.Name;
        TagNames.Clear();
        Tags.Clear();
        foreach (var tag in Project.Tags)
        {
            TagNames.Add(tag.Name);
            Tags.Add(new TagOptionViewModel(tag, this));
        }
        SelectedTag = Tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? Tags.FirstOrDefault();
        SyncNodeOptionChecks();
    }

    private void RebuildCharacterOptions()
    {
        var selectedId = SelectedCharacter?.Id;
        Characters.Clear();
        foreach (var character in Project.Characters)
        {
            Characters.Add(new CharacterOptionViewModel(character, this));
        }
        RebuildCharacterAssignmentGroups();
        SelectedCharacter = Characters.FirstOrDefault(option => option.Id == selectedId) ?? Characters.FirstOrDefault();
        SyncNodeOptionChecks();
        SyncGroupChecks();
    }

    private void RebuildGroupOptions()
    {
        var selectedId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var group in Project.Groups)
        {
            Groups.Add(new CharacterGroupOptionViewModel(group, this));
        }
        SelectedGroup = Groups.FirstOrDefault(group => group.Id == selectedId) ?? Groups.FirstOrDefault();
        SyncGroupChecks();
        RebuildCharacterAssignmentGroups();
    }

    private void RebuildCharacterAssignmentGroups()
    {
        CharacterAssignmentGroups.Clear();
        var optionsById = Characters.ToDictionary(option => option.Id, StringComparer.Ordinal);
        foreach (var group in Project.Groups)
        {
            var section = new CharacterAssignmentGroupViewModel
            {
                Id = group.Id,
                Name = group.Name,
            };
            foreach (var character in Project.Characters.Where(character => character.GroupIds.Contains(group.Id)))
            {
                if (optionsById.TryGetValue(character.Id, out var option))
                {
                    section.Characters.Add(option);
                }
            }

            if (section.Characters.Count > 0)
            {
                CharacterAssignmentGroups.Add(section);
            }
        }

        var ungrouped = new CharacterAssignmentGroupViewModel
        {
            Id = "NoGroup",
            Name = Loc.Get("CharacterGroup_NoGroup"),
        };
        foreach (var character in Project.Characters.Where(character => character.GroupIds.Count == 0))
        {
            if (optionsById.TryGetValue(character.Id, out var option))
            {
                ungrouped.Characters.Add(option);
            }
        }

        if (ungrouped.Characters.Count > 0)
        {
            CharacterAssignmentGroups.Add(ungrouped);
        }
    }

    private void RefreshSelectedNodeTagVisuals()
    {
        foreach (var node in _selectedNodes)
        {
            node.RefreshTags();
        }
    }

    private void RefreshCharacterGroupSummaries()
    {
        foreach (var character in Characters)
        {
            character.RefreshGroupSummary();
        }
        RebuildCharacterAssignmentGroups();
        OnPropertyChanged(nameof(SelectedCharacterGroupSummary));
    }

    // ----- File commands and exports -----

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        CommitPendingTextEdit();
        CommitGeometryEdit();
        Project = CreateNewProject();
        CurrentFilePath = null;
        _undoRedo.Reset(Project);
        RefreshHistoryState();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var path = await _fileService.PickOpenAsync();
        if (path is not null)
        {
            await LoadFromPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            RemoveRecent(path);
            await ShowErrorAsync(Loc.Get("Error_FileNotFoundTitle"), Loc.Format("Error_FileNotFoundContent", path));
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        await LoadFromPathAsync(path);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        CommitPendingTextEdit();
        CommitGeometryEdit();
        if (CurrentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await SaveToPathAsync(CurrentFilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        CommitPendingTextEdit();
        CommitGeometryEdit();
        var path = await _fileService.PickSaveAsync(Project.Title);
        if (path is not null)
        {
            await SaveToPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync() =>
        await ExportAsync(Loc.Get("Picker_MarkdownType"), ".md", PlotExporter.ToMarkdown, "Error_ExportContent");

    [RelayCommand]
    private async Task ExportTextAsync() =>
        await ExportAsync(Loc.Get("Picker_TextType"), ".txt", PlotExporter.ToPlainText, "Error_ExportContent");

    [RelayCommand]
    private async Task ExportSvgAsync() =>
        await ExportAsync(
            Loc.Get("Picker_SvgType"),
            ".svg",
            project => SvgExportService.Export(project),
            "Error_SvgExportContent");

    public Task<string?> PickExportPathAsync(string typeDescription, string extension) =>
        _fileService.PickExportAsync(Project.Title, typeDescription, extension);

    public Task<StorageFile?> PickExportFileAsync(string typeDescription, string extension) =>
        _fileService.PickExportFileAsync(Project.Title, typeDescription, extension);

    public Task ShowExportCompletedAsync(string path) => ShowExportCompletedDialogAsync(path);

    public Task ShowExportErrorAsync(string message) =>
        ShowErrorAsync(Loc.Get("Error_ExportTitle"), message);

    private async Task ExportAsync(
        string typeDescription,
        string extension,
        Func<PlotProject, string> format,
        string errorContentResource)
    {
        var path = await PickExportPathAsync(typeDescription, extension);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, format(Project));
            await ShowExportCompletedDialogAsync(path);
        }
        catch (Exception)
        {
            await ShowExportErrorAsync(Loc.Get(errorContentResource));
        }
    }

    private static async Task ShowExportCompletedDialogAsync(string path)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = Loc.Get("Dialog_ExportDoneTitle"),
            Content = Loc.Format("Dialog_ExportDoneContent", path),
            CloseButtonText = Loc.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        CommitPendingTextEdit();
        CommitGeometryEdit();
        if (!IsDirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = Loc.Get("Dialog_UnsavedTitle"),
            Content = Loc.Format("Dialog_UnsavedContent", Project.Title),
            PrimaryButtonText = Loc.Get("Dialog_Save"),
            SecondaryButtonText = Loc.Get("Dialog_Discard"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => await SaveAndReportAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };

        async Task<bool> SaveAndReportAsync()
        {
            await SaveAsync();
            return !IsDirty;
        }
    }

    private static PlotProject CreateNewProject() => new()
    {
        Title = Loc.Get("Default_ProjectTitle"),
    };

    public async Task LoadFromPathAsync(string path)
    {
        try
        {
            var loaded = await _fileService.LoadAsync(path);
            CommitPendingTextEdit();
            CommitGeometryEdit();
            Project = loaded;
            CurrentFilePath = path;
            _undoRedo.Reset(Project);
            RefreshHistoryState();
            AddRecent(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.Get("Error_OpenTitle"), ex.Message);
        }
    }

    private async Task SaveToPathAsync(string path)
    {
        try
        {
            await _fileService.SaveAsync(Project, path);
            CurrentFilePath = path;
            _undoRedo.SaveCheckpoint(Project);
            RefreshHistoryState();
            AddRecent(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.Get("Error_SaveTitle"), ex.Message);
        }
    }

    private void AddRecent(string path)
    {
        _recentFilesService.Add(path);
        SyncRecentFiles();
    }

    private void RemoveRecent(string path)
    {
        _recentFilesService.Remove(path);
        SyncRecentFiles();
    }

    private void SyncRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var recent in _recentFilesService.RecentFiles)
        {
            RecentFiles.Add(recent);
        }
    }

    private static async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Loc.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    // ----- Collection synchronization -----

    private void RebuildGraph()
    {
        var selectedNodeIds = _selectedNodes.Select(node => node.Model.Id).ToList();
        var primaryNodeId = SelectedNode?.Model.Id;
        var selectedEdgeId = SelectedEdge?.Model.Id;

        _selectedNodes.Clear();
        SelectedNode = null;
        SelectedEdge = null;
        Nodes.Clear();
        Edges.Clear();

        var nodesById = new Dictionary<string, NodeViewModel>(StringComparer.Ordinal);
        foreach (var node in Project.Nodes)
        {
            var vm = new NodeViewModel(node, this);
            Nodes.Add(vm);
            nodesById.TryAdd(node.Id, vm);
        }

        foreach (var edge in Project.Edges)
        {
            if (nodesById.TryGetValue(edge.FromId, out var from) && nodesById.TryGetValue(edge.ToId, out var to))
            {
                Edges.Add(new EdgeViewModel(edge, from, to, this));
            }
        }

        RestoreSelection(nodesById, selectedNodeIds, primaryNodeId, selectedEdgeId);
        RebuildTagOptions();
        RebuildCharacterOptions();
        RebuildGroupOptions();

        OnPropertyChanged(nameof(NodeCount));
        NotifySelectionChanged();
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreSelection(
        Dictionary<string, NodeViewModel> nodesById,
        List<string> selectedNodeIds,
        string? primaryNodeId,
        string? selectedEdgeId)
    {
        if (selectedNodeIds.Count > 0)
        {
            var restored = new List<NodeViewModel>(selectedNodeIds.Count);
            foreach (var id in selectedNodeIds)
            {
                if (nodesById.TryGetValue(id, out var node) && !restored.Contains(node))
                {
                    restored.Add(node);
                }
            }

            if (restored.Count > 0)
            {
                var primary = primaryNodeId is not null && nodesById.TryGetValue(primaryNodeId, out var match)
                    ? match
                    : null;
                SetNodeSelectionCore(restored, primary);
                return;
            }
        }

        if (selectedEdgeId is not null && Edges.FirstOrDefault(edge => edge.Model.Id == selectedEdgeId) is { } edge)
        {
            SelectedEdge = edge;
            edge.IsSelected = true;
        }
    }

    internal string GetNodeTextEditKey(NodeViewModel node, string propertyName) =>
        $"node:{node.Model.Id}:{propertyName}";

    internal string GetEdgeTextEditKey(EdgeViewModel edge) =>
        $"edge:{edge.Model.Id}:label";

    partial void OnProjectChanged(PlotProject value)
    {
        RebuildGraph();
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(LayoutDirectionIndex));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(TypeHeaderColor));
        OnPropertyChanged(nameof(TypeWidth));
        OnPropertyChanged(nameof(TypeHeight));
        OnPropertyChanged(nameof(TypeDisplayModeIndex));
        OnPropertyChanged(nameof(HasTypeAppearance));
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        if (_editorNode is not null)
        {
            _editorNode.PropertyChanged -= OnSelectedNodePropertyChanged;
        }

        _editorNode = value;
        if (_editorNode is not null)
        {
            _editorNode.PropertyChanged += OnSelectedNodePropertyChanged;
        }

        OnPropertyChanged(nameof(SelectedNodeTitle));
        OnPropertyChanged(nameof(SelectedNodeTypeIndex));
        OnPropertyChanged(nameof(IsSelectedNodeTypeEditable));
        OnPropertyChanged(nameof(SelectedNodeBody));
        OnPropertyChanged(nameof(SelectedNodeMemo));
        OnPropertyChanged(nameof(NodeDisplayModeIndex));
        NotifyNodeAppearanceChanged();
    }

    partial void OnSelectedEdgeChanged(EdgeViewModel? value)
    {
        if (_editorEdge is not null)
        {
            _editorEdge.PropertyChanged -= OnSelectedEdgePropertyChanged;
        }

        _editorEdge = value;
        if (_editorEdge is not null)
        {
            _editorEdge.PropertyChanged += OnSelectedEdgePropertyChanged;
        }

        OnPropertyChanged(nameof(SelectedEdgeLabelText));
    }

    private NodeViewModel? _editorNode;
    private EdgeViewModel? _editorEdge;

    private void OnSelectedNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NodeViewModel.Title):
                OnPropertyChanged(nameof(SelectedNodeTitle));
                break;
            case nameof(NodeViewModel.TypeIndex):
                OnPropertyChanged(nameof(SelectedNodeTypeIndex));
                OnPropertyChanged(nameof(IsSelectedNodeTypeEditable));
                break;
            case nameof(NodeViewModel.Body):
                OnPropertyChanged(nameof(SelectedNodeBody));
                break;
            case nameof(NodeViewModel.Memo):
                OnPropertyChanged(nameof(SelectedNodeMemo));
                break;
        }
    }

    private void OnSelectedEdgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EdgeViewModel.LabelText))
        {
            OnPropertyChanged(nameof(SelectedEdgeLabelText));
        }
    }

    partial void OnSelectedCharacterChanged(CharacterOptionViewModel? value)
    {
        SyncGroupChecks();
        OnPropertyChanged(nameof(HasSelectedCharacter));
    }

    private sealed record PendingTextEdit(string Key, PlotProject Before);
}
