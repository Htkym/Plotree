using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Input;
using Plotree.Helpers;
using Plotree.Models;
using Plotree.Services;
using Plotree.ViewModels;
using System.ComponentModel;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Plotree.Views;

/// <summary>Character positions and undirected relationships for the current project.</summary>
public sealed partial class CharacterGraph : UserControl
{
    private const double NodeWidth = 120;
    private const double NodeHeight = 110;
    private readonly Dictionary<string, Grid> _nodes = [];
    private readonly Dictionary<string, (Line Line, Line HitTarget, Button Label)> _edges = [];
    private readonly Dictionary<string, List<Border>> _groups = [];
    private readonly List<Button> _groupLabels = [];
    private readonly Dictionary<string, Border> _selectionRings = [];
    private readonly Dictionary<string, (string Data, BitmapImage Image)> _avatarImages = [];
    private readonly HashSet<string> _selectedCharacterIds = new(StringComparer.Ordinal);
    private readonly CharacterNodeActivation _characterActivation = new();
    private readonly Dictionary<string, (double X, double Y)> _dragOrigins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _marqueeSelectionOrigin = new(StringComparer.Ordinal);
    private MainPageViewModel? _viewModel;
    private string? _selectedRelationshipId;
    private string? _movingCharacterId;
    private string? _connectingFromId;
    private Line? _connectionPreview;
    private Point _pointerStart;
    private double _originX;
    private double _originY;
    private double _panX;
    private double _panY;
    private bool _panning;
    private bool _marqueeSelecting;
    private bool _marqueeAdditive;
    private bool _marqueeMoved;
    private bool _characterDragStarted;
    private bool _editing;
    private bool _updatingEditor;
    private bool _fitPending = true;
    private float _hitTargetZoom = 1;
    private DeferredGraphRenderer? _renderer;

    public CharacterGraph()
    {
        InitializeComponent();
        AddRelationshipButton.Label = Loc.Get("Relationship_Add");
        ZoomInButton.Label = Loc.Get("Graph_ZoomIn");
        ZoomOutButton.Label = Loc.Get("Graph_ZoomOut");
        FitButton.Label = Loc.Get("Graph_Fit");
        DetailsHeading.Text = Loc.Get("Relationship_Details");
        RelationshipsHeading.Text = Loc.Get("Relationship_List");
        RelationshipLabel.Header = Loc.Get("Relationship_Label");
        LabelBackgroundColor.Header = Loc.Get("Relationship_LabelBackgroundColor");
        LabelForegroundColor.Header = Loc.Get("Relationship_LabelForegroundColor");
        FirstCharacter.Header = Loc.Get("Relationship_First");
        SecondCharacter.Header = Loc.Get("Relationship_Second");
        SaveRelationshipButton.Content = Loc.Get("Dialog_Save");
        DeleteRelationshipButton.Content = Loc.Get("Dialog_Delete");
        AutomationProperties.SetName(GraphScroll, Loc.Get("Relationship_Graph"));
        AutomationProperties.SetHelpText(Viewport, Loc.Get("Help_CharacterGraphGestures"));
        AutomationProperties.SetName(MarqueeVisual, Loc.Get("Automation_Marquee"));
        AutomationProperties.SetName(RelationshipList, Loc.Get("Relationship_List"));
        GroupsButton.Label = Loc.Get("Graph_Groups");
        GroupVisibilityButton.Label = Loc.Get("Graph_GroupVisibility");
        ChangeAvatarButton.Content = Loc.Get("Character_ChangeAvatar");
        RemoveAvatarButton.Content = Loc.Get("Character_RemoveAvatar");
        AutomationProperties.SetName(SelectedAvatar, Loc.Get("Character_AvatarPreview"));
        ActualThemeChanged += (_, _) => Render();
        _renderer = new DeferredGraphRenderer(action => DispatcherQueue.TryEnqueue(() => action()), RenderCore);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        InAppFlyout.Configure(GroupsButton, GroupsFlyout);
        GraphScroll.ViewChanged += OnGraphViewChanged;
        Viewport.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnBackgroundPressed), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler(OnPointerCanceled), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(OnPointerCanceled), handledEventsToo: true);
        World.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnWheel), handledEventsToo: true);
    }

    public MainPageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.CharacterGraphChanged -= OnGraphChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.CharacterGraphChanged += OnGraphChanged;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            Render();
        }
    }

    public void ClearSelection()
    {
        _selectedCharacterIds.Clear();
        _selectedRelationshipId = null;
        _viewModel?.SelectGraphGroup(null);
        if (_viewModel is not null) RefreshGroupBackgrounds();
        _editing = false;
        RefreshDetails();
        RefreshSelection();
        BuildGroupsFlyout();
    }

    public async void DeleteSelection()
    {
        if (_selectedRelationshipId is not null)
        {
            await DeleteSelectedRelationshipAsync();
        }
    }

    public void ZoomIn() => ZoomBy(1.2);

    public void ZoomOut() => ZoomBy(1 / 1.2);

    public void Fit()
    {
        if (_viewModel is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            _fitPending = true;
            return;
        }

        if (!double.IsFinite(World.Width) || !double.IsFinite(World.Height) || World.Width <= 0 || World.Height <= 0)
        {
            _fitPending = true;
            Render();
            return;
        }
        var zoom = Math.Clamp(Math.Min(Viewport.ActualWidth / World.Width,
            Viewport.ActualHeight / World.Height), 0.25, 4);
        GraphScroll.ChangeView(0, 0, (float)zoom, disableAnimation: true);
        _fitPending = false;
    }

    private void ZoomBy(double factor)
    {
        var zoom = Math.Clamp(GraphScroll.ZoomFactor * factor, 0.25, 4);
        GraphScroll.ChangeView(null, null, (float)zoom);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitPending)
        {
            Render();
        }
    }

    private void OnGraphChanged(object? sender, EventArgs e) => Render();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainPageViewModel.Project))
        {
            _fitPending = true;
            Render();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InAppFlyout.ConfigureTree(GraphToolbar);
        _renderer?.SetLoaded(true);
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _characterActivation.Reset();
        _renderer?.SetLoaded(false);
        var commitMove = _movingCharacterId is not null && _characterDragStarted;
        ResetGestureState();
        Viewport.ReleasePointerCaptures();
        if (commitMove) _viewModel?.EndCharacterMove();
        _renderer?.SetSuspended(false);
    }

    private void Render()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(Render);
            return;
        }
        _renderer?.Request();
    }

    private void RenderCore()
    {
        if (!IsLoaded) return;
        GroupsFlyout.Hide();
        if (!IsLoaded) return;
        World.Children.Clear();
        _nodes.Clear();
        _edges.Clear();
        _groups.Clear();
        _groupLabels.Clear();
        _selectionRings.Clear();
        if (_viewModel is null)
        {
            return;
        }

        var characters = _viewModel.Project.Characters;
        _selectedCharacterIds.RemoveWhere(id => characters.All(character => character.Id != id));
        if (!_viewModel.Project.Relationships.Any(relationship => relationship.Id == _selectedRelationshipId))
        {
            _selectedRelationshipId = null;
            _editing = false;
        }
        foreach (var cachedId in _avatarImages.Keys.ToArray())
        {
            var character = characters.FirstOrDefault(candidate => candidate.Id == cachedId);
            if (character is null || character.AvatarData != _avatarImages[cachedId].Data)
            {
                _avatarImages.Remove(cachedId);
            }
        }
        var centerX = characters.Count == 0 ? 0 : (characters.Min(c => c.GraphX ?? 0) + characters.Max(c => (c.GraphX ?? 0) + NodeWidth)) / 2;
        var centerY = characters.Count == 0 ? 0 : (characters.Min(c => c.GraphY ?? 0) + characters.Max(c => (c.GraphY ?? 0) + NodeHeight)) / 2;
        var zoom = Math.Max(GraphScroll.ZoomFactor, 0.25);
        var anchorX = _fitPending ? centerX : (GraphScroll.HorizontalOffset + Viewport.ActualWidth / 2) / zoom - _originX;
        var anchorY = _fitPending ? centerY : (GraphScroll.VerticalOffset + Viewport.ActualHeight / 2) / zoom - _originY;
        var extent = CanvasExtentCalculator.Calculate(
            characters.Select(c => CharacterGraphInteraction.CalculateCharacterBounds(c.GraphX ?? 0, c.GraphY ?? 0, NodeWidth, NodeHeight))
                .Concat(CharacterGraphLayout.CalculateCanvasBounds(_viewModel.Project, NodeWidth, NodeHeight)),
            Math.Max(Viewport.ActualWidth, 1), Math.Max(Viewport.ActualHeight, 1),
            Math.Max(GraphScroll.ZoomFactor, 0.25), 120,
            anchorX, anchorY, Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
        _originX = extent.OriginX;
        _originY = extent.OriginY;
        World.Width = extent.Width;
        World.Height = extent.Height;

        DrawGroupClusters();

        foreach (var relationship in _viewModel.Project.Relationships)
        {
            var line = new Line
            {
                Style = (Style)Resources["RelationshipLineStyle"],
                Tag = relationship.Id,
                IsHitTestVisible = false,
            };
            var hitTarget = new Line
            {
                Tag = relationship.Id,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            hitTarget.PointerPressed += OnLinePressed;
            Canvas.SetZIndex(line, CharacterGraphInteraction.RelationshipLayer);
            Canvas.SetZIndex(hitTarget, CharacterGraphInteraction.RelationshipHitLayer);
            var label = new Button
            {
                Content = relationship.Label,
                Tag = relationship.Id,
                MaxWidth = 150,
                Style = (Style)Resources["RelationshipLabelStyle"],
                Visibility = string.IsNullOrWhiteSpace(relationship.Label) ? Visibility.Collapsed : Visibility.Visible,
            };
            ApplyRelationshipLabelColors(label, relationship);
            label.Click += OnRelationshipLabelClick;
            AutomationProperties.SetName(label, RelationshipName(relationship));
            AutomationProperties.SetAutomationId(label, $"Relationship_{relationship.Id}");
            Canvas.SetZIndex(label, CharacterGraphInteraction.RelationshipLabelLayer);
            _edges.Add(relationship.Id, (line, hitTarget, label));
            World.Children.Add(hitTarget);
            World.Children.Add(line);
            World.Children.Add(label);
            PositionRelationship(relationship);
        }

        foreach (var character in characters)
        {
            var icon = new Button
            {
                Style = (Style)Resources["CharacterNodeStyle"],
                Tag = character.Id,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
            };
            icon.Content = new FontIcon { Glyph = "\uE77B", FontSize = 28 };
            icon.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnNodePressed), handledEventsToo: true);
            icon.PreviewKeyDown += (_, e) => _characterActivation.PrepareKeyboard(e.Key);
            icon.Click += OnNodeClick;
            AutomationProperties.SetName(icon, character.Name);
            AutomationProperties.SetAutomationId(icon, $"CharacterNode_{character.Id}");
            var name = new TextBlock
            {
                Text = character.Name,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var visual = new Grid { Width = NodeWidth, Height = NodeHeight };
            visual.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
            visual.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(icon, 0);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            visual.Children.Add(icon);
            // A separate outer container owns the ring; the 68px avatar row must not clip an 80px ring.
            var container = new Grid { Width = NodeWidth,
                Height = NodeHeight + CharacterGraphInteraction.SelectionRingInset };
            visual.VerticalAlignment = VerticalAlignment.Top;
            visual.Margin = new Thickness(0, CharacterGraphInteraction.SelectionRingInset, 0, 0);
            container.Children.Add(visual);
            var ring = new Border
            {
                Width = CharacterGraphInteraction.SelectionRingDiameter,
                Height = CharacterGraphInteraction.SelectionRingDiameter,
                CornerRadius = new CornerRadius(CharacterGraphInteraction.SelectionRingDiameter / 2),
                BorderBrush = (Brush)Resources["CharacterSelectionRingBrush"],
                BorderThickness = new Thickness(CharacterGraphInteraction.SelectionRingThickness),
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
            };
            container.Children.Add(ring);
            _selectionRings.Add(character.Id, ring);
            Canvas.SetZIndex(container, CharacterGraphInteraction.CharacterLayer);
            Grid.SetRow(name, 1);
            name.Margin = new Thickness(0, 4, 0, 0);
            visual.Children.Add(name);

            var connect = new Button
            {
                Content = new FontIcon { Glyph = "\uE710", FontSize = 12 },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Tag = character.Id,
            };
            connect.HorizontalAlignment = HorizontalAlignment.Right;
            connect.VerticalAlignment = VerticalAlignment.Center;
            connect.Margin = new Thickness(0, 0, 8, 0);
            connect.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnConnectHandlePressed), handledEventsToo: true);
            connect.Click += OnConnectHandleClick;
            AutomationProperties.SetName(connect, Loc.Get("Relationship_ConnectHandle"));
            AutomationProperties.SetAutomationId(connect, $"ConnectHandle_{character.Id}");
            visual.Children.Add(connect);
            _nodes.Add(character.Id, container);
            World.Children.Add(container);
            PositionCharacter(character);
            _ = LoadAvatarAsync(character, icon);
        }

        EmptyHint.Text = characters.Count switch
        {
            0 => Loc.Get("Relationship_EmptyCharacters"),
            1 => Loc.Get("Relationship_OneCharacter"),
            _ => string.Empty,
        };
        EmptyHint.Visibility = characters.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        AddRelationshipButton.IsEnabled = characters.Count >= 2;
        RefreshSelection();
        RefreshDetails();
        BuildGroupsFlyout();
        if (_fitPending)
        {
            Fit();
        }
        else
        {
            GraphScroll.ChangeView((anchorX + _originX) * zoom - Viewport.ActualWidth / 2,
                (anchorY + _originY) * zoom - Viewport.ActualHeight / 2, null, disableAnimation: true);
        }
    }

    private void DrawGroupClusters()
    {
        if (_viewModel is null) return;
        var project = _viewModel.Project;
        var regions = CharacterGraphLayout.Calculate(project, _viewModel.SelectedGraphGroupId, NodeWidth, NodeHeight);
        foreach (var region in regions)
        {
            var group = project.Groups.First(group => group.Id == region.GroupId);
            var groupIndex = project.Groups.IndexOf(group);
            var color = ColorHex.Parse(group.BackgroundColor)
                ?? ColorHex.Parse(DefaultGroupColors[groupIndex % DefaultGroupColors.Length])!.Value;
            var border = new Border
            {
                Tag = group.Id,
                Background = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, color.R, color.G, color.B)),
                BorderThickness = new Thickness(group.Id == _viewModel.SelectedGraphGroupId ? 4 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 12),
                Width = region.Width,
                Height = region.Height,
            };
            Canvas.SetZIndex(border, CharacterGraphInteraction.BackgroundLayer);
            border.PointerPressed += OnGroupPressed;
            Canvas.SetLeft(border, region.X + _originX);
            Canvas.SetTop(border, region.Y + _originY);
            AutomationProperties.SetName(border, Loc.Format("Graph_GroupCluster", group.Name));
            if (!_groups.TryGetValue(group.Id, out var backgrounds))
            {
                backgrounds = [];
                _groups.Add(group.Id, backgrounds);
            }
            AutomationProperties.SetAutomationId(border, $"CharacterGroupCluster_{group.Id}_{backgrounds.Count}");
            backgrounds.Add(border);
            World.Children.Add(border);
        }
        foreach (var placement in CharacterGraphLayout.CalculateLabels(regions))
        {
            var group = project.Groups.First(group => group.Id == placement.GroupId);
            var name = new Button
            {
                Tag = group.Id,
                Content = new TextBlock { Text = group.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                Style = (Style)Resources["RelationshipLabelStyle"],
                Padding = new Thickness(6, 0, 6, 0),
                Width = placement.Width, Height = placement.Height, MinWidth = CharacterGraphLayout.MinimumGroupLabelWidth, MinHeight = 0,
                BorderThickness = new Thickness(group.Id == _viewModel.SelectedGraphGroupId ? 2 : 1),
            };
            name.Click += OnGroupLabelClick;
            ToolTipService.SetToolTip(name, group.Name);
            AutomationProperties.SetName(name, Loc.Format("Graph_GroupCluster", group.Name));
            AutomationProperties.SetAutomationId(name, $"CharacterGroupName_{group.Id}_{placement.RegionIndex}");
            Canvas.SetLeft(name, placement.X + _originX);
            Canvas.SetTop(name, placement.Y + _originY);
            Canvas.SetZIndex(name, CharacterGraphInteraction.GroupNameLayer);
            _groupLabels.Add(name);
            World.Children.Add(name);
        }
    }

    private void RefreshGroupBackgrounds()
    {
        foreach (var border in _groups.Values.SelectMany(backgrounds => backgrounds)) World.Children.Remove(border);
        foreach (var name in _groupLabels) World.Children.Remove(name);
        _groups.Clear();
        _groupLabels.Clear();
        DrawGroupClusters();
    }

    private void OnGroupLabelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string groupId } name)
            SelectGroup(groupId, name.TransformToVisual(this).TransformPoint(new Point(0, name.ActualHeight)));
    }

    private void OnGroupPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: string groupId } || _viewModel is null
            || !e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed) return;
        SelectGroup(groupId, e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    private void SelectGroup(string groupId, Point position)
    {
        if (_viewModel is null || !_viewModel.SelectGraphGroup(groupId)) return;
        _selectedCharacterIds.Clear();
        _selectedRelationshipId = null;
        _editing = false;
        RefreshSelection();
        RefreshGroupBackgrounds();
        RefreshDetails();
        BuildGroupsFlyout();
        var context = new MenuFlyout { ShouldConstrainToRootBounds = true };
        AddGroupActions(context.Items, groupId);
        ShowGroupMenu(context, position);
    }

    private void ShowGroupMenu(MenuFlyout flyout, Point position)
    {
        flyout.ShowAt(this, new FlyoutShowOptions
        {
            Position = new Point(Math.Clamp(position.X, 0, Math.Max(0, ActualWidth - 240)),
                Math.Clamp(position.Y, 0, Math.Max(0, ActualHeight - 180))),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });
    }

    private void OnGroupsClick(object sender, RoutedEventArgs e)
    {
        BuildGroupsFlyout();
        InAppFlyout.ShowBelowItem(GroupsButton, GroupsFlyout);
    }

    private static readonly string[] DefaultGroupColors = ["#4F6BED", "#2E8B57", "#C77E1E", "#8E4EC6", "#C74E4E", "#3B9EDD"];

    private void ApplyRelationshipLabelColors(Button label, CharacterRelationship relationship)
    {
        if (ColorHex.Parse(relationship.LabelBackgroundColor) is { } background)
        {
            label.Background = new SolidColorBrush(background);
        }
        if (ColorHex.Parse(relationship.LabelForegroundColor) is { } foreground)
        {
            label.Foreground = new SolidColorBrush(foreground);
        }
    }

    private void PositionCharacter(Character character)
    {
        if (_nodes.TryGetValue(character.Id, out var visual))
        {
            var bounds = CharacterGraphInteraction.CalculateCharacterBounds(character.GraphX ?? 0,
                character.GraphY ?? 0, NodeWidth, NodeHeight);
            Canvas.SetLeft(visual, bounds.X + _originX);
            Canvas.SetTop(visual, bounds.Y + _originY);
        }
    }

    private void PositionRelationship(CharacterRelationship relationship)
    {
        if (_viewModel is null || !_edges.TryGetValue(relationship.Id, out var visual))
        {
            return;
        }

        var first = _viewModel.Project.Characters.First(c => c.Id == relationship.FirstCharacterId);
        var second = _viewModel.Project.Characters.First(c => c.Id == relationship.SecondCharacterId);
        var x1 = (first.GraphX ?? 0) + _originX + NodeWidth / 2;
        var y1 = (first.GraphY ?? 0) + _originY + 34;
        var x2 = (second.GraphX ?? 0) + _originX + NodeWidth / 2;
        var y2 = (second.GraphY ?? 0) + _originY + 34;
        var geometry = CharacterGraphInteraction.Calculate(x1, y1, x2, y2, GraphScroll.ZoomFactor);
        visual.Line.X1 = geometry.Visible.X1;
        visual.Line.Y1 = geometry.Visible.Y1;
        visual.Line.X2 = geometry.Visible.X2;
        visual.Line.Y2 = geometry.Visible.Y2;
        visual.HitTarget.X1 = geometry.HitTarget.X1;
        visual.HitTarget.Y1 = geometry.HitTarget.Y1;
        visual.HitTarget.X2 = geometry.HitTarget.X2;
        visual.HitTarget.Y2 = geometry.HitTarget.Y2;
        visual.HitTarget.StrokeThickness = geometry.HitThickness;
        visual.HitTarget.IsHitTestVisible = geometry.CanHit;
        Canvas.SetLeft(visual.Label, (x1 + x2) / 2 - 35);
        Canvas.SetTop(visual.Label, (y1 + y2) / 2 - 16);
    }

    private void OnGraphViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_viewModel is null || Math.Abs(GraphScroll.ZoomFactor - _hitTargetZoom) < 0.001) return;
        _hitTargetZoom = GraphScroll.ZoomFactor;
        foreach (var relationship in _viewModel.Project.Relationships) PositionRelationship(relationship);
    }

    private void OnNodeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { Tag: string id }
            || !_characterActivation.ShouldSelectFromClick(id)) return;
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        SelectCharacter(id, control, shift);
    }

    private void SelectCharacter(string id, bool control, bool shift, bool preserveExistingSelection = false)
    {
        _viewModel!.SelectGraphGroup(null);
        RefreshGroupBackgrounds();
        CharacterGraphInteraction.SelectCharacter(_selectedCharacterIds, id, control, shift, preserveExistingSelection);
        _selectedRelationshipId = null;
        _editing = false;
        RefreshDetails();
        RefreshSelection();
        BuildGroupsFlyout();
        if (control || shift) SaveSelectedPairIfNew();
    }

    private void OnNodePressed(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { Tag: string id }
            || !e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _characterActivation.BeginPointer(id);
        ((Button)sender).Focus(FocusState.Pointer);
        var control = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
        SelectCharacter(id, control, shift, preserveExistingSelection: true);
        if (control || shift)
        {
            e.Handled = true;
            return;
        }

        _renderer?.SetSuspended(true);
        _movingCharacterId = id;
        _pointerStart = e.GetCurrentPoint(Viewport).Position;
        _dragOrigins.Clear();
        foreach (var character in _viewModel.Project.Characters.Where(character => _selectedCharacterIds.Contains(character.Id)))
        {
            _dragOrigins[character.Id] = (character.GraphX ?? 0, character.GraphY ?? 0);
        }
        _characterDragStarted = false;
        Viewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnConnectHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { Tag: string id }
            || !e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _viewModel.SelectGraphGroup(null);
        RefreshGroupBackgrounds();
        _renderer?.SetSuspended(true);
        _connectingFromId = id;
        _pointerStart = e.GetCurrentPoint(Viewport).Position;
        _connectionPreview = new Line
        {
            Style = (Style)Resources["RelationshipLineStyle"],
            IsHitTestVisible = false,
        };
        Canvas.SetZIndex(_connectionPreview, CharacterGraphInteraction.RelationshipLabelLayer);
        World.Children.Add(_connectionPreview);
        var character = _viewModel.Project.Characters.First(c => c.Id == id);
        _connectionPreview.X1 = _connectionPreview.X2 = (character.GraphX ?? 0) + _originX + NodeWidth / 2;
        _connectionPreview.Y1 = _connectionPreview.Y2 = (character.GraphY ?? 0) + _originY + 34;
        Viewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnBackgroundPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, World))
        {
            return;
        }

        _pointerStart = e.GetCurrentPoint(Viewport).Position;
        if (e.GetCurrentPoint(Viewport).Properties.IsMiddleButtonPressed
            || (e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed
                && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Space).HasFlag(CoreVirtualKeyStates.Down)))
        {
            _panning = true;
            _panX = GraphScroll.HorizontalOffset;
            _panY = GraphScroll.VerticalOffset;
        }
        else if (e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
        {
            _viewModel?.SelectGraphGroup(null);
            if (_viewModel is not null) RefreshGroupBackgrounds();
            _marqueeSelecting = true;
            _marqueeAdditive = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)
                || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
            _marqueeMoved = false;
            _marqueeSelectionOrigin.Clear();
            if (_marqueeAdditive)
            {
                _marqueeSelectionOrigin.UnionWith(_selectedCharacterIds);
            }
            UpdateMarqueeVisual(_pointerStart, _pointerStart);
        }
        else
        {
            return;
        }

        _renderer?.SetSuspended(true);
        Viewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Viewport).Position;
        if (_connectingFromId is not null && _connectionPreview is not null)
        {
            var worldPoint = Viewport.TransformToVisual(World).TransformPoint(point);
            _connectionPreview.X2 = worldPoint.X;
            _connectionPreview.Y2 = worldPoint.Y;
            e.Handled = true;
            return;
        }

        if (_panning)
        {
            GraphScroll.ChangeView(_panX - (point.X - _pointerStart.X),
                _panY - (point.Y - _pointerStart.Y), null, disableAnimation: true);
            e.Handled = true;
            return;
        }

        if (_marqueeSelecting)
        {
            _marqueeMoved = _marqueeMoved || Math.Abs(point.X - _pointerStart.X) + Math.Abs(point.Y - _pointerStart.Y) >= 4;
            UpdateMarqueeVisual(_pointerStart, point);
            var selection = new HashSet<string>(_marqueeSelectionOrigin, StringComparer.Ordinal);
            var marquee = RectFromPoints(_pointerStart, point);
            foreach (var (id, visual) in _nodes)
            {
                var location = visual.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
                var nodeBounds = new Rect(location.X, location.Y, visual.ActualWidth, visual.ActualHeight);
                if (marquee.X < nodeBounds.X + nodeBounds.Width
                    && marquee.X + marquee.Width > nodeBounds.X
                    && marquee.Y < nodeBounds.Y + nodeBounds.Height
                    && marquee.Y + marquee.Height > nodeBounds.Y)
                {
                    selection.Add(id);
                }
            }
            _selectedCharacterIds.Clear();
            _selectedCharacterIds.UnionWith(selection);
            _selectedRelationshipId = null;
            _editing = false;
            RefreshSelection();
            RefreshDetails();
            e.Handled = true;
            return;
        }

        if (_movingCharacterId is null || _viewModel is null)
        {
            return;
        }

        if (Math.Abs(point.X - _pointerStart.X) < 3 && Math.Abs(point.Y - _pointerStart.Y) < 3)
        {
            return;
        }

        if (!_characterDragStarted)
        {
            _viewModel.BeginCharacterMove();
            _characterDragStarted = true;
        }

        var deltaX = (point.X - _pointerStart.X) / GraphScroll.ZoomFactor;
        var deltaY = (point.Y - _pointerStart.Y) / GraphScroll.ZoomFactor;
        foreach (var (id, origin) in _dragOrigins)
        {
            _viewModel.MoveCharacter(id, origin.X + deltaX, origin.Y + deltaY);
            PositionCharacter(_viewModel.Project.Characters.First(character => character.Id == id));
            foreach (var relationship in _viewModel.Project.Relationships.Where(r => r.FirstCharacterId == id || r.SecondCharacterId == id))
            {
                PositionRelationship(relationship);
            }
        }
        RefreshGroupBackgrounds();
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e) => CompletePointerGesture(e, completed: true);

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e) => CompletePointerGesture(e, completed: false);

    private void CompletePointerGesture(PointerRoutedEventArgs e, bool completed)
    {
        // Leave the gate in place until all native Click callbacks in this release event finish.
        var completeActivation = _characterActivation.CompletePointer();
        DispatcherQueue.TryEnqueue(() => completeActivation());
        if (_movingCharacterId is null && _connectingFromId is null && !_panning && !_marqueeSelecting) return;
        var sourceId = _connectingFromId;
        var preview = _connectionPreview;
        var commitMove = _movingCharacterId is not null && _characterDragStarted;
        var clearSelection = _marqueeSelecting && !_marqueeMoved && !_marqueeAdditive;
        var completedMarquee = _marqueeSelecting && _marqueeMoved && completed;
        var completedConnection = sourceId is not null && completed;
        // Release/capture-loss, model notifications and UI removal can synchronously reenter
        // this handler. Retire the complete gesture before any of those operations.
        ResetGestureState();
        Viewport.ReleasePointerCapture(e.Pointer);
        if (preview is not null) World.Children.Remove(preview);
        if (completedConnection && sourceId is not null && _viewModel is not null
            && FindCharacterAt(e.GetCurrentPoint(Viewport).Position) is { } targetId && targetId != sourceId)
        {
            _selectedCharacterIds.Clear();
            _selectedCharacterIds.Add(sourceId);
            _selectedCharacterIds.Add(targetId);
            _selectedRelationshipId = null;
            _editing = false;
            RefreshSelection();
            RefreshDetails();
            SaveSelectedPairIfNew();
        }
        if (commitMove)
        {
            _viewModel?.EndCharacterMove();
            Render();
        }
        if (clearSelection)
        {
            _selectedCharacterIds.Clear();
            _selectedRelationshipId = null;
            _editing = false;
            RefreshSelection();
            RefreshDetails();
        }
        MarqueeVisual.Visibility = Visibility.Collapsed;
        BuildGroupsFlyout();
        if (completedMarquee) SaveSelectedPairIfNew();
        _renderer?.SetSuspended(false);
        e.Handled = true;
    }

    private void ResetGestureState()
    {
        _movingCharacterId = null;
        _characterDragStarted = false;
        _dragOrigins.Clear();
        _connectingFromId = null;
        _connectionPreview = null;
        _panning = false;
        _marqueeSelecting = false;
        _marqueeMoved = false;
        _marqueeAdditive = false;
        _marqueeSelectionOrigin.Clear();
    }

    private void SaveSelectedPairIfNew()
    {
        if (_viewModel is null || _selectedCharacterIds.Count != 2)
        {
            return;
        }

        var pair = _viewModel.Project.Characters.Where(character => _selectedCharacterIds.Contains(character.Id)).ToArray();
        if (pair.Length == 2)
        {
            _viewModel.SaveRelationship(null, pair[0].Id, pair[1].Id, string.Empty);
        }
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            ZoomBy(e.GetCurrentPoint(World).Properties.MouseWheelDelta > 0 ? 1.1 : 1 / 1.1);
            e.Handled = true;
        }
    }

    private void OnLinePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Line { Tag: string id })
        {
            SelectRelationship(id);
            e.Handled = true;
        }
    }

    private void OnRelationshipLabelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            SelectRelationship(id);
        }
    }

    private void SelectRelationship(string id)
    {
        _viewModel?.SelectGraphGroup(null);
        if (_viewModel is not null) RefreshGroupBackgrounds();
        _selectedRelationshipId = id;
        _selectedCharacterIds.Clear();
        _editing = true;
        RefreshDetails();
        RefreshSelection();
        BuildGroupsFlyout();
    }

    private void RefreshSelection()
    {
        if (_viewModel is not null)
        {
            foreach (var relationship in _viewModel.Project.Relationships)
            {
                if (_edges.TryGetValue(relationship.Id, out var edge))
                {
                    edge.Line.StrokeThickness = CharacterGraphLayout.IsRelationshipEmphasized(relationship,
                        _selectedRelationshipId, _selectedCharacterIds) ? 5 : 2;
                    edge.Label.BorderThickness = new Thickness(relationship.Id == _selectedRelationshipId ? 3 : 1);
                }
            }
        }
        foreach (var (id, ring) in _selectionRings)
            ring.Visibility = _selectedCharacterIds.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshDetails()
    {
        if (_viewModel is null)
        {
            return;
        }

        var project = _viewModel.Project;
        var selectedCharacters = project.Characters.Where(character => _selectedCharacterIds.Contains(character.Id)).ToArray();
        var character = selectedCharacters.Length == 1 ? selectedCharacters[0] : null;
        SelectedName.Text = selectedCharacters.Length switch
        {
            0 => Loc.Get("Relationship_SelectHint"),
            1 => character!.Name,
            _ => Loc.Format("Graph_SelectedCount", selectedCharacters.Length),
        };
        var selectedGroup = project.Groups.FirstOrDefault(group => group.Id == _viewModel.SelectedGraphGroupId);
        var selectedRelationship = project.Relationships.FirstOrDefault(relationship => relationship.Id == _selectedRelationshipId);
        if (selectedGroup is not null) SelectedName.Text = selectedGroup.Name;
        else if (selectedRelationship is not null) SelectedName.Text = RelationshipName(selectedRelationship);
        SelectedGroups.Text = character is null ? string.Empty
            : Loc.Format("Relationship_Groups", _viewModel.GetCharacterGroupSummary(character));
        if (selectedGroup is not null)
            SelectedGroups.Text = CharacterSummaryFormatter.DetailLines(project, project.Characters
                .Where(member => member.GroupIds.Contains(selectedGroup.Id)).Select(member => member.Id));
        AvatarActions.Visibility = character is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedAvatar.Visibility = character?.AvatarData is null ? Visibility.Collapsed : Visibility.Visible;
        RemoveAvatarButton.Visibility = character?.AvatarData is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedAvatar.Source = null;
        if (character?.AvatarData is { } avatarData)
        {
            if (_avatarImages.TryGetValue(character.Id, out var cached) && cached.Data == avatarData)
            {
                SelectedAvatar.Source = cached.Image;
            }
            else
            {
                _ = LoadSelectedAvatarAsync(character.Id, avatarData, character.AvatarContentType);
            }
        }
        var relationships = project.Relationships
            .Where(r => selectedCharacters.Length == 0
                || selectedCharacters.Any(selected => r.FirstCharacterId == selected.Id || r.SecondCharacterId == selected.Id))
            .Select(r => new RelationshipItem(r.Id, RelationshipName(r)))
            .ToArray();
        _updatingEditor = true;
        RelationshipList.ItemsSource = relationships;
        RelationshipList.SelectedItem = relationships.FirstOrDefault(r => r.Id == _selectedRelationshipId);
        _updatingEditor = false;
        NoRelationshipsHint.Text = Loc.Get("Relationship_EmptyRelationships");
        NoRelationshipsHint.Visibility = relationships.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        DeleteRelationshipButton.Visibility = _selectedRelationshipId is null ? Visibility.Collapsed : Visibility.Visible;
        EditorHeading.Text = Loc.Get(_selectedRelationshipId is null ? "Relationship_Add" : "Relationship_Edit");

        if (!_editing)
        {
            return;
        }

        var selected = project.Relationships.FirstOrDefault(r => r.Id == _selectedRelationshipId);
        _updatingEditor = true;
        FirstCharacter.ItemsSource = project.Characters;
        SecondCharacter.ItemsSource = project.Characters;
        FirstCharacter.SelectedItem = project.Characters.FirstOrDefault(c => c.Id == selected?.FirstCharacterId)
            ?? selectedCharacters.FirstOrDefault();
        SecondCharacter.SelectedItem = project.Characters.FirstOrDefault(c => c.Id == selected?.SecondCharacterId);
        RelationshipLabel.Text = selected?.Label ?? Loc.Get("Relationship_DefaultLabel");
        LabelBackgroundColor.Text = selected?.LabelBackgroundColor ?? string.Empty;
        LabelForegroundColor.Text = selected?.LabelForegroundColor ?? string.Empty;
        _updatingEditor = false;
        UpdateValidation();
    }

    private string RelationshipName(CharacterRelationship relationship)
    {
        var project = _viewModel!.Project;
        var first = project.Characters.First(c => c.Id == relationship.FirstCharacterId).Name;
        var second = project.Characters.First(c => c.Id == relationship.SecondCharacterId).Name;
        return string.IsNullOrWhiteSpace(relationship.Label)
            ? Loc.Format("Relationship_Pair", first, second)
            : Loc.Format("Relationship_Item", first, second, relationship.Label);
    }

    private void OnRelationshipSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingEditor && RelationshipList.SelectedItem is RelationshipItem item)
        {
            SelectRelationship(item.Id);
        }
    }

    private void OnAddRelationship(object sender, RoutedEventArgs e)
    {
        _viewModel?.SelectGraphGroup(null);
        if (_viewModel is not null) RefreshGroupBackgrounds();
        _selectedRelationshipId = null;
        _editing = true;
        RefreshDetails();
        if (_selectedCharacterIds.Count == 2)
        {
            FirstCharacter.SelectedItem = _viewModel!.Project.Characters.First(character => _selectedCharacterIds.Contains(character.Id));
            SecondCharacter.SelectedItem = _viewModel.Project.Characters.Last(character => _selectedCharacterIds.Contains(character.Id));
        }
        RelationshipLabel.Focus(FocusState.Programmatic);
        UpdateValidation();
    }

    private void OnConnectHandleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            BeginNewRelationship(id, null);
            BuildGroupsFlyout();
        }
    }

    private void BeginNewRelationship(string firstId, string? secondId)
    {
        _viewModel?.SelectGraphGroup(null);
        if (_viewModel is not null) RefreshGroupBackgrounds();
        _selectedCharacterIds.Clear();
        _selectedCharacterIds.Add(firstId);
        if (secondId is not null)
        {
            _selectedCharacterIds.Add(secondId);
        }
        _selectedRelationshipId = null;
        _editing = true;
        RefreshSelection();
        RefreshDetails();
        FirstCharacter.SelectedItem = _viewModel!.Project.Characters.First(character => character.Id == firstId);
        SecondCharacter.SelectedItem = secondId is null
            ? null
            : _viewModel.Project.Characters.First(character => character.Id == secondId);
        RelationshipLabel.Focus(FocusState.Programmatic);
        UpdateValidation();
    }

    private string? FindCharacterAt(Point point)
    {
        foreach (var (id, visual) in _nodes)
        {
            var topLeft = visual.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
            if (new Rect(topLeft.X, topLeft.Y, visual.ActualWidth, visual.ActualHeight).Contains(point))
            {
                return id;
            }
        }

        return null;
    }

    private void UpdateMarqueeVisual(Point start, Point end)
    {
        var rect = RectFromPoints(start, end);
        Canvas.SetLeft(MarqueeVisual, rect.X);
        Canvas.SetTop(MarqueeVisual, rect.Y);
        MarqueeVisual.Width = rect.Width;
        MarqueeVisual.Height = rect.Height;
        MarqueeVisual.Visibility = Visibility.Visible;
    }

    private static Rect RectFromPoints(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X),
        Math.Abs(first.Y - second.Y));

    private void AddGroupActions(IList<MenuFlyoutItemBase> items, string groupId)
    {
        var rename = new MenuFlyoutItem { Text = Loc.Get("Graph_RenameGroup"), Tag = groupId };
        rename.Click += OnRenameGroup;
        items.Add(rename);
        var color = new MenuFlyoutItem { Text = Loc.Get("Graph_ChangeGroupColor"), Tag = groupId };
        color.Click += OnChangeGroupColor;
        items.Add(color);
    }

    private void BuildGroupsFlyout()
    {
        GroupsFlyout.Items.Clear();
        GroupVisibilityFlyout.Items.Clear();
        var create = new MenuFlyoutItem
        {
            Text = Loc.Get("Graph_CreateGroupFromSelection"),
            IsEnabled = _selectedCharacterIds.Count > 0,
        };
        create.Click += OnCreateGroup;
        GroupsFlyout.Items.Add(create);
        if (_viewModel is null || _viewModel.Project.Groups.Count == 0) return;
        GroupsFlyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var group in _viewModel.Project.Groups)
        {
            var submenu = new MenuFlyoutSubItem { Text = group.Name };
            var assign = new MenuFlyoutItem
            {
                Text = Loc.Get("Graph_GroupSelection"), Tag = group.Id,
                IsEnabled = _selectedCharacterIds.Count > 0,
            };
            assign.Click += OnAssignSelectionToGroup;
            submenu.Items.Add(assign);
            AddGroupActions(submenu.Items, group.Id);
            GroupsFlyout.Items.Add(submenu);
            var visibility = new ToggleMenuFlyoutItem { Text = group.Name, IsChecked = group.IsVisible };
            var groupId = group.Id;
            visibility.Click += (_, _) => _viewModel.SetGraphGroupVisible(groupId, visibility.IsChecked);
            GroupVisibilityFlyout.Items.Add(visibility);
        }
        GroupsFlyout.Items.Add(new MenuFlyoutSeparator());
        var ungroup = new MenuFlyoutItem
        {
            Text = Loc.Get("Graph_UngroupSelection"),
            IsEnabled = _viewModel.Project.Characters.Any(character =>
                _selectedCharacterIds.Contains(character.Id) && character.GroupIds.Count > 0),
        };
        ungroup.Click += (_, _) => _viewModel.AssignCharactersToGraphGroup(_selectedCharacterIds, null);
        GroupsFlyout.Items.Add(ungroup);
    }

    private async void OnCreateGroup(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _selectedCharacterIds.Count == 0)
        {
            return;
        }

        var name = new TextBox { Header = Loc.Get("Graph_GroupName") };
        var color = new TextBox { Header = Loc.Get("Graph_GroupColor"), Text = DefaultGroupColors[_viewModel.Project.Groups.Count % DefaultGroupColors.Length] };
        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(name);
        form.Children.Add(color);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Graph_CreateGroupTitle"),
            Content = form,
            PrimaryButtonText = Loc.Get("Dialog_Save"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && _viewModel.CreateGraphGroup(name.Text, color.Text, _selectedCharacterIds) is null)
        {
            await ShowGraphErrorAsync(Loc.Get("Graph_GroupInputError"));
        }
    }

    private void OnAssignSelectionToGroup(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is MenuFlyoutItem { Tag: string groupId })
        {
            _viewModel.AssignCharactersToGraphGroup(_selectedCharacterIds, groupId);
        }
    }

    private async void OnRenameGroup(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not MenuFlyoutItem { Tag: string groupId }
            || _viewModel.Groups.FirstOrDefault(group => group.Id == groupId) is not { } group)
        {
            return;
        }

        var name = new TextBox { Header = Loc.Get("Graph_GroupName"), Text = group.Name };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Graph_RenameGroup"),
            Content = name,
            PrimaryButtonText = Loc.Get("Dialog_Save"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !_viewModel.EditGroup(group, name.Text))
        {
            await ShowGraphErrorAsync(Loc.Get("Graph_GroupInputError"));
        }
    }

    private async void OnChangeGroupColor(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not MenuFlyoutItem { Tag: string groupId }
            || _viewModel.Project.Groups.FirstOrDefault(group => group.Id == groupId) is not { } group)
        {
            return;
        }

        var groupIndex = _viewModel.Project.Groups.IndexOf(group);
        var color = new TextBox
        {
            Header = Loc.Get("Graph_GroupColor"),
            Text = group.BackgroundColor ?? DefaultGroupColors[groupIndex % DefaultGroupColors.Length],
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Graph_ChangeGroupColor"),
            Content = color,
            PrimaryButtonText = Loc.Get("Dialog_Save"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !_viewModel.SetGraphGroupBackgroundColor(groupId, color.Text))
        {
            await ShowGraphErrorAsync(Loc.Get("Graph_ColorInputError"));
        }
    }

    private async Task ShowGraphErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Dialog_InvalidEditTitle"),
            Content = message,
            CloseButtonText = Loc.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private async void OnChangeAvatar(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _selectedCharacterIds.Count != 1)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var contentType = System.IO.Path.GetExtension(file.Name).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = new byte[checked((int)buffer.Length)];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            var isPng = bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
            var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            if ((contentType == "image/png" && !isPng) || (contentType == "image/jpeg" && !isJpeg))
            {
                await ShowGraphErrorAsync(Loc.Get("Character_ImageLoadError"));
                return;
            }

            var imageData = CryptographicBuffer.EncodeToBase64String(buffer);
            await DecodeAvatarAsync(imageData);
            _viewModel.SetCharacterAvatar(_selectedCharacterIds.Single(), imageData, contentType);
        }
        catch (Exception)
        {
            await ShowGraphErrorAsync(Loc.Get("Character_ImageLoadError"));
        }
    }

    private void OnRemoveAvatar(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && _selectedCharacterIds.Count == 1)
        {
            _viewModel.RemoveCharacterAvatar(_selectedCharacterIds.Single());
        }
    }

    private async Task LoadAvatarAsync(Character character, Button icon)
    {
        if (character.AvatarData is not { } data || character.AvatarContentType is not ("image/png" or "image/jpeg"))
        {
            return;
        }

        try
        {
            if (!_avatarImages.TryGetValue(character.Id, out var cached) || cached.Data != data)
            {
                var bitmap = await DecodeAvatarAsync(data);
                if (_viewModel?.Project.Characters.FirstOrDefault(candidate => candidate.Id == character.Id)?.AvatarData != data)
                {
                    return;
                }
                _avatarImages[character.Id] = (data, bitmap);
                cached = (data, bitmap);
            }

            var borderWidth = _selectedCharacterIds.Contains(character.Id) ? 3 : 1;
            icon.Content = new Ellipse
            {
                Width = 68 - 2 * borderWidth,
                Height = 68 - 2 * borderWidth,
                Fill = new ImageBrush
                {
                    ImageSource = cached.Image,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                },
            };
        }
        catch (Exception)
        {
            // A damaged embedded image leaves the default character glyph in place.
        }
    }

    private async Task LoadSelectedAvatarAsync(string characterId, string data, string? contentType)
    {
        if (contentType is not ("image/png" or "image/jpeg"))
        {
            SelectedAvatar.Source = null;
            return;
        }

        try
        {
            if (!_avatarImages.TryGetValue(characterId, out var cached) || cached.Data != data)
            {
                var bitmap = await DecodeAvatarAsync(data);
                if (_viewModel?.Project.Characters.FirstOrDefault(character => character.Id == characterId)?.AvatarData != data)
                {
                    return;
                }
                _avatarImages[characterId] = (data, bitmap);
                cached = (data, bitmap);
            }

            if (_selectedCharacterIds.Count == 1 && _selectedCharacterIds.Contains(characterId))
            {
                SelectedAvatar.Source = cached.Image;
            }
        }
        catch (Exception)
        {
            if (_selectedCharacterIds.Count == 1 && _selectedCharacterIds.Contains(characterId)
                && _viewModel?.Project.Characters.FirstOrDefault(character => character.Id == characterId)?.AvatarData == data)
            {
                SelectedAvatar.Source = null;
            }
        }
    }

    private static async Task<BitmapImage> DecodeAvatarAsync(string data)
    {
        var bytes = Convert.FromBase64String(data);
        var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(buffer);
        stream.Seek(0);
        var bitmap = new BitmapImage { DecodePixelWidth = 192 };
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private void OnEditorSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValidation();

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (_updatingEditor || !_editing || _viewModel is null)
        {
            return;
        }

        var error = _viewModel.ValidateRelationship(_selectedRelationshipId,
            (FirstCharacter.SelectedItem as Character)?.Id ?? string.Empty,
            (SecondCharacter.SelectedItem as Character)?.Id ?? string.Empty,
            RelationshipLabel.Text,
            LabelBackgroundColor.Text,
            LabelForegroundColor.Text);
        ValidationMessage.Text = error is null ? string.Empty : Loc.Get(error);
        SaveRelationshipButton.IsEnabled = error is null;
    }

    private void OnSaveRelationship(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SaveRelationship(_selectedRelationshipId,
            (FirstCharacter.SelectedItem as Character)?.Id ?? string.Empty,
            (SecondCharacter.SelectedItem as Character)?.Id ?? string.Empty,
            RelationshipLabel.Text,
            LabelBackgroundColor.Text,
            LabelForegroundColor.Text) is { } relationship)
        {
            SelectRelationship(relationship.Id);
        }
    }

    private async void OnDeleteRelationship(object sender, RoutedEventArgs e) =>
        await DeleteSelectedRelationshipAsync();

    private async Task DeleteSelectedRelationshipAsync()
    {
        if (_viewModel is null || _selectedRelationshipId is null)
        {
            return;
        }

        var relationship = _viewModel.Project.Relationships.FirstOrDefault(r => r.Id == _selectedRelationshipId);
        if (relationship is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Relationship_DeleteTitle"),
            Content = RelationshipName(relationship),
            PrimaryButtonText = Loc.Get("Dialog_Delete"),
            CloseButtonText = Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _selectedRelationshipId = null;
            _editing = false;
            _viewModel.DeleteRelationship(relationship.Id);
        }
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomIn();
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomOut();
    private void OnFit(object sender, RoutedEventArgs e) => Fit();

    private sealed record RelationshipItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
