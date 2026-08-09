using CommunityToolkit.Mvvm.ComponentModel;
using Plotree.Models;
using Plotree.Services;

namespace Plotree.ViewModels;

/// <summary>Observable wrapper around a <see cref="PlotNode"/> for canvas rendering and editing.</summary>
public partial class NodeViewModel : ObservableObject
{
    private const double SizeEpsilon = 0.01;
    private readonly MainPageViewModel _owner;
    private bool _isInitializing;

    public NodeViewModel(PlotNode model, MainPageViewModel owner)
    {
        Model = model;
        _owner = owner;
        _isInitializing = true;
        X = model.X;
        Y = model.Y;
        _isInitializing = false;
        RefreshTags();
    }

    public PlotNode Model { get; }

    public NodeType Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value)
            {
                return;
            }

            var before = _owner.CaptureProjectBeforeChange();
            Model.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeName));
            OnPropertyChanged(nameof(TypeIndex));
            RefreshEffectiveAppearance();
            _owner.CompleteProjectMutation(before);
        }
    }

    public string TypeName => Loc.Get($"NodeType_{Model.Type}");
    public bool IsTypeEditable => true;
    public bool CanDelete => true;

    public int TypeIndex
    {
        get => Model.Type switch
        {
            NodeType.Scene => 0,
            NodeType.Choice => 1,
            NodeType.Ending => 2,
            _ => -1,
        };
        set
        {
            if (value >= 0)
            {
                Type = value switch
                {
                    0 => NodeType.Scene,
                    1 => NodeType.Choice,
                    2 => NodeType.Ending,
                    _ => Type,
                };
            }
        }
    }

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value)
            {
                return;
            }

            var key = _owner.GetNodeTextEditKey(this, nameof(Title));
            var before = _owner.CaptureProjectBeforeChange(key);
            Model.Title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            _owner.CompleteProjectMutation(before, key);
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Model.Title) ? Loc.Get("Default_UntitledNode") : Model.Title;

    public string Body
    {
        get => Model.Body;
        set
        {
            if (Model.Body == value)
            {
                return;
            }

            var key = _owner.GetNodeTextEditKey(this, nameof(Body));
            var before = _owner.CaptureProjectBeforeChange(key);
            Model.Body = value;
            OnPropertyChanged();
            _owner.CompleteProjectMutation(before, key);
        }
    }

    public string Memo
    {
        get => Model.Memo;
        set
        {
            if (Model.Memo == value)
            {
                return;
            }

            var key = _owner.GetNodeTextEditKey(this, nameof(Memo));
            var before = _owner.CaptureProjectBeforeChange(key);
            Model.Memo = value;
            OnPropertyChanged();
            _owner.CompleteProjectMutation(before, key);
        }
    }

    /// <summary>
    /// Compatibility view of the first tag. New editor code must use <see cref="TagNames"/>
    /// and <see cref="SetTagWithoutRecording"/> so it never discards other assigned tags.
    /// </summary>
    public string? ColorTagName => Model.TagNames.FirstOrDefault();

    public IReadOnlyList<string> TagNames => Model.TagNames;

    /// <summary>Comma-separated node tags for details and accessibility summaries.</summary>
    public string TagSummary => Model.TagNames.Count == 0
        ? Loc.Get("Summary_None")
        : string.Join(", ", Model.TagNames);

    /// <summary>Resolved colors for all assigned tags, in assignment order.</summary>
    public IReadOnlyList<string> TagColors => Model.TagNames
        .Select(_owner.ResolveTagColor)
        .OfType<string>()
        .ToArray();

    /// <summary>Compatibility view of the first resolved tag color.</summary>
    [ObservableProperty]
    public partial string? AccentColor { get; set; }

    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    // ----- Resolved appearance -----

    public ResolvedAppearance Appearance => AppearanceResolver.Resolve(_owner.Project, Model);
    public string? EffectiveHeaderColor => Appearance.HeaderColor;
    public double EffectiveWidth => Appearance.Width;
    public double EffectiveHeight => Appearance.Height;
    public NodeDisplayMode EffectiveDisplayMode => Appearance.DisplayMode;

    /// <summary>Notifies all bindings that consume the inherited effective appearance.</summary>
    public void RefreshEffectiveAppearance()
    {
        OnPropertyChanged(nameof(Appearance));
        OnPropertyChanged(nameof(EffectiveHeaderColor));
        OnPropertyChanged(nameof(EffectiveWidth));
        OnPropertyChanged(nameof(EffectiveHeight));
        OnPropertyChanged(nameof(EffectiveDisplayMode));
    }

    // ----- Per-node geometry and size override -----

    public static double MinCardWidth => NodeAppearanceFallback.MinWidth;
    public static double MaxCardWidth => NodeAppearanceFallback.MaxWidth;
    public static double MinCardHeight => NodeAppearanceFallback.MinHeight;
    public static double MaxCardHeight => NodeAppearanceFallback.MaxHeight;

    public double? OverrideWidth
    {
        get => Model.Appearance?.Width;
        set => SetOverrideSizeImmediate(value, OverrideHeight);
    }

    public double? OverrideHeight
    {
        get => Model.Appearance?.Height;
        set => SetOverrideSizeImmediate(OverrideWidth, value);
    }

    public bool HasSizeOverride =>
        Model.Appearance is { } appearance && (appearance.Width is not null || appearance.Height is not null);

    /// <summary>Applies one pointer-move resize update without creating a history entry yet.</summary>
    public void Resize(double width, double height)
    {
        _owner.BeginGeometryEdit();
        SetOverrideSizeCore(width, height);
    }

    /// <summary>Removes the size override as one discrete edit.</summary>
    public void ResetSize() => SetOverrideSizeImmediate(null, null);

    public void CompleteResize()
    {
        if (!Model.IsPinned)
        {
            Model.IsPinned = true;
            OnPropertyChanged(nameof(IsPinnedNode));
        }

        _owner.NotifyNodeGeometryChanged(this);
        _owner.CompleteGeometryEdit();
    }

    private void SetOverrideSizeImmediate(double? width, double? height)
    {
        if (IsSameSize(Model.Appearance?.Width, ClampOrNull(width, MinCardWidth, MaxCardWidth))
            && IsSameSize(Model.Appearance?.Height, ClampOrNull(height, MinCardHeight, MaxCardHeight)))
        {
            return;
        }

        var before = _owner.CaptureProjectBeforeChange();
        SetOverrideSizeCore(width, height);
        _owner.CompleteProjectMutation(before);
    }

    private void SetOverrideSizeCore(double? width, double? height)
    {
        var newWidth = ClampOrNull(width, MinCardWidth, MaxCardWidth);
        var newHeight = ClampOrNull(height, MinCardHeight, MaxCardHeight);
        if (IsSameSize(Model.Appearance?.Width, newWidth) && IsSameSize(Model.Appearance?.Height, newHeight))
        {
            return;
        }

        var appearance = Model.Appearance ??= new NodeAppearance();
        appearance.Width = newWidth;
        appearance.Height = newHeight;
        if (appearance.IsEmpty)
        {
            Model.Appearance = null;
        }

        OnPropertyChanged(nameof(OverrideWidth));
        OnPropertyChanged(nameof(OverrideHeight));
        OnPropertyChanged(nameof(HasSizeOverride));
        RefreshEffectiveAppearance();
    }

    private static double? ClampOrNull(double? value, double min, double max) =>
        value is { } number && double.IsFinite(number) ? Math.Clamp(number, min, max) : null;

    private static bool IsSameSize(double? left, double? right) =>
        left is null ? right is null : right is not null && Math.Abs(left.Value - right.Value) < SizeEpsilon;

    // ----- Tags and characters -----

    public bool HasTag(string tagName) =>
        Model.TagNames.Contains(tagName, StringComparer.OrdinalIgnoreCase);

    internal void SetTagWithoutRecording(string tagName, bool isPresent)
    {
        var existing = Model.TagNames.FirstOrDefault(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));
        if (isPresent && existing is null)
        {
            Model.TagNames.Add(tagName);
            RefreshTags();
        }
        else if (!isPresent && existing is not null)
        {
            Model.TagNames.Remove(existing);
            RefreshTags();
        }
    }

    internal void ClearTagsWithoutRecording()
    {
        if (Model.TagNames.Count == 0)
        {
            return;
        }

        Model.TagNames.Clear();
        RefreshTags();
    }

    /// <summary>Refreshes the first-tag canvas accent and every tag display binding.</summary>
    public void RefreshTags()
    {
        AccentColor = _owner.ResolveTagColor(Model.TagNames.FirstOrDefault());
        OnPropertyChanged(nameof(TagColors));
        OnPropertyChanged(nameof(ColorTagName));
        OnPropertyChanged(nameof(TagNames));
        OnPropertyChanged(nameof(TagSummary));
    }

    public bool HasCharacter(string characterId) => Model.CharacterIds.Contains(characterId);

    internal void SetCharacterWithoutRecording(string characterId, bool isPresent)
    {
        if (isPresent && !Model.CharacterIds.Contains(characterId))
        {
            Model.CharacterIds.Add(characterId);
        }
        else if (!isPresent)
        {
            Model.CharacterIds.Remove(characterId);
        }
    }

    public bool IsPinnedNode => Model.IsPinned;

    public void Unpin()
    {
        if (!Model.IsPinned)
        {
            return;
        }

        var before = _owner.CaptureProjectBeforeChange();
        UnpinWithoutRecording();
        _owner.CompleteProjectMutation(before);
    }

    internal void UnpinWithoutRecording()
    {
        if (Model.IsPinned)
        {
            Model.IsPinned = false;
            OnPropertyChanged(nameof(IsPinnedNode));
        }
    }

    public void CompleteDrag()
    {
        if (!Model.IsPinned)
        {
            Model.IsPinned = true;
            OnPropertyChanged(nameof(IsPinnedNode));
        }

        _owner.CompleteGeometryEdit();
    }

    partial void OnXChanged(double value)
    {
        if (_isInitializing || Math.Abs(Model.X - value) < SizeEpsilon)
        {
            return;
        }

        _owner.BeginGeometryEdit();
        Model.X = value;
    }

    partial void OnYChanged(double value)
    {
        if (_isInitializing || Math.Abs(Model.Y - value) < SizeEpsilon)
        {
            return;
        }

        _owner.BeginGeometryEdit();
        Model.Y = value;
    }
}
