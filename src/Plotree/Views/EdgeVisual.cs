using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Plotree.Models;
using Plotree.ViewModels;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Plotree.Views;

/// <summary>
/// Renders one edge: a Bezier curve with an arrowhead, an invisible wide hit-test
/// stroke for selection, and an optional label chip at the curve midpoint.
/// </summary>
internal sealed class EdgeVisual
{
    private const double StrokeNormal = 2;
    private const double StrokeIncident = 3;
    private const double StrokeSelected = 4;
    private const double HitStroke = 14;
    private const double ArrowLength = 10;
    private const double ArrowHalfWidth = 5;
    private const double ArrowLengthEmphasized = 13;
    private const double ArrowHalfWidthEmphasized = 6.5;

    private readonly Path _curve;
    private readonly Path _hit;
    private readonly Polygon _arrow;
    private readonly Border _labelChip;
    private readonly TextBlock _labelText;
    private Point _p0;
    private Point _p1;
    private Point _p2;
    private Point _p3;
    private bool _isEmphasized;

    public EdgeVisual(EdgeViewModel edge)
    {
        Edge = edge;

        _curve = new Path { StrokeThickness = StrokeNormal, IsHitTestVisible = false };
        _arrow = new Polygon { IsHitTestVisible = false };
        _hit = new Path
        {
            Stroke = new SolidColorBrush(Colors.Transparent),
            StrokeThickness = HitStroke,
        };

        _labelText = new TextBlock { FontSize = 12 };
        _labelChip = new Border
        {
            Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", Colors.LightGray),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _labelText,
        };

        UpdateEmphasis(Edge.IsSelected, false);
    }

    public EdgeViewModel Edge { get; }

    /// <summary>The element that receives pointer events for edge selection.</summary>
    public Path HitTarget => _hit;

    public void AddTo(Canvas layer)
    {
        layer.Children.Add(_curve);
        layer.Children.Add(_arrow);
        layer.Children.Add(_hit);
        layer.Children.Add(_labelChip);
    }

    /// <summary>
    /// Emphasizes the edge for the current selection. Thickness carries the state
    /// (2 / 3 / 4 px, with a matching arrowhead), so the distinction survives High Contrast
    /// themes where every accent brush collapses onto the same system highlight colour.
    /// </summary>
    public void UpdateEmphasis(bool isSelected, bool isIncidentToSelection)
    {
        _isEmphasized = isSelected || isIncidentToSelection;

        var brush = isSelected
            ? GetThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue)
            : isIncidentToSelection
                ? GetThemeBrush("AccentFillColorSecondaryBrush", Colors.DodgerBlue)
                : GetThemeBrush("ControlStrongStrokeColorDefaultBrush", Colors.Gray);

        _curve.Stroke = brush;
        _curve.StrokeThickness = isSelected ? StrokeSelected : isIncidentToSelection ? StrokeIncident : StrokeNormal;
        _arrow.Fill = brush;
        UpdateArrow(_p2, _p3);
    }

    public void UpdateGeometry(LayoutDirection direction)
    {
        var (p0, p3) = GetAnchors(direction);

        var fromSide = Edge.Model.FromSide ?? DefaultFromSide(direction);
        var toSide = Edge.Model.ToSide ?? DefaultToSide(direction);
        var dx = p3.X - p0.X;
        var dy = p3.Y - p0.Y;
        var handle = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) * 0.5, 40, 200);

        var fromNormal = GetSideNormal(fromSide);
        var toNormal = GetSideNormal(toSide);
        var p1 = new Point(p0.X + fromNormal.X * handle, p0.Y + fromNormal.Y * handle);
        var p2 = new Point(p3.X + toNormal.X * handle, p3.Y + toNormal.Y * handle);

        _curve.Data = BuildBezier(p0, p1, p2, p3);
        _hit.Data = BuildBezier(p0, p1, p2, p3);
        (_p0, _p1, _p2, _p3) = (p0, p1, p2, p3);
        UpdateArrow(p2, p3);
        RefreshLabel();
    }

    /// <summary>Re-reads the edge label, updating chip text, visibility, and position.</summary>
    public void RefreshLabel()
    {
        var text = Edge.LabelText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _labelChip.Visibility = Visibility.Collapsed;
            return;
        }

        _labelText.Text = text;
        _labelChip.Visibility = Visibility.Visible;

        // Cubic Bezier midpoint at t = 0.5.
        var mx = 0.125 * _p0.X + 0.375 * _p1.X + 0.375 * _p2.X + 0.125 * _p3.X;
        var my = 0.125 * _p0.Y + 0.375 * _p1.Y + 0.375 * _p2.Y + 0.125 * _p3.Y;

        _labelChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_labelChip, mx - _labelChip.DesiredSize.Width / 2);
        Canvas.SetTop(_labelChip, my - _labelChip.DesiredSize.Height / 2);
    }

    private (Point From, Point To) GetAnchors(LayoutDirection direction)
    {
        var fromSide = Edge.Model.FromSide ?? DefaultFromSide(direction);
        var toSide = Edge.Model.ToSide ?? DefaultToSide(direction);
        return (GetAnchor(Edge.From, fromSide), GetAnchor(Edge.To, toSide));
    }

    private static EdgeSide DefaultFromSide(LayoutDirection direction) =>
        direction == LayoutDirection.TopToBottom ? EdgeSide.Bottom : EdgeSide.Right;

    private static EdgeSide DefaultToSide(LayoutDirection direction) =>
        direction == LayoutDirection.TopToBottom ? EdgeSide.Top : EdgeSide.Left;

    private static Point GetAnchor(NodeViewModel node, EdgeSide side) => side switch
    {
        EdgeSide.Left => new Point(node.X, node.Y + node.EffectiveHeight / 2),
        EdgeSide.Top => new Point(node.X + node.EffectiveWidth / 2, node.Y),
        EdgeSide.Bottom => new Point(node.X + node.EffectiveWidth / 2, node.Y + node.EffectiveHeight),
        _ => new Point(node.X + node.EffectiveWidth, node.Y + node.EffectiveHeight / 2),
    };

    private static Point GetSideNormal(EdgeSide side) => side switch
    {
        EdgeSide.Left => new Point(-1, 0),
        EdgeSide.Top => new Point(0, -1),
        EdgeSide.Bottom => new Point(0, 1),
        _ => new Point(1, 0),
    };

    private static PathGeometry BuildBezier(Point p0, Point p1, Point p2, Point p3)
    {
        var figure = new PathFigure { StartPoint = p0, IsFilled = false };
        figure.Segments.Add(new BezierSegment { Point1 = p1, Point2 = p2, Point3 = p3 });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void UpdateArrow(Point p2, Point p3)
    {
        var length = _isEmphasized ? ArrowLengthEmphasized : ArrowLength;
        var halfWidth = _isEmphasized ? ArrowHalfWidthEmphasized : ArrowHalfWidth;

        var vx = p3.X - p2.X;
        var vy = p3.Y - p2.Y;
        var distance = Math.Sqrt(vx * vx + vy * vy);
        if (distance < 0.001)
        {
            vx = 1;
            vy = 0;
            distance = 1;
        }

        vx /= distance;
        vy /= distance;
        var baseX = p3.X - vx * length;
        var baseY = p3.Y - vy * length;
        var nx = -vy;
        var ny = vx;

        _arrow.Points.Clear();
        _arrow.Points.Add(p3);
        _arrow.Points.Add(new Point(baseX + nx * halfWidth, baseY + ny * halfWidth));
        _arrow.Points.Add(new Point(baseX - nx * halfWidth, baseY - ny * halfWidth));
    }

    private static Brush GetThemeBrush(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
