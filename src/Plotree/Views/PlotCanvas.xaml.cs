using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Plotree.Models;
using Plotree.Services;
using Plotree.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Plotree.Views;

/// <summary>
/// The pannable/zoomable node editor surface. Renders nodes as <see cref="NodeCard"/>s
/// and edges as Bezier curves, and handles pointer interaction: pan, zoom, node drag,
/// node/edge selection (single, additive and marquee), group drag, edge creation via side
/// handles, card resize, and context menus.
/// </summary>
public sealed partial class PlotCanvas : UserControl, INotifyPropertyChanged
{
    // VK_OEM_PLUS / VK_OEM_MINUS are not named members of Windows.System.VirtualKey.
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 1.1;
    private const double WheelPanFactor = 0.5;
    private const double DragThreshold = 3;
    private const double HandleSize = 14;
    private const double HandleTouchSize = 32;
    private const double EdgeTouchBand = 24;
    private const double GripSize = 12;
    private const double ExtentPadding = 200;

    private enum DragMode
    {
        None,
        Pan,
        Node,
        Edge,
        Resize,
        Marquee,
    }

    /// <summary>Corner a resize gesture grabs; the opposite corner stays anchored.</summary>
    private enum ResizeCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private readonly Dictionary<NodeViewModel, NodeCard> _nodeVisuals = [];
    private readonly Dictionary<NodeViewModel, List<Grid>> _handleVisuals = [];
    private readonly Dictionary<NodeViewModel, List<Rectangle>> _gripVisuals = [];
    private readonly Dictionary<EdgeViewModel, EdgeVisual> _edgeVisuals = [];

    /// <summary>
    /// Node → incident edge visuals. Without this index every node move would scan all edges,
    /// which turns a group drag of N nodes over E edges into N×E work per pointer move.
    /// </summary>
    private readonly Dictionary<NodeViewModel, List<EdgeVisual>> _incidentEdges = [];

    private readonly List<NodeViewModel> _dragNodes = [];
    private readonly HashSet<EdgeVisual> _dragEdgeVisuals = [];
    private readonly List<NodeViewModel> _marqueeHits = [];

    private MainPageViewModel? _viewModel;
    private DragMode _dragMode;
    private NodeViewModel? _dragNode;
    private NodeViewModel? _edgeSource;
    private NodeViewModel? _hoverNode;
    private NodeViewModel? _resizeNode;
    private NodeViewModel? _pendingToggleNode;
    private ResizeCorner _resizeCorner;
    private Point _resizeStartWorld;
    private Rect _resizeOrigin;
    private EdgeSide _edgeSourceSide;
    private Line? _previewLine;
    private Point _lastPointerPosition;
    private Point _marqueeStart;
    private bool _isMarqueeAdditive;

    /// <summary>
    /// True once the current marquee has pushed a hit set to the view model. Without it the
    /// "did the hit set change?" guard below would short-circuit on the very first update of an
    /// empty band (0 hits vs. 0 remembered hits) and a rubber band over blank canvas would
    /// leave the previous selection standing.
    /// </summary>
    private bool _isMarqueeApplied;
    private bool _isGroupDragging;
    private bool _dragMoved;
    private double _zoom = 1;
    private Point _worldOrigin;
    private bool _isUpdatingExtent;
    private bool _isGeometryGestureUpdating;
    private bool _extentUpdatePending;

    /// <summary>Raised when a node is double-clicked (open the details panel, focus title).</summary>
    public event EventHandler<NodeViewModel>? NodeActivated;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlotCanvas()
    {
        InitializeComponent();

        ViewportGrid.PointerPressed += OnViewportPointerPressed;
        ViewportGrid.PointerMoved += OnViewportPointerMoved;
        ViewportGrid.PointerReleased += OnViewportPointerReleased;
        ViewportGrid.PointerCanceled += OnViewportPointerReleased;
        ViewportGrid.PointerCaptureLost += OnViewportPointerReleased;
        // Register on the scroll content, before the ScrollViewer ancestor's class handler.
        // handledEventsToo keeps the gesture alive when a child control handles the routed
        // event, while e.Handled below prevents the ScrollViewer from applying a second
        // built-in scroll after our explicit ChangeView/zoom operation.
        WorldCanvas.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnViewportPointerWheelChanged),
            handledEventsToo: true);
        ViewportGrid.RightTapped += OnViewportRightTapped;
        ViewportGrid.SizeChanged += OnViewportSizeChanged;

        AutomationProperties.SetName(ViewportGrid, Loc.Get("Automation_Canvas"));
        AutomationProperties.SetHelpText(ViewportGrid, Loc.Get("Help_CanvasGestures"));
        AutomationProperties.SetName(MarqueeVisual, Loc.Get("Automation_Marquee"));
    }

    public MainPageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.GraphChanged -= OnGraphChanged;
                _viewModel.SelectionChanged -= OnSelectionChanged;
            }

            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.GraphChanged += OnGraphChanged;
                _viewModel.SelectionChanged += OnSelectionChanged;
            }

            RebuildAll();
        }
    }

    private LayoutDirection Direction => _viewModel?.Project.LayoutDirection ?? LayoutDirection.LeftToRight;

    private double Zoom => _zoom;

    /// <summary>Whether another zoom-in step can be applied.</summary>
    public bool CanZoomIn => Zoom < MaxZoom;

    /// <summary>Whether another zoom-out step can be applied.</summary>
    public bool CanZoomOut => Zoom > MinZoom;

    /// <summary>Zooms in around the center of the visible viewport.</summary>
    public void ZoomIn() => ZoomAtViewportCenter(ZoomStep);

    /// <summary>Zooms out around the center of the visible viewport.</summary>
    public void ZoomOut() => ZoomAtViewportCenter(1 / ZoomStep);

    /// <summary>Adds a node of the given type centered in the current viewport.</summary>
    public void AddNodeAtViewportCenter(NodeType type)
    {
        var world = ToWorld(new Point(ViewportGrid.ActualWidth / 2, ViewportGrid.ActualHeight / 2));
        AddNodeCenteredAt(type, world);
    }

    /// <summary>Adds a node centered on a world point, using the size its type actually resolves to.</summary>
    private void AddNodeCenteredAt(NodeType type, Point world)
    {
        if (_viewModel is null)
        {
            return;
        }

        var size = AppearanceResolver.ResolveDefaults(_viewModel.Project, type);
        _viewModel.AddNode(type, world.X - size.Width / 2, world.Y - size.Height / 2);
    }

    private void OnGraphChanged(object? sender, EventArgs e) => RebuildAll();

    private void OnSelectionChanged(object? sender, EventArgs e) => UpdateSelectionEmphasis();

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewportGrid.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
        UpdateWorldExtent();
    }

    // ----- Visual tree construction -----

    private void RebuildAll()
    {
        foreach (var node in _nodeVisuals.Keys)
        {
            node.PropertyChanged -= OnNodeViewModelPropertyChanged;
        }

        foreach (var edge in _edgeVisuals.Keys)
        {
            edge.PropertyChanged -= OnEdgeViewModelPropertyChanged;
        }

        _nodeVisuals.Clear();
        _handleVisuals.Clear();
        _gripVisuals.Clear();
        _edgeVisuals.Clear();
        _incidentEdges.Clear();
        _dragNodes.Clear();
        _dragEdgeVisuals.Clear();
        _marqueeHits.Clear();
        NodeLayer.Children.Clear();
        EdgeLayer.Children.Clear();
        HideMarquee();
        _dragMode = DragMode.None;
        _dragNode = null;
        _edgeSource = null;
        _hoverNode = null;
        _resizeNode = null;
        _pendingToggleNode = null;
        _isGroupDragging = false;
        _isGeometryGestureUpdating = false;
        _extentUpdatePending = false;
        _previewLine = null;

        if (_viewModel is null)
        {
            WorldCanvas.Width = Math.Max(1, ViewportGrid.ActualWidth);
            WorldCanvas.Height = Math.Max(1, ViewportGrid.ActualHeight);
            return;
        }

        UpdateWorldExtent();

        foreach (var edge in _viewModel.Edges)
        {
            AddEdgeVisual(edge);
        }

        foreach (var node in _viewModel.Nodes)
        {
            AddNodeVisual(node);
        }

        UpdateSelectionEmphasis();
    }

    /// <summary>Records an edge visual against both of its endpoints for O(degree) geometry updates.</summary>
    private void IndexIncidentEdge(EdgeViewModel edge, EdgeVisual visual)
    {
        Add(edge.From);
        Add(edge.To);

        void Add(NodeViewModel node)
        {
            if (!_incidentEdges.TryGetValue(node, out var visuals))
            {
                _incidentEdges[node] = visuals = [];
            }

            if (!visuals.Contains(visual))
            {
                visuals.Add(visual);
            }
        }
    }

    private void AddNodeVisual(NodeViewModel node)
    {
        var card = new NodeCard(node);
        AutomationProperties.SetAutomationId(card, $"Node_{node.Model.Id}");
        PositionNodeCard(node, card);

        card.PointerPressed += (s, e) => OnNodePointerPressed(card, node, e);
        card.PointerMoved += (s, e) => OnNodePointerMoved(node, e);
        card.PointerReleased += (s, e) => OnNodePointerReleased(card, node, e);
        card.PointerCanceled += (s, e) => OnNodePointerReleased(card, node, e);
        // Losing capture (window deactivation, touch cancellation) must end the drag too,
        // otherwise the card stays glued to the pointer. The handler is re-entrancy safe.
        card.PointerCaptureLost += (s, e) => OnNodePointerReleased(card, node, e);
        card.DoubleTapped += (s, e) =>
        {
            _viewModel?.SelectNode(node);
            NodeActivated?.Invoke(this, node);
            e.Handled = true;
        };
        card.RightTapped += (s, e) => OnNodeRightTapped(card, node, e);

        node.PropertyChanged += OnNodeViewModelPropertyChanged;
        _nodeVisuals[node] = card;
        NodeLayer.Children.Add(card);

        // Any side can start a connection. Endings are terminal and therefore omit all handles.
        if (node.Type != NodeType.Ending)
        {
            var handles = new List<Grid>();
            foreach (var side in Enum.GetValues<EdgeSide>())
            {
                var handleVisual = new Ellipse
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = GetThemeBrush("AccentFillColorDefaultBrush"),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                var handle = new Grid
                {
                    Width = HandleTouchSize,
                    Height = HandleTouchSize,
                    Background = new SolidColorBrush(Colors.Transparent),
                    Tag = side,
                    Visibility = Visibility.Collapsed,
                };
                handle.Children.Add(handleVisual);
                ApplyHandleZoom(handle);
                AutomationProperties.SetAutomationId(handle, $"ConnectionHandle_{node.Model.Id}_{side}");
                // A shape with only an AutomationId stays in the UIA raw view; the accessible
                // name is what promotes it into the control view for screen readers (and makes
                // it addressable from automated tests), exactly as the resize grips do.
                AutomationProperties.SetName(handle, Loc.Get("Tooltip_DragToConnect"));
                ToolTipService.SetToolTip(handle, Loc.Get("Tooltip_DragToConnect"));

                handle.PointerPressed += (s, e) => OnHandlePointerPressed(handle, node, side, e);
                handle.PointerMoved += (s, e) => OnHandlePointerMoved(e);
                handle.PointerReleased += (s, e) => OnHandlePointerReleased(handle, e);
                handle.PointerCanceled += (s, e) => OnHandlePointerReleased(handle, e);
                handle.PointerCaptureLost += (s, e) => OnHandlePointerReleased(handle, e);
                handle.PointerEntered += (s, e) => SetHoverNode(node);
                handle.PointerExited += (s, e) => UpdateHoverNode(node, e);

                handles.Add(handle);
                NodeLayer.Children.Add(handle);
                PositionHandle(node, handle, side);
            }

            _handleVisuals[node] = handles;
        }

        // Corner grips resize the card. They sit on the corners, so they never cover
        // the side connection ports at the edge midpoints.
        var grips = new List<Rectangle>();
        foreach (var corner in Enum.GetValues<ResizeCorner>())
        {
            var grip = new Rectangle
            {
                Width = GripSize,
                Height = GripSize,
                RadiusX = 2,
                RadiusY = 2,
                Fill = GetThemeBrush("CardBackgroundFillColorDefaultBrush"),
                Stroke = GetThemeBrush("AccentFillColorDefaultBrush"),
                StrokeThickness = 2,
                Tag = corner,
                Visibility = Visibility.Collapsed,
            };
            ApplyZoom(grip, GripSize);
            AutomationProperties.SetAutomationId(grip, $"ResizeGrip_{node.Model.Id}_{corner}");
            AutomationProperties.SetName(grip, ResizeGripName);
            ToolTipService.SetToolTip(grip, ResizeGripName);

            grip.PointerPressed += (s, e) => OnGripPointerPressed(grip, node, corner, e);
            grip.PointerMoved += (s, e) => OnGripPointerMoved(node, e);
            grip.PointerReleased += (s, e) => OnGripPointerReleased(grip, node, e);
            grip.PointerCanceled += (s, e) => OnGripPointerReleased(grip, node, e);
            grip.PointerCaptureLost += (s, e) => OnGripPointerReleased(grip, node, e);

            grips.Add(grip);
            NodeLayer.Children.Add(grip);
            PositionGrip(node, grip, corner);
        }

        _gripVisuals[node] = grips;

        card.PointerEntered += (s, e) => SetHoverNode(node);
        card.PointerExited += (s, e) => UpdateHoverNode(node, e);
    }

    private void AddEdgeVisual(EdgeViewModel edge)
    {
        var visual = new EdgeVisual(edge);
        AutomationProperties.SetAutomationId(visual.HitTarget, $"Edge_{edge.Model.Id}");
        // Like the connection handles, a bare Path with only an AutomationId never leaves the
        // UIA raw view. Naming it by its endpoints puts the connection in the control view so
        // screen readers can reach it and announce what it links.
        AutomationProperties.SetName(
            visual.HitTarget,
            Loc.Format("Automation_Edge", edge.From.DisplayTitle, edge.To.DisplayTitle));
        visual.HitTarget.PointerPressed += (s, e) =>
        {
            _viewModel?.SelectEdge(edge);
            e.Handled = true;
        };
        visual.HitTarget.RightTapped += (s, e) => OnEdgeRightTapped(edge, e);

        edge.PropertyChanged += OnEdgeViewModelPropertyChanged;
        _edgeVisuals[edge] = visual;
        IndexIncidentEdge(edge, visual);
        visual.AddTo(EdgeLayer);
        visual.UpdateGeometry(Direction, Zoom, _worldOrigin);
    }

    private void OnNodeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeViewModel node)
        {
            return;
        }

        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y))
        {
            if (_nodeVisuals.TryGetValue(node, out var card))
            {
                PositionNodeCard(node, card);
            }

            UpdateNodeGeometry(node);
            RequestWorldExtentUpdate();
        }
        else if (e.PropertyName is nameof(NodeViewModel.EffectiveWidth) or nameof(NodeViewModel.EffectiveHeight))
        {
            // The card itself is bound to the resolved size; only the surrounding
            // geometry (ports, grips, incident edges) has to follow.
            UpdateNodeGeometry(node);
            RequestWorldExtentUpdate();
        }
        else if (e.PropertyName is nameof(NodeViewModel.Type))
        {
            // Changing type changes which connection ports exist, so rebuild the visuals.
            RebuildAll();
        }
    }

    /// <summary>Repositions a node's ports and resize grips and re-routes its incident edges.</summary>
    private void UpdateNodeGeometry(NodeViewModel node)
    {
        PositionNodeAdornments(node);

        // During a group drag the union of incident edges is redrawn once per pointer move
        // by the drag handler, so skip the per-node pass that would repeat shared edges.
        if (_isGroupDragging)
        {
            return;
        }

        if (_incidentEdges.TryGetValue(node, out var visuals))
        {
            foreach (var visual in visuals)
            {
                visual.UpdateGeometry(Direction, Zoom, _worldOrigin);
            }
        }
    }

    /// <summary>Repositions the connection ports and resize grips that surround a node.</summary>
    private void PositionNodeAdornments(NodeViewModel node)
    {
        if (_handleVisuals.TryGetValue(node, out var handles))
        {
            foreach (var handle in handles)
            {
                PositionHandle(node, handle, (EdgeSide)handle.Tag);
            }
        }

        if (_gripVisuals.TryGetValue(node, out var grips))
        {
            foreach (var grip in grips)
            {
                PositionGrip(node, grip, (ResizeCorner)grip.Tag);
            }
        }
    }

    private void OnEdgeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EdgeViewModel edge || !_edgeVisuals.TryGetValue(edge, out var visual))
        {
            return;
        }

        if (e.PropertyName == nameof(EdgeViewModel.LabelText))
        {
            visual.RefreshLabel();
        }
    }

    // ----- Node drag & selection -----

    private void OnNodePointerPressed(NodeCard card, NodeViewModel node, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportGrid);
        if (!point.Properties.IsLeftButtonPressed || _viewModel is null)
        {
            return;
        }

        _pendingToggleNode = null;

        if (IsAdditiveModifier(e.KeyModifiers))
        {
            // Ctrl/Shift on an already selected node must not deselect before a possible drag:
            // defer the removal to the release that never moved.
            if (node.IsSelected)
            {
                _pendingToggleNode = node;
            }
            else
            {
                _viewModel.AddNodeToSelection(node);
            }
        }
        else if (!node.IsSelected)
        {
            _viewModel.SelectNode(node);
        }

        BeginNodeDrag(node);
        _lastPointerPosition = point.Position;
        card.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// Starts a node drag. Dragging any selected node moves the whole selection, and the
    /// union of the moved nodes' incident edges is captured once so pointer moves never
    /// rescan the edge list.
    /// </summary>
    private void BeginNodeDrag(NodeViewModel node)
    {
        _dragMode = DragMode.Node;
        _dragNode = node;
        _dragMoved = false;
        _isGeometryGestureUpdating = true;

        _dragNodes.Clear();
        if (node.IsSelected && _viewModel is not null)
        {
            _dragNodes.AddRange(_viewModel.SelectedNodes);
        }

        if (_dragNodes.Count == 0)
        {
            _dragNodes.Add(node);
        }

        _isGroupDragging = _dragNodes.Count > 1;
        _dragEdgeVisuals.Clear();
        if (!_isGroupDragging)
        {
            return;
        }

        foreach (var dragged in _dragNodes)
        {
            if (_incidentEdges.TryGetValue(dragged, out var visuals))
            {
                foreach (var visual in visuals)
                {
                    _dragEdgeVisuals.Add(visual);
                }
            }
        }
    }

    private void OnNodePointerMoved(NodeViewModel node, PointerRoutedEventArgs e)
    {
        if (_dragMode == DragMode.Edge && _edgeSource == node)
        {
            UpdateEdgeDrag(e);
            return;
        }

        if (_dragMode != DragMode.Node || _dragNode != node)
        {
            return;
        }

        var position = e.GetCurrentPoint(ViewportGrid).Position;
        var dx = position.X - _lastPointerPosition.X;
        var dy = position.Y - _lastPointerPosition.Y;

        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        _dragMoved = true;
        _pendingToggleNode = null;
        _lastPointerPosition = position;

        var worldDx = dx / Zoom;
        var worldDy = dy / Zoom;
        foreach (var dragged in _dragNodes)
        {
            dragged.X += worldDx;
            dragged.Y += worldDy;
        }

        // One batched pass over the distinct incident edges instead of one pass per moved node.
        foreach (var visual in _dragEdgeVisuals)
        {
            visual.UpdateGeometry(Direction, Zoom, _worldOrigin);
        }

        e.Handled = true;
    }

    private void OnNodePointerReleased(NodeCard card, NodeViewModel node, PointerRoutedEventArgs e)
    {
        if (_dragMode == DragMode.Edge && _edgeSource == node)
        {
            EndEdgeDrag(card, e);
            return;
        }

        if (_dragMode != DragMode.Node || _dragNode != node)
        {
            return;
        }

        // Same re-entrancy discipline as the viewport handler: snapshot the gesture and clear
        // the drag state before releasing capture, so a nested PointerCaptureLost is a no-op.
        var moved = _dragMoved;
        var pendingToggle = _pendingToggleNode;
        var draggedNodes = _dragNodes.ToArray();

        _dragMode = DragMode.None;
        _dragNode = null;
        _dragMoved = false;
        _isGroupDragging = false;
        _pendingToggleNode = null;
        _dragEdgeVisuals.Clear();
        _dragNodes.Clear();

        card.ReleasePointerCaptures();
        _isGeometryGestureUpdating = false;

        if (moved)
        {
            foreach (var dragged in draggedNodes)
            {
                dragged.CompleteDrag();
            }
        }
        else if (pendingToggle == node)
        {
            _viewModel?.ToggleNodeSelection(node);
        }
        else if (!IsAdditiveModifier(e.KeyModifiers))
        {
            // A plain click inside a multi-selection collapses it onto the clicked node.
            _viewModel?.SelectNode(node);
        }

        FlushPendingWorldExtent();
        e.Handled = true;
    }

    // ----- Card resize via corner grips -----

    private void OnGripPointerPressed(Rectangle grip, NodeViewModel node, ResizeCorner corner, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _viewModel?.SelectNode(node);

        _dragMode = DragMode.Resize;
        _resizeNode = node;
        _resizeCorner = corner;
        _dragMoved = false;
        _isGeometryGestureUpdating = true;
        _resizeStartWorld = ToWorld(point.Position);
        _resizeOrigin = new Rect(node.X, node.Y, node.EffectiveWidth, node.EffectiveHeight);

        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripPointerMoved(NodeViewModel node, PointerRoutedEventArgs e)
    {
        if (_dragMode != DragMode.Resize || _resizeNode != node)
        {
            return;
        }

        // World-space deltas keep the grip glued to the pointer at every zoom level.
        var world = ToWorld(e.GetCurrentPoint(ViewportGrid).Position);
        var dx = world.X - _resizeStartWorld.X;
        var dy = world.Y - _resizeStartWorld.Y;

        if (!_dragMoved && (Math.Abs(dx) + Math.Abs(dy)) * Zoom < DragThreshold)
        {
            return;
        }

        _dragMoved = true;

        var isWest = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
        var isNorth = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight;

        var width = Math.Clamp(
            isWest ? _resizeOrigin.Width - dx : _resizeOrigin.Width + dx,
            NodeViewModel.MinCardWidth,
            NodeViewModel.MaxCardWidth);
        var height = Math.Clamp(
            isNorth ? _resizeOrigin.Height - dy : _resizeOrigin.Height + dy,
            NodeViewModel.MinCardHeight,
            NodeViewModel.MaxCardHeight);

        // Dragging a west/north grip moves the card so the opposite corner stays anchored.
        node.X = isWest ? _resizeOrigin.Right - width : _resizeOrigin.Left;
        node.Y = isNorth ? _resizeOrigin.Bottom - height : _resizeOrigin.Top;
        node.Resize(width, height);

        e.Handled = true;
    }

    private void OnGripPointerReleased(Rectangle grip, NodeViewModel node, PointerRoutedEventArgs e)
    {
        if (_dragMode != DragMode.Resize || _resizeNode != node)
        {
            return;
        }

        grip.ReleasePointerCaptures();
        _dragMode = DragMode.None;
        _resizeNode = null;
        _isGeometryGestureUpdating = false;

        if (_dragMoved)
        {
            // A resized card keeps its manual geometry: pin it out of auto-layout.
            node.CompleteResize();
            UpdateNodeGeometry(node);
        }

        FlushPendingWorldExtent();
        _dragMoved = false;
        e.Handled = true;
    }

    private static Point GetCorner(NodeViewModel node, ResizeCorner corner) => corner switch
    {
        ResizeCorner.TopLeft => new Point(node.X, node.Y),
        ResizeCorner.TopRight => new Point(node.X + node.EffectiveWidth, node.Y),
        ResizeCorner.BottomLeft => new Point(node.X, node.Y + node.EffectiveHeight),
        _ => new Point(node.X + node.EffectiveWidth, node.Y + node.EffectiveHeight),
    };

    private void PositionNodeCard(NodeViewModel node, NodeCard card)
    {
        Canvas.SetLeft(card, (node.X + _worldOrigin.X) * Zoom);
        Canvas.SetTop(card, (node.Y + _worldOrigin.Y) * Zoom);
        card.RenderTransformOrigin = new Point(0, 0);
        card.RenderTransform = new CompositeTransform
        {
            ScaleX = Zoom,
            ScaleY = Zoom,
        };
    }

    private void ApplyZoom(FrameworkElement element, double size)
    {
        // Port and grip sizes are set in content pixels because the canvas itself is laid out
        // in zoomed coordinates rather than relying on a RenderTransform for its extent.
        element.Width = size * Zoom;
        element.Height = size * Zoom;
    }

    private void ApplyHandleZoom(Grid handle)
    {
        var visualSize = HandleSize * Zoom;
        handle.Width = Math.Max(HandleTouchSize, visualSize);
        handle.Height = Math.Max(HandleTouchSize, visualSize);
        if (handle.Children[0] is Ellipse visual)
        {
            visual.Width = visualSize;
            visual.Height = visualSize;
        }
    }

    private void PositionGrip(NodeViewModel node, Rectangle grip, ResizeCorner corner)
    {
        var point = GetCorner(node, corner);
        Canvas.SetLeft(grip, (point.X + _worldOrigin.X) * Zoom - grip.Width / 2);
        Canvas.SetTop(grip, (point.Y + _worldOrigin.Y) * Zoom - grip.Height / 2);
    }

    /// <summary>
    /// Resize grips belong to a single selected node only: showing them for every node of a
    /// multi-selection would bury the cards under grips and make group drag ambiguous.
    /// </summary>
    private void UpdateGripVisibility(NodeViewModel node)
    {
        if (!_gripVisuals.TryGetValue(node, out var grips))
        {
            return;
        }

        var isVisible = _resizeNode == node || (node.IsSelected && _viewModel?.IsSingleNodeSelected == true);
        foreach (var grip in grips)
        {
            grip.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ----- Edge creation via node edges and side handles -----

    private void OnHandlePointerPressed(Grid handle, NodeViewModel node, EdgeSide side, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(ViewportGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginEdgeDrag(handle, node, side, e);
    }

    private void BeginEdgeDrag(UIElement captureOwner, NodeViewModel node, EdgeSide side, PointerRoutedEventArgs e)
    {
        _dragMode = DragMode.Edge;
        _edgeSource = node;
        _edgeSourceSide = side;

        var anchor = ToCanvas(GetAnchor(node, side));
        _previewLine = new Line
        {
            X1 = anchor.X,
            Y1 = anchor.Y,
            X2 = anchor.X,
            Y2 = anchor.Y,
            Stroke = GetThemeBrush("AccentFillColorDefaultBrush"),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            IsHitTestVisible = false,
        };
        EdgeLayer.Children.Add(_previewLine);

        captureOwner.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnHandlePointerMoved(PointerRoutedEventArgs e)
    {
        UpdateEdgeDrag(e);
    }

    private void UpdateEdgeDrag(PointerRoutedEventArgs e)
    {
        if (_dragMode != DragMode.Edge || _previewLine is null)
        {
            return;
        }

        var canvasPoint = ToCanvas(ToWorld(e.GetCurrentPoint(ViewportGrid).Position));
        _previewLine.X2 = canvasPoint.X;
        _previewLine.Y2 = canvasPoint.Y;
        e.Handled = true;
    }

    private void OnHandlePointerReleased(Grid handle, PointerRoutedEventArgs e)
    {
        EndEdgeDrag(handle, e);
    }

    private void EndEdgeDrag(UIElement captureOwner, PointerRoutedEventArgs e)
    {
        if (_dragMode != DragMode.Edge)
        {
            return;
        }

        // ReleasePointerCaptures raises PointerCaptureLost synchronously. Reset the state
        // first so its routed handler is a harmless no-op.
        var source = _edgeSource;
        var sourceSide = _edgeSourceSide;
        _edgeSource = null;
        _dragMode = DragMode.None;

        if (_previewLine is not null)
        {
            EdgeLayer.Children.Remove(_previewLine);
            _previewLine = null;
        }

        captureOwner.ReleasePointerCaptures();
        if (source is not null)
        {
            UpdateHandleVisibility(source);
        }

        var world = ToWorld(e.GetCurrentPoint(ViewportGrid).Position);
        var target = FindNodeAt(world);
        if (source is not null && target is not null)
        {
            var targetSide = TryGetEdgeSide(target, world, out var explicitTargetSide)
                ? explicitTargetSide
                : OppositeSide(sourceSide);
            _viewModel?.AddEdge(source, target, sourceSide, targetSide);
        }

        e.Handled = true;
    }

    private static Point GetAnchor(NodeViewModel node, EdgeSide side) => side switch
    {
        EdgeSide.Left => new Point(node.X, node.Y + node.EffectiveHeight / 2),
        EdgeSide.Top => new Point(node.X + node.EffectiveWidth / 2, node.Y),
        EdgeSide.Bottom => new Point(node.X + node.EffectiveWidth / 2, node.Y + node.EffectiveHeight),
        _ => new Point(node.X + node.EffectiveWidth, node.Y + node.EffectiveHeight / 2),
    };

    private void PositionHandle(NodeViewModel node, Grid handle, EdgeSide side)
    {
        var anchor = GetAnchor(node, side);
        Canvas.SetLeft(handle, (anchor.X + _worldOrigin.X) * Zoom - handle.Width / 2);
        Canvas.SetTop(handle, (anchor.Y + _worldOrigin.Y) * Zoom - handle.Height / 2);
    }

    private NodeViewModel? FindNodeAt(Point world)
    {
        if (_viewModel is null)
        {
            return null;
        }

        NodeViewModel? hit = null;
        foreach (var node in _viewModel.Nodes)
        {
            if (world.X >= node.X && world.X <= node.X + node.EffectiveWidth
                && world.Y >= node.Y && world.Y <= node.Y + node.EffectiveHeight)
            {
                hit = node;
            }
        }

        return hit;
    }

    private static EdgeSide GetNearestSide(NodeViewModel node, Point world)
    {
        var leftDistance = Math.Abs(world.X - node.X);
        var rightDistance = Math.Abs(world.X - (node.X + node.EffectiveWidth));
        var topDistance = Math.Abs(world.Y - node.Y);
        var bottomDistance = Math.Abs(world.Y - (node.Y + node.EffectiveHeight));
        var minimum = Math.Min(Math.Min(leftDistance, rightDistance), Math.Min(topDistance, bottomDistance));

        return minimum == leftDistance ? EdgeSide.Left
            : minimum == rightDistance ? EdgeSide.Right
            : minimum == topDistance ? EdgeSide.Top
            : EdgeSide.Bottom;
    }

    /// <summary>
    /// Finds an edge touch target in viewport-sized units, preserving a usable touch target
    /// across zoom levels. A release away from an edge is handled by the caller as an inferred
    /// incoming side instead of pretending that a card's nearest edge was explicitly chosen.
    /// </summary>
    private bool TryGetEdgeSide(NodeViewModel node, Point world, out EdgeSide side)
    {
        side = GetNearestSide(node, world);
        var distance = side switch
        {
            EdgeSide.Left => Math.Abs(world.X - node.X),
            EdgeSide.Right => Math.Abs(world.X - (node.X + node.EffectiveWidth)),
            EdgeSide.Top => Math.Abs(world.Y - node.Y),
            _ => Math.Abs(world.Y - (node.Y + node.EffectiveHeight)),
        };

        return distance <= EdgeTouchBand / Zoom;
    }

    private static EdgeSide OppositeSide(EdgeSide side) => side switch
    {
        EdgeSide.Left => EdgeSide.Right,
        EdgeSide.Right => EdgeSide.Left,
        EdgeSide.Top => EdgeSide.Bottom,
        _ => EdgeSide.Top,
    };

    private void SetHandlesVisible(NodeViewModel node, bool isVisible)
    {
        if (_handleVisuals.TryGetValue(node, out var handles))
        {
            foreach (var handle in handles)
            {
                handle.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void UpdateHandleVisibility(NodeViewModel node) =>
        SetHandlesVisible(
            node,
            _hoverNode == node
            || _edgeSource == node
            || ReferenceEquals(_viewModel?.SelectedNode, node));

    private void SetHoverNode(NodeViewModel? node)
    {
        if (ReferenceEquals(_hoverNode, node))
        {
            return;
        }

        var previous = _hoverNode;
        _hoverNode = node;

        if (previous is not null)
        {
            UpdateHandleVisibility(previous);
        }

        if (node is not null)
        {
            UpdateHandleVisibility(node);
        }
    }

    private void UpdateHoverNode(NodeViewModel node, PointerRoutedEventArgs e)
    {
        var world = ToWorld(e.GetCurrentPoint(ViewportGrid).Position);
        var isOverNodeOrHandle =
            world.X >= node.X - HandleSize / 2
            && world.X <= node.X + node.EffectiveWidth + HandleSize / 2
            && world.Y >= node.Y - HandleSize / 2
            && world.Y <= node.Y + node.EffectiveHeight + HandleSize / 2;

        if (isOverNodeOrHandle)
        {
            SetHoverNode(node);
        }
        else if (_hoverNode == node)
        {
            SetHoverNode(null);
        }
        else
        {
            UpdateHandleVisibility(node);
        }
    }

    /// <summary>
    /// Repaints selection, related-node and connected-edge emphasis for the whole graph.
    /// The related set is the complete weakly connected component of the selection, derived
    /// from the incident-edge index without repeatedly scanning the graph.
    /// </summary>
    private void UpdateSelectionEmphasis()
    {
        if (_viewModel is null)
        {
            return;
        }

        var primary = _viewModel.SelectedNode;
        var selectedNodes = new HashSet<NodeViewModel>(_viewModel.SelectedNodes);
        var connectedNodes = new HashSet<NodeViewModel>();
        var connectedVisuals = new HashSet<EdgeVisual>();
        var pending = new Queue<NodeViewModel>();

        if (_viewModel.SelectedEdge is { } selectedEdge)
        {
            AddConnectedNode(selectedEdge.From);
            AddConnectedNode(selectedEdge.To);
        }

        foreach (var selected in selectedNodes)
        {
            AddConnectedNode(selected);
        }

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (!_incidentEdges.TryGetValue(node, out var visuals))
            {
                continue;
            }

            foreach (var visual in visuals)
            {
                connectedVisuals.Add(visual);
                AddConnectedNode(visual.Edge.From);
                AddConnectedNode(visual.Edge.To);
            }
        }

        connectedNodes.ExceptWith(selectedNodes);

        foreach (var (node, card) in _nodeVisuals)
        {
            var emphasis = node.IsSelected
                ? ReferenceEquals(node, primary) ? NodeEmphasis.Primary : NodeEmphasis.Selected
                : connectedNodes.Contains(node) ? NodeEmphasis.Related : NodeEmphasis.None;

            card.UpdateSelectionEmphasis(emphasis);
            UpdateHandleVisibility(node);
            UpdateGripVisibility(node);
        }

        foreach (var (edge, visual) in _edgeVisuals)
        {
            visual.UpdateEmphasis(edge.IsSelected, connectedVisuals.Contains(visual));
        }

        void AddConnectedNode(NodeViewModel node)
        {
            if (connectedNodes.Add(node))
            {
                pending.Enqueue(node);
            }
        }
    }

    // ----- Context menus -----

    private void OnViewportRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (!IsBackgroundElement(e.OriginalSource))
        {
            return;
        }

        var position = e.GetPosition(ViewportGrid);
        var world = ToWorld(position);
        var flyout = new MenuFlyout();
        AddCreateItem(flyout, NodeType.Scene, Loc.Get("Context_AddSceneHere"), "\uE8A5", world);
        AddCreateItem(flyout, NodeType.Choice, Loc.Get("NodeType_Choice"), "\uE8AB", world);
        AddCreateItem(flyout, NodeType.Ending, Loc.Get("Context_AddEndingHere"), "\uE71A", world);
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateClipboardItem(
            "Context_Copy",
            "\uE8C8",
            "ContextCopy",
            _viewModel?.CopyCommand,
            VirtualKey.C));
        flyout.Items.Add(CreateClipboardItem(
            "Context_Paste",
            "\uE77F",
            "ContextPaste",
            _viewModel?.PasteCommand,
            VirtualKey.V));
        flyout.Items.Add(new MenuFlyoutSeparator());

        var zoomIn = new MenuFlyoutItem
        {
            Text = Loc.Get("Context_ZoomIn"),
            Icon = new FontIcon { Glyph = "\uE8A3" },
            IsEnabled = CanZoomIn,
        };
        zoomIn.Click += (_, _) => ZoomIn();
        AutomationProperties.SetAutomationId(zoomIn, "ContextZoomIn");
        flyout.Items.Add(zoomIn);

        var zoomOut = new MenuFlyoutItem
        {
            Text = Loc.Get("Context_ZoomOut"),
            Icon = new FontIcon { Glyph = "\uE71F" },
            IsEnabled = CanZoomOut,
        };
        zoomOut.Click += (_, _) => ZoomOut();
        AutomationProperties.SetAutomationId(zoomOut, "ContextZoomOut");
        flyout.Items.Add(zoomOut);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var selectAll = new MenuFlyoutItem
        {
            Text = Loc.Get("Context_SelectAll"),
            Icon = new FontIcon { Glyph = "\uE8B3" },
            Command = _viewModel?.SelectAllNodesCommand,
        };
        selectAll.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = VirtualKey.A,
            Modifiers = VirtualKeyModifiers.Control,
            IsEnabled = false, // Display only: the page-level accelerator owns the shortcut.
        });
        AutomationProperties.SetAutomationId(selectAll, "ContextSelectAllNodes");
        flyout.Items.Add(selectAll);

        flyout.ShowAt(ViewportGrid, position);
        e.Handled = true;
    }

    private static MenuFlyoutItem CreateClipboardItem(
        string textKey,
        string glyph,
        string automationId,
        System.Windows.Input.ICommand? command,
        VirtualKey key)
    {
        var item = new MenuFlyoutItem
        {
            Text = Loc.Get(textKey),
            Icon = new FontIcon { Glyph = glyph },
            Command = command,
        };
        item.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = key,
            Modifiers = VirtualKeyModifiers.Control,
            IsEnabled = false, // Display only: the page-level accelerator owns the shortcut.
        });
        AutomationProperties.SetAutomationId(item, automationId);
        return item;
    }

    private void AddCreateItem(MenuFlyout flyout, NodeType type, string text, string glyph, Point world)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetAutomationId(item, $"ContextAdd{type}");
        item.Click += (s, e) => AddNodeCenteredAt(type, world);
        flyout.Items.Add(item);
    }

    private void OnNodeRightTapped(NodeCard card, NodeViewModel node, RightTappedRoutedEventArgs e)
    {
        // Right-clicking inside a multi-selection keeps it, so bulk delete/unpin stay reachable.
        if (!node.IsSelected)
        {
            _viewModel?.SelectNode(node);
        }

        var selectedCount = _viewModel?.SelectedNodeCount ?? 1;
        var isMultiple = selectedCount > 1;

        var copy = CreateClipboardItem(
            "Context_Copy",
            "\uE8C8",
            "ContextCopy",
            _viewModel?.CopyCommand,
            VirtualKey.C);
        var paste = CreateClipboardItem(
            "Context_Paste",
            "\uE77F",
            "ContextPaste",
            _viewModel?.PasteCommand,
            VirtualKey.V);

        var unpin = new MenuFlyoutItem
        {
            Text = isMultiple ? Loc.Format("Context_UnpinSelected", selectedCount) : Loc.Get("Context_Unpin"),
            Icon = new FontIcon { Glyph = "\uE77A" },
            Command = _viewModel?.UnpinSelectionCommand,
        };
        AutomationProperties.SetAutomationId(unpin, "ContextUnpinNode");

        var delete = new MenuFlyoutItem
        {
            Text = isMultiple ? Loc.Format("Context_DeleteNodes", selectedCount) : Loc.Get("Context_DeleteNode"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            Command = _viewModel?.DeleteSelectedCommand,
        };
        AutomationProperties.SetAutomationId(delete, "ContextDeleteNode");

        var flyout = new MenuFlyout();
        flyout.Items.Add(copy);
        flyout.Items.Add(paste);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(unpin);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);
        flyout.ShowAt(card, e.GetPosition(card));
        e.Handled = true;
    }

    private void OnEdgeRightTapped(EdgeViewModel edge, RightTappedRoutedEventArgs e)
    {
        _viewModel?.SelectEdge(edge);

        var delete = new MenuFlyoutItem
        {
            Text = Loc.Get("Context_DeleteEdge"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            Command = _viewModel?.DeleteSelectedCommand,
        };
        AutomationProperties.SetAutomationId(delete, "ContextDeleteEdge");

        var flyout = new MenuFlyout();
        flyout.Items.Add(delete);
        flyout.ShowAt(ViewportGrid, e.GetPosition(ViewportGrid));
        e.Handled = true;
    }

    // ----- Canvas extents -----

    /// <summary>
    /// Recalculates the scrollable content in zoomed content pixels. The origin is moved into
    /// the positive canvas space so nodes with negative coordinates remain reachable by the
    /// automatic scroll bars. A viewport anchor is retained whenever the extent changes.
    /// </summary>
    private void UpdateWorldExtent(Point? anchorViewport = null, Point? anchorWorldOverride = null)
    {
        if (_isUpdatingExtent)
        {
            return;
        }

        var viewport = anchorViewport
            ?? new Point(ViewportGrid.ActualWidth / 2, ViewportGrid.ActualHeight / 2);
        var anchorWorld = anchorWorldOverride ?? ToWorld(viewport);
        var extent = CanvasExtentCalculator.Calculate(
            _viewModel?.Nodes.Select(node => new CanvasNodeBounds(
                node.X,
                node.Y,
                node.EffectiveWidth,
                node.EffectiveHeight)) ?? Enumerable.Empty<CanvasNodeBounds>(),
            ViewportGrid.ActualWidth,
            ViewportGrid.ActualHeight,
            Zoom,
            ExtentPadding,
            anchorWorld.X,
            anchorWorld.Y,
            viewport.X,
            viewport.Y);
        var width = extent.Width;
        var height = extent.Height;
        _worldOrigin = new Point(extent.OriginX, extent.OriginY);

        _isUpdatingExtent = true;
        try
        {
            WorldCanvas.Width = width * Zoom;
            WorldCanvas.Height = height * Zoom;

            foreach (var (node, card) in _nodeVisuals)
            {
                PositionNodeCard(node, card);
                if (_handleVisuals.TryGetValue(node, out var handles))
                {
                    foreach (var handle in handles)
                    {
                        ApplyHandleZoom(handle);
                        PositionHandle(node, handle, (EdgeSide)handle.Tag);
                    }
                }

                if (_gripVisuals.TryGetValue(node, out var grips))
                {
                    foreach (var grip in grips)
                    {
                        ApplyZoom(grip, GripSize);
                        PositionGrip(node, grip, (ResizeCorner)grip.Tag);
                    }
                }
            }

            foreach (var visual in _edgeVisuals.Values)
            {
                visual.UpdateGeometry(Direction, Zoom, _worldOrigin);
            }

            var horizontalOffset = (anchorWorld.X + _worldOrigin.X) * Zoom - viewport.X;
            var verticalOffset = (anchorWorld.Y + _worldOrigin.Y) * Zoom - viewport.Y;
            WorldScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, disableAnimation: true);
        }
        finally
        {
            _isUpdatingExtent = false;
        }
    }

    private void RequestWorldExtentUpdate()
    {
        if (_isGeometryGestureUpdating)
        {
            _extentUpdatePending = true;
            return;
        }

        UpdateWorldExtent();
    }

    private void FlushPendingWorldExtent()
    {
        if (!_extentUpdatePending)
        {
            return;
        }

        _extentUpdatePending = false;
        UpdateWorldExtent();
    }

    private Point ToCanvas(Point world) =>
        new((world.X + _worldOrigin.X) * Zoom, (world.Y + _worldOrigin.Y) * Zoom);

    // ----- Pan, marquee & zoom -----

    private void OnViewportPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportGrid);
        var isBackground = IsBackgroundElement(e.OriginalSource);
        var isLeftOnBackground = point.Properties.IsLeftButtonPressed && isBackground;

        // Middle button and Space+left-drag always pan. Shift+left-drag on empty canvas
        // starts a marquee; a plain left-drag pans like a document editor.
        if (point.Properties.IsMiddleButtonPressed || (isLeftOnBackground && IsSpaceHeld()))
        {
            _dragMode = DragMode.Pan;
            _lastPointerPosition = point.Position;
            _dragMoved = false;
            ViewportGrid.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        if (isLeftOnBackground)
        {
            if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
            {
                _dragMode = DragMode.Marquee;
                _isMarqueeAdditive = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
                _isMarqueeApplied = false;
                _marqueeStart = point.Position;
                _lastPointerPosition = point.Position;
            }
            else
            {
                _dragMode = DragMode.Pan;
                _lastPointerPosition = point.Position;
            }

            _dragMoved = false;
            _marqueeHits.Clear();
            ViewportGrid.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(ViewportGrid).Position;

        if (_dragMode == DragMode.Pan)
        {
            var deltaX = position.X - _lastPointerPosition.X;
            var deltaY = position.Y - _lastPointerPosition.Y;
            if (!_dragMoved
                && Math.Abs(deltaX) + Math.Abs(deltaY) < DragThreshold)
            {
                return;
            }

            _dragMoved = true;
            WorldScrollViewer.ChangeView(
                WorldScrollViewer.HorizontalOffset - deltaX,
                WorldScrollViewer.VerticalOffset - deltaY,
                null,
                disableAnimation: true);
            _lastPointerPosition = position;
            e.Handled = true;
        }
        else if (_dragMode == DragMode.Marquee)
        {
            if (!_dragMoved
                && Math.Abs(position.X - _marqueeStart.X) + Math.Abs(position.Y - _marqueeStart.Y) < DragThreshold)
            {
                return;
            }

            _dragMoved = true;
            var band = new Rect(
                Math.Min(_marqueeStart.X, position.X),
                Math.Min(_marqueeStart.Y, position.Y),
                Math.Abs(position.X - _marqueeStart.X),
                Math.Abs(position.Y - _marqueeStart.Y));

            ShowMarquee(band);
            UpdateMarqueeSelection(band);
            e.Handled = true;
        }
    }

    private void OnViewportPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode is not (DragMode.Pan or DragMode.Marquee))
        {
            return;
        }

        // ReleasePointerCaptures() raises PointerCaptureLost synchronously, which re-enters this
        // handler. Snapshot the gesture and reset the drag state *before* releasing, so the
        // re-entrant call bails out at the guard above instead of clobbering _dragMoved — which
        // would make a completed marquee look like a plain click and wipe the new selection.
        var mode = _dragMode;
        var moved = _dragMoved;

        _dragMode = DragMode.None;
        _dragMoved = false;
        _marqueeHits.Clear();

        ViewportGrid.ReleasePointerCaptures();

        if (mode == DragMode.Marquee)
        {
            HideMarquee();

            // A click on empty canvas that never became a marquee just clears the selection.
            if (!moved && !_isMarqueeAdditive)
            {
                _viewModel?.ClearSelection();
            }
        }
        else if (!moved)
        {
            _viewModel?.ClearSelection();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Selects every node whose card intersects the rubber band. The test runs in viewport
    /// space, so what the pointer encloses on screen is exactly what gets selected at any zoom.
    /// </summary>
    private void UpdateMarqueeSelection(Rect band)
    {
        if (_viewModel is null)
        {
            return;
        }

        var hits = new List<NodeViewModel>();
        foreach (var node in _viewModel.Nodes)
        {
            if (IntersectsBand(node, band))
            {
                hits.Add(node);
            }
        }

        // Only push a change when the hit set actually moved, so dragging the band across
        // empty canvas doesn't repaint the whole graph on every pointer move. The first update
        // of a marquee always applies, so that banding blank canvas clears the old selection.
        if (_isMarqueeApplied && hits.Count == _marqueeHits.Count && !hits.Except(_marqueeHits).Any())
        {
            return;
        }

        _isMarqueeApplied = true;
        _marqueeHits.Clear();
        _marqueeHits.AddRange(hits);
        _viewModel.SelectNodes(hits, _isMarqueeAdditive);
    }

    private bool IntersectsBand(NodeViewModel node, Rect band)
    {
        var left = (node.X + _worldOrigin.X) * Zoom - WorldScrollViewer.HorizontalOffset;
        var top = (node.Y + _worldOrigin.Y) * Zoom - WorldScrollViewer.VerticalOffset;
        var right = left + node.EffectiveWidth * Zoom;
        var bottom = top + node.EffectiveHeight * Zoom;

        return left <= band.Right && right >= band.Left && top <= band.Bottom && bottom >= band.Top;
    }

    private void ShowMarquee(Rect band)
    {
        Canvas.SetLeft(MarqueeVisual, band.X);
        Canvas.SetTop(MarqueeVisual, band.Y);
        MarqueeVisual.Width = band.Width;
        MarqueeVisual.Height = band.Height;
        MarqueeVisual.Visibility = Visibility.Visible;
    }

    private void HideMarquee() => MarqueeVisual.Visibility = Visibility.Collapsed;

    /// <summary>Space+left-drag pans, mirroring the canvas conventions of design tools.</summary>
    private static bool IsSpaceHeld() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Space).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Ctrl or Shift both mean "add to the selection" for pointer gestures.</summary>
    private static bool IsAdditiveModifier(VirtualKeyModifiers modifiers) =>
        modifiers.HasFlag(VirtualKeyModifiers.Control) || modifiers.HasFlag(VirtualKeyModifiers.Shift);

    private void OnViewportPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportGrid);
        var delta = point.Properties.MouseWheelDelta;

        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            ZoomAt(point.Position, delta > 0 ? ZoomStep : 1 / ZoomStep);
        }
        else if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || point.Properties.IsHorizontalMouseWheel)
        {
            WorldScrollViewer.ChangeView(
                WorldScrollViewer.HorizontalOffset - delta * WheelPanFactor,
                null,
                null,
                disableAnimation: true);
        }
        else
        {
            WorldScrollViewer.ChangeView(
                null,
                WorldScrollViewer.VerticalOffset - delta * WheelPanFactor,
                null,
                disableAnimation: true);
        }

        e.Handled = true;
    }

    private void ZoomAtViewportCenter(double factor) =>
        ZoomAt(new Point(ViewportGrid.ActualWidth / 2, ViewportGrid.ActualHeight / 2), factor);

    private void ZoomAt(Point center, double factor)
    {
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        var world = ToWorld(center);
        _zoom = newZoom;
        UpdateWorldExtent(center, world);

        WorldScrollViewer.ChangeView(
            (world.X + _worldOrigin.X) * newZoom - center.X,
            (world.Y + _worldOrigin.Y) * newZoom - center.Y,
            null,
            disableAnimation: true);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanZoomIn)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanZoomOut)));
    }

    private Point ToWorld(Point viewport) => new(
        (viewport.X + WorldScrollViewer.HorizontalOffset) / Zoom - _worldOrigin.X,
        (viewport.Y + WorldScrollViewer.VerticalOffset) / Zoom - _worldOrigin.Y);

    private bool IsBackgroundElement(object source) =>
        ReferenceEquals(source, ViewportGrid)
        || ReferenceEquals(source, WorldScrollViewer)
        || ReferenceEquals(source, WorldCanvas)
        || ReferenceEquals(source, EdgeLayer)
        || ReferenceEquals(source, NodeLayer);

    private static Brush GetThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.DodgerBlue);

    /// <summary>Accessible name and tooltip for the resize grips.</summary>
    private static string ResizeGripName => Loc.Get("Tooltip_DragToResize");
}
