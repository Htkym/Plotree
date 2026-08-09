using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Plotree.Helpers;
using Plotree.Models;
using Plotree.Services;
using Plotree.ViewModels;
using System.ComponentModel;
using Windows.UI;

namespace Plotree.Views;

/// <summary>How strongly a node card is emphasized by the current canvas selection.</summary>
internal enum NodeEmphasis
{
    /// <summary>Not selected and not connected to anything selected.</summary>
    None,

    /// <summary>Weakly connected to the current selection.</summary>
    Related,

    /// <summary>Selected, but not the node the details pane is bound to.</summary>
    Selected,

    /// <summary>Selected and bound to the details pane.</summary>
    Primary,
}

/// <summary>Visual card for a single plot node on the canvas.</summary>
public sealed partial class NodeCard : UserControl
{
    private static readonly SolidColorBrush SceneBrush = new(Color.FromArgb(0xFF, 0x4F, 0x6B, 0xED));
    private static readonly SolidColorBrush ChoiceBrush = new(Color.FromArgb(0xFF, 0xC7, 0x7E, 0x1E));
    private static readonly SolidColorBrush EndingBrush = new(Color.FromArgb(0xFF, 0xC7, 0x4E, 0x4E));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public NodeViewModel ViewModel { get; }

    public NodeCard(NodeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
        RebuildTagAccents();
        UpdateSelectionEmphasis(viewModel.IsSelected ? NodeEmphasis.Primary : NodeEmphasis.None);
    }

    public Brush HeaderBrush(string? customColor, NodeType type) =>
        ColorHex.Parse(customColor) is { } color ? new SolidColorBrush(color) : HeaderBrush(type);

    private static Brush HeaderBrush(NodeType type) => type switch
    {
        NodeType.Choice => ChoiceBrush,
        NodeType.Ending => EndingBrush,
        _ => SceneBrush,
    };

    public string TypeGlyph(NodeType type) => type switch
    {
        NodeType.Choice => "\uE8AB", // Switch
        NodeType.Ending => "\uE71A", // Stop
        _ => "\uE8A5",               // Document
    };

    /// <summary>
    /// Shows selection and graph adjacency without changing selection state in the view model.
    /// The states differ by ring thickness first (0 / 2 / 3 px) and only then by brush, so they
    /// stay distinguishable in High Contrast and for colour-blind users. The ring is drawn
    /// outside the card, so emphasis never reflows the card. Opacity is never used to convey
    /// state, because dimmed text fails contrast requirements.
    /// </summary>
    internal void UpdateSelectionEmphasis(NodeEmphasis emphasis)
    {
        var (ringThickness, ringBrushKey, statusKey) = emphasis switch
        {
            NodeEmphasis.Primary => (3.0, "SelectedNodeEmphasisBrush", "State_Selected"),
            NodeEmphasis.Selected => (3.0, "SelectedNodeEmphasisBrush", "State_Selected"),
            NodeEmphasis.Related => (2.0, "RelatedNodeEmphasisBrush", "State_Related"),
            _ => (0.0, "CardStrokeColorDefaultBrush", string.Empty),
        };

        EmphasisRing.BorderThickness = new Thickness(ringThickness);
        EmphasisRing.BorderBrush = GetThemeBrush(ringBrushKey);

        // The card's own stroke also darkens, so emphasis reads even where the ring is clipped.
        CardBorder.BorderBrush = GetThemeBrush(
            emphasis == NodeEmphasis.None ? "CardStrokeColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");

        AutomationProperties.SetItemStatus(this, statusKey.Length == 0 ? string.Empty : Loc.Get(statusKey));
    }

    public int TitleMaxLines(NodeDisplayMode displayMode) =>
        displayMode == NodeDisplayMode.TitleOnly ? 3 : 2;

    public int BodyMaxLines(
        NodeDisplayMode displayMode,
        string title,
        double cardWidth,
        double cardHeight)
    {
        if (displayMode == NodeDisplayMode.TitleOnly)
        {
            return 0;
        }

        if (displayMode == NodeDisplayMode.Compact)
        {
            return 1;
        }

        const double headerHeight = 24;
        const double accentWidth = 4;
        const double horizontalMargins = 16;
        const double verticalMargins = 8;
        var contentWidth = Math.Max(0, cardWidth - accentWidth - horizontalMargins);
        var contentHeight = Math.Max(0, cardHeight - headerHeight - verticalMargins);
        var budget = NodeTextLayoutCalculator.Calculate(
            title,
            displayMode,
            contentWidth,
            contentHeight,
            hasBody: true);
        return Math.Max(1, budget.BodyLineCapacity);
    }

    public Visibility BodyVisibility(NodeDisplayMode displayMode) =>
        displayMode == NodeDisplayMode.TitleOnly ? Visibility.Collapsed : Visibility.Visible;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.TagColors))
        {
            RebuildTagAccents();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Unloaded -= OnUnloaded;
    }

    private void RebuildTagAccents()
    {
        TagAccentGrid.RowDefinitions.Clear();
        TagAccentGrid.Children.Clear();

        var colors = ViewModel.TagColors;
        for (var index = 0; index < colors.Count; index++)
        {
            TagAccentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var segment = new Border
            {
                Background = ColorHex.Parse(colors[index]) is { } color
                    ? new SolidColorBrush(color)
                    : TransparentBrush,
                CornerRadius = index switch
                {
                    0 when colors.Count == 1 => new CornerRadius(7, 0, 0, 7),
                    0 => new CornerRadius(7, 0, 0, 0),
                    _ when index == colors.Count - 1 => new CornerRadius(0, 0, 0, 7),
                    _ => new CornerRadius(0),
                },
            };
            Grid.SetRow(segment, index);
            TagAccentGrid.Children.Add(segment);
        }
    }

    private static Brush GetThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Gray);
}
